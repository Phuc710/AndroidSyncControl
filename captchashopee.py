import cv2
import numpy as np
import random
import time
import math
import logging
import os
from typing import List, Tuple, Union, Optional

# Setup Logging
logging.basicConfig(level=logging.INFO, format="[%(asctime)s] [%(levelname)s] %(message)s")

class ShopeeCaptchaSolver:
    """
    Production-grade Shopee Slider Captcha Solver.
    Uses Dual-Method Detection (HSV Color Masking + Edge Canny Matching)
    and Human-like Bézier Trajectory Generation.
    """

    DEFAULT_SLIDER_START_X = 35 # Standard X position of slider handle

    @classmethod
    def load_image(cls, image_input: Union[str, bytes, np.ndarray]) -> np.ndarray:
        """Helper to convert file path, raw bytes, or numpy array to OpenCV BGR image"""
        if isinstance(image_input, str):
            img = cv2.imread(image_input)
        elif isinstance(image_input, bytes):
            img = cv2.imdecode(np.frombuffer(image_input, np.uint8), cv2.IMREAD_COLOR)
        elif isinstance(image_input, np.ndarray):
            img = image_input
        else:
            raise ValueError("Unsupported image input format.")

        if img is None:
            raise ValueError("Failed to decode image.")
        return img

    @classmethod
    def detect_gap_hsv(cls, img: np.ndarray) -> Optional[int]:
        """
        Detect Shopee target gap using HSV Blue Frame Masking (Primary Method for Web/Mobile UI)
        """
        hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
        
        # Shopee captcha blue target frame HSV range
        lower_blue = np.array([90, 50, 50])
        upper_blue = np.array([130, 255, 255])
        mask = cv2.inRange(hsv, lower_blue, upper_blue)

        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        target_x = None
        max_area = 0

        for c in contours:
            x, y, w, h = cv2.boundingRect(c)
            area = w * h
            # Filter valid target box dimensions (X > 100 to exclude initial piece)
            if x > 100 and area > max_area and 30 < w < 120 and 15 < h < 80:
                max_area = area
                target_x = x

        return target_x

    @classmethod
    def detect_gap_canny(cls, bg_img: np.ndarray, slice_img: np.ndarray) -> int:
        """
        Detect gap using Canny Edge Template Matching (Secondary Fallback Method)
        """
        bg_gray = cv2.cvtColor(bg_img, cv2.COLOR_BGR2GRAY)
        slice_gray = cv2.cvtColor(slice_img, cv2.COLOR_BGR2GRAY)

        bg_edges = cv2.Canny(bg_gray, 100, 200)
        slice_edges = cv2.Canny(slice_gray, 100, 200)

        contours, _ = cv2.findContours(slice_edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if contours:
            x, y, w, h = cv2.boundingRect(max(contours, key=cv2.contourArea))
            slice_edges = slice_edges[y:y+h, x:x+w]

        res = cv2.matchTemplate(bg_edges, slice_edges, cv2.TM_CCOEFF_NORMED)
        _, _, _, max_loc = cv2.minMaxLoc(res)

        return max_loc[0]

    @classmethod
    def get_drag_distance(cls, full_img_input: Union[str, bytes], slice_input: Optional[Union[str, bytes]] = None) -> int:
        """
        Main entry point to calculate the exact drag distance in pixels.
        """
        img = cls.load_image(full_img_input)

        # 1. Try Primary HSV Detection
        target_x = cls.detect_gap_hsv(img)
        if target_x is not None:
            logging.info(f"[HSV Engine] Detected target gap at X = {target_x}px")
            return target_x - cls.DEFAULT_SLIDER_START_X

        # 2. Fallback to Template Matching if slice image provided
        if slice_input is not None:
            slice_img = cls.load_image(slice_input)
            target_x = cls.detect_gap_canny(img, slice_img)
            logging.info(f"[Canny Engine] Detected target gap at X = {target_x}px")
            return target_x - cls.DEFAULT_SLIDER_START_X

        raise RuntimeError("Unable to detect captcha gap coordinates.")

    @staticmethod
    def generate_human_trajectory(distance: int) -> List[Tuple[int, int, float]]:
        """
        Generates realistic human drag trajectory with Ease-in-Out acceleration,
        micro-jittering on Y-axis, and small end-point overshoot.
        
        :param distance: Required total X drag distance in pixels.
        :return: List of tuples [(delta_x, delta_y, delay_seconds), ...]
        """
        tracks = []
        current_x = 0
        mid = distance * random.uniform(0.75, 0.85) # Acceleration phase up to 75-85%
        t = 0.2
        v = 0

        while current_x < distance:
            if current_x < mid:
                a = random.uniform(3.0, 6.0) # Ease-in acceleration
            else:
                a = -random.uniform(4.0, 7.0) # Ease-out deceleration

            v0 = v
            v = max(0.5, v0 + a * t)
            move = v0 * t + 0.5 * a * (t ** 2)
            
            if current_x + move > distance:
                move = distance - current_x

            current_x += move

            # Y-axis micro-jitter to pass telemetry
            y_jitter = random.choice([-1, 0, 1]) if random.random() < 0.25 else 0
            time_delay = round(random.uniform(0.012, 0.028), 3)

            tracks.append((round(move), y_jitter, time_delay))

        # Human Overshoot & Correction
        overshoot = random.randint(2, 4)
        tracks.append((overshoot, 0, 0.04))
        tracks.append((-overshoot, 0, 0.04))

        return tracks


# ==========================================
# ADB INTEGRATION ENGINE
# ==========================================
def execute_adb_swipe(device_serial: str, start_x: int, start_y: int, distance: int, scale_ratio: float = 1.0):
    """
    Executes humanized swipe command over ADB to physical/emulated device.
    """
    import subprocess

    real_distance = int(distance * scale_ratio)
    tracks = ShopeeCaptchaSolver.generate_human_trajectory(real_distance)

    logging.info(f"Executing ADB Human Swipe on [{device_serial}]: Distance={real_distance}px ({len(tracks)} steps)")

    curr_x = start_x
    curr_y = start_y

    for dx, dy, delay in tracks:
        next_x = curr_x + dx
        next_y = curr_y + dy
        duration_ms = int(delay * 1000)

        cmd = f"adb -s {device_serial} shell input swipe {curr_x} {curr_y} {next_x} {next_y} {duration_ms}"
        subprocess.run(cmd, shell=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

        curr_x, curr_y = next_x, next_y
        time.sleep(delay)


if __name__ == "__main__":
    import sys

    print("""
    ==================================================
        SHOPEE CAPTCHA SOLVER — TEST HARNESS
    ==================================================
    """)

    # 1. Test against local sample images (1.png or 2.png)
    test_files = ["1.png", "2.png"]
    found_any = False

    for img_file in test_files:
        if os.path.exists(img_file):
            found_any = True
            print(f"\n[+] Testing image file: {img_file}")
            try:
                distance = ShopeeCaptchaSolver.get_drag_distance(img_file)
                print(f"    -> Calculated Drag Distance: {distance}px")
                
                trajectory = ShopeeCaptchaSolver.generate_human_trajectory(distance)
                print(f"    -> Generated {len(trajectory)} human movement trajectory steps.")
                print(f"    -> Trajectory preview (first 3 steps): {trajectory[:3]}")
            except Exception as e:
                print(f"    [!] Error testing {img_file}: {e}")

    if not found_any:
        print("[!] No test images (1.png / 2.png) found in root directory.")
        print("[*] Place '1.png' or '2.png' in the project folder to run local detection tests.")



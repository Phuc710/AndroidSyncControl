import requests
import random
import string
import time
import json
import os
import sys

# Target API endpoints
API_URL = "https://app.botcloudphone.com/api/auth/ver2/register"
LOGIN_URL = "https://app.botcloudphone.com/api/auth/login"
CLAIM_URL = "https://app.botcloudphone.com/api/v1/trial/claim"
OUTPUT_FILE = "registered_accounts.txt"
CLAIMED_FILE = "claimed_devices.txt"
COMMON_PASSWORD = "phucbeo710"

# Data pools for realistic Vietnamese profiles & dynamic User-Agents
FIRST_NAMES = ["An", "Bình", "Cường", "Dũng", "Giang", "Hải", "Hùng", "Khánh", "Linh", "Minh", "Nam", "Phong", "Quang", "Sơn", "Thắng", "Tuấn", "Việt"]
LAST_NAMES = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương"]
PHONE_PREFIXES = ["090", "091", "093", "094", "096", "097", "098", "086", "088", "089", "032", "033", "034", "035", "036", "037", "038", "039", "070", "076", "077", "078", "079"]
CHROME_VERSIONS = ["120.0.0.0", "121.0.0.0", "122.0.0.0", "123.0.0.0", "124.0.0.0", "125.0.0.0"]

def get_random_user_agent():
    chrome_ver = random.choice(CHROME_VERSIONS)
    win_ver = random.choice(["10.0; Win64; x64", "11.0; Win64; x64"])
    return f"Mozilla/5.0 (Windows NT {win_ver}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{chrome_ver} Safari/537.36"

def generate_random_string(length):
    return ''.join(random.choices(string.ascii_lowercase + string.digits, k=length))

def generate_random_phone():
    prefix = random.choice(PHONE_PREFIXES)
    suffix = ''.join(random.choices(string.digits, k=7))
    return f"{prefix}{suffix}"

def generate_email():
    name_part = generate_random_string(random.randint(8, 12))
    return f"{name_part}@gmail.com"

def generate_password():
    return COMMON_PASSWORD

def register_account(index):
    first_name = random.choice(FIRST_NAMES)
    last_name = random.choice(LAST_NAMES)
    email = generate_email()
    password = COMMON_PASSWORD
    phone = generate_random_phone()

    payload = {
        "email": email,
        "password": password,
        "password_confirmation": password,
        "first_name": first_name,
        "last_name": last_name,
        "phone": phone,
        "brand": "bot"
    }

    headers = {
        "User-Agent": get_random_user_agent(),
        "Accept": "application/json, text/plain, */*",
        "Content-Type": "application/json",
        "Origin": "https://botcloudphone.com",
        "Referer": "https://botcloudphone.com/register",
        "Accept-Language": "vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7",
        "Connection": "keep-alive"
    }

    print(f"[*] [#{index}] Registering: {email} | Phone: {phone}")

    try:
        session = requests.Session()
        response = session.post(API_URL, json=payload, headers=headers, timeout=15)

        if response.status_code in (200, 201):
            account_entry = f"{email}:{password}\n"
            
            with open(OUTPUT_FILE, "a", encoding="utf-8") as f:
                f.write(account_entry)

            print(f"[+] [#{index}] SUCCESS -> Saved: {email}:{password}")
            
            # --- Auto Claim Logic ---
            print(f"    -> Logging in to grab token...")
            login_res = session.post(LOGIN_URL, json={"email": email, "password": password}, headers=headers, timeout=10)
            if login_res.status_code == 200:
                token = login_res.json().get("data", {}).get("access_token")
                if token:
                    claim_headers = headers.copy()
                    claim_headers["Authorization"] = f"Bearer {token}"
                    
                    # Try to claim all 3 possible trial boxes (1, 2, 14)
                    for box_id in [1, 2, 14]:
                        claim_res = session.post(CLAIM_URL, json={"boxId": box_id}, headers=claim_headers, timeout=10)
                        if claim_res.status_code == 201:
                            device_id = claim_res.json().get("deviceId")
                            print(f"[!] [#{index}] BINGO! Claimed Box {box_id}! DeviceID: {device_id}")
                            with open(CLAIMED_FILE, "a", encoding="utf-8") as cf:
                                cf.write(f"{email}|{password}|https://botcloudphone.com/devices/{device_id}/view\n")
                            break # Claimed successfully, skip other boxes
                        else:
                            print(f"    [-] Box {box_id} failed: {claim_res.json().get('message', 'Unknown Error')}")
            return True
        else:
            print(f"[-] [#{index}] FAILED ({response.status_code}): {response.text[:120]}")
            return False

    except Exception as e:
        print(f"[!] [#{index}] ERROR: {str(e)}")
        return False

def mass_register(count, min_delay=1.5, max_delay=3.5):
    success_count = 0
    for i in range(1, count + 1):
        if register_account(i):
            success_count += 1
        
        if i < count:
            delay = round(random.uniform(min_delay, max_delay), 2)
            print(f"[*] Sleeping {delay}s to simulate human jitter...")
            time.sleep(delay)

    print(f"\n[=] Completed! Successfully created {success_count}/{count} accounts.")
    print(f"[=] Accounts saved to: {os.path.abspath(OUTPUT_FILE)}")

if __name__ == "__main__":
    TARGET_COUNT = 50
    MIN_DELAY = 1.5
    MAX_DELAY = 3.5
    
    mass_register(TARGET_COUNT, min_delay=MIN_DELAY, max_delay=MAX_DELAY)



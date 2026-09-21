---
name: scrcpy-wpf-embed
description: >
  Kỹ thuật nhúng cửa sổ đồ họa scrcpy (SDL2 Win32 HWND) vào container WPF,
  xử lý đồng bộ kích thước, DPI scaling, phím chuột và ma trận tối ưu hóa video encoder.
triggers:
  - "nhúng scrcpy"
  - "lỗi màn hình đen scrcpy"
  - "resize scrcpy"
  - "scrcpy crash"
  - "tối ưu encoder"
  - "exynos codec"
---

# Skill: Scrcpy Win32 HWND Embedding & Codec Tuning

## 1. Cơ Chế Nhúng Cửa Sổ SDL2 Vào WPF Container

Scrcpy là một ứng dụng đồ họa độc lập render bằng thư viện **SDL2**. Để biến cửa sổ này thành một UserControl nhúng liền mạch trong WPF:

```
┌─────────────────────────────────────────────────────────┐
│ WPF MainWindow (HWND Host)                              │
│   ┌───────────────────────────────┐  ┌───────────────┐  │
│   │ Border (ScrcpyContainer)      │  │ ShopeeSidebar │  │
│   │                               │  │ (WPF Controls)│  │
│   │   [SetParent Win32 API]       │  │               │  │
│   │   ┌────────────────────────┐  │  │               │  │
│   │   │ Scrcpy SDL2 Window     │  │  │               │  │
│   │   │ (WS_CHILD, No Border)  │  │  │               │  │
│   │   └────────────────────────┘  │  │               │  │
│   └───────────────────────────────┘  └───────────────┘  │
└─────────────────────────────────────────────────────────┘
```

### Quy Trình Kỹ Thuật:
1. **Đặt Title Độc Bản**: Khởi động scrcpy với tham số `--window-title="Scrcpy_Embed_<Guid>"`.
2. **Thăm Dò HWND (Polling Loop)**:
   ```csharp
   IntPtr scrcpyHwnd = IntPtr.Zero;
   for (int i = 0; i < 50; i++)
   {
       scrcpyHwnd = NativeMethods.FindWindow(null, windowTitle);
       if (scrcpyHwnd != IntPtr.Zero) break;
       await Task.Delay(100);
   }
   ```
3. **Loại Bỏ Viền & Thanh Tiêu Đề Cửa Sổ**:
   ```csharp
   int style = NativeMethods.GetWindowLong(scrcpyHwnd, GWL_STYLE);
   style &= ~WS_CAPTION;
   style &= ~WS_THICKFRAME;
   style &= ~WS_MINIMIZEBOX;
   style &= ~WS_MAXIMIZEBOX;
   style |= WS_CHILD;
   NativeMethods.SetWindowLong(scrcpyHwnd, GWL_STYLE, style);
   ```
4. **Gán Cửa Sổ Cha (Reparenting)**:
   ```csharp
   NativeMethods.SetParent(scrcpyHwnd, hostContainerHwnd);
   ```
5. **Cập Nhật Tọa Độ & Kích Thước (Resize Sync)**:
   Mỗi khi container WPF thay đổi kích thước (`SizeChanged`), gọi `MoveWindow` hoặc `SetWindowPos` với cờ `SWP_NOACTIVATE | SWP_NOZORDER | SWP_FRAMECHANGED`.

---

## 2. Ma Trận Dự Phòng Video Encoder (Codec Fallback Matrix)

### Vấn Đề Đặc Thù Của Chip Exynos (Galaxy J3 Pro):
Chip Exynos 7570 có bộ xử lý phần cứng `omx.sec.avc.enc` rất nhạy cảm với bitrate cao và kích thước không chuẩn, dễ gây văng tiến trình với mã lỗi `MediaCodec error: -10000` hoặc treo màn hình đen.

### Chiến Lược Cấu Hình 2 Tầng:
1. **Tầng 1 (Primary - Hardware Encoder)**:
   ```bash
   scrcpy --video-codec=h264 --video-encoder=omx.sec.avc.enc --max-size=1024 --video-bit-rate=6M --max-fps=30
   ```
2. **Tầng 2 (Fallback - Software Encoder)**:
   Nếu tiến trình tầng 1 thoát bất thường trong vòng 2 giây đầu tiên, tự động kích hoạt chế độ dự phòng bằng encoder phần mềm của Google:
   ```bash
   scrcpy --video-codec=h264 --video-encoder=omx.google.h264.encoder --max-size=720 --video-bit-rate=4M --max-fps=30
   ```

---

## 3. Khắc Phục Lỗi Phổ Biến (Troubleshooting)

- **Màn hình scrcpy bị đen nhưng log vẫn chạy**:
  - Do tiến trình scrcpy cũ chưa bị terminate hoàn toàn, đang chiếm giữ camera/display encoder của Android.
  - Khắc phục: Chạy `taskkill /f /im scrcpy.exe` và khởi động lại supervisor.
- **Cửa sổ scrcpy bay lơ lửng ngoài màn hình WPF**:
  - `FindWindow` đã tìm thấy nhưng `SetParent` thất bại do handle của container WPF chưa sẵn sàng (`IsLoaded == false`).
  - Khắc phục: Đợi sự kiện `SourceInitialized` hoặc `Loaded` của WPF trước khi nhúng.

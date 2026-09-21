# 🏛️ Kiến Trúc Hệ Thống AndroidSyncControl (Architecture)

Tài liệu này mô tả chi tiết kiến trúc kỹ thuật của giải pháp **AndroidSyncControl**, bao gồm cấu trúc thư mục, cơ chế quản lý toolchain ADB/scrcpy, máy trạng thái kết nối và kỹ thuật nhúng cửa sổ scrcpy vào giao diện WPF.

---

## 1. Cấu Trúc Tổng Thể Dự Án

Dự án được tổ chức theo tiêu chuẩn **1 Product Chính — Runtime Tập Trung — Zero Duplicate**:

```
AndroidSyncControl (ROOT_Shopee)/
│
├── src/
│   └── AndroidSyncControl/             # Ứng dụng Desktop chính (WPF .NET 8)
│       ├── UI/
│       │   ├── Controls/               # UserControls (ShopeeSidebar, NumericUpDown)
│       │   ├── Helpers/                # Core logic & Win32 Helpers
│       │   │   ├── AndroidToolchain.cs # Resolver đường dẫn ADB & scrcpy duy nhất
│       │   │   ├── DeviceConnectionSupervisor.cs # Quản lý vòng đời kết nối
│       │   │   ├── ShopeeBypassService.cs        # Thực thi lệnh ADB bypass
│       │   │   ├── ScrcpyProfile.cs              # Tối ưu hóa encoder video
│       │   │   ├── ClipboardWatcher.cs           # Lắng nghe bộ nhớ tạm Windows
│       │   │   └── WindowMaximizeHelper.cs       # Cân bằng margin khi phóng to
│       │   ├── ViewModels/             # ViewModels hỗ trợ UI binding
│       │   └── MainWindow.xaml/.cs     # Cửa sổ chính chứa container scrcpy
│       ├── Localization/               # Bộ từ điển ngôn ngữ động (en / vi)
│       ├── Themes/                     # Bộ màu Dark / Light / System
│       ├── DataClass/                  # Dữ liệu cấu hình (SettingData.cs)
│       ├── Resources/                  # Icon và tài nguyên nhúng
│       └── AndroidSyncControl.csproj
│
├── tools/
│   └── android/                        # Thư mục chứa Binary / Dependency runtime
│       ├── adb/                        # Bản thực thi ADB duy nhất (adb.exe + dlls)
│       └── scrcpy/                     # Bản thực thi scrcpy (scrcpy.exe + SDL2 + ffmpeg)
│
├── scripts/
│   ├── build/                          # build.ps1 (biên dịch Release)
│   ├── dev/                            # run.ps1 (chạy ứng dụng môi trường dev)
│   └── setup/                          # setup.ps1 (kiểm tra môi trường thiết bị)
│
├── docs/                               # Bộ tài liệu hướng dẫn kỹ thuật
├── config/                             # File cấu hình mẫu
├── setting.json                        # Cấu hình lưu trữ của người dùng
├── run.bat / sync_control.bat          # Script khởi chạy 1-click cho người dùng
├── AndroidSyncControl.sln              # Solution Visual Studio cấp gốc
└── .gitignore
```

---

## 2. Cơ Chế AndroidToolchain (Single Canonical Resolver)

Thay vì hardcode các đường dẫn tương đối như `"../../platform-tools/adb.exe"` hay `"tools/scrcpy/adb.exe"` (dễ gây lỗi khi di chuyển hoặc chạy từ thư mục build `bin/`), lớp [`AndroidToolchain`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/UI/Helpers/AndroidToolchain.cs) hoạt động theo nguyên tắc:

1. **Quét ngược cây thư mục (Directory Traversal)**:
   - Lấy thư mục chạy hiện tại (`AppDomain.CurrentDomain.BaseDirectory`).
   - Cắt bỏ dấu gạch chéo cuối (`TrimEnd('\\', '/')`) và duyệt ngược lên các thư mục cha thông qua `DirectoryInfo.Parent`.
   - Tìm kiếm thư mục chuẩn:
     - `tools/android/adb/adb.exe`
     - `tools/android/scrcpy/scrcpy.exe`
2. **Fallback an toàn**:
   - Nếu không tìm thấy ở thư mục cha, kiểm tra ngay tại thư mục chứa file thực thi.
   - Cuối cùng trả về tên tiến trình trần (`"adb.exe"`, `"scrcpy.exe"`) để hệ thống tìm trong biến môi trường `PATH`.
3. **Đồng nhất môi trường scrcpy**:
   - Khi khởi động `scrcpy.exe`, supervisor luôn gán biến môi trường `ADB = AndroidToolchain.AdbPath` vào `ProcessStartInfo.EnvironmentVariables`. Điều này đảm bảo scrcpy và app WPF luôn dùng chung 1 adb server daemon, loại bỏ hoàn toàn lỗi *"ADB server version doesn't match"*.

---

## 3. Máy Trạng Thái Kết Nối (DeviceConnectionSupervisor)

[`DeviceConnectionSupervisor`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/UI/Helpers/DeviceConnectionSupervisor.cs) chịu trách nhiệm quản lý vòng đời kết nối với thiết bị Android theo mô hình hướng sự kiện:

### Sơ đồ chuyển đổi trạng thái

```
               ┌───────────────┐
               │  INITIALIZING │
               └───────┬───────┘
                       │
                       ▼
               ┌───────────────┐
               │   SEARCHING   │◄─────────────────────────────┐
               └───────┬───────┘                              │
                       │ (adb track-devices: phát hiện máy)   │
                       ▼                                      │
               ┌───────────────┐                              │
               │ DEVICE_FOUND  │                              │
               └───────┬───────┘                              │
                       │                                      │
                       ▼                                      │
               ┌───────────────┐                              │
               │  CONNECTING   │                              │
               └───────┬───────┘                              │
                       │ (Khởi chạy scrcpy thành công)        │
                       ▼                                      │
               ┌───────────────┐                              │
               │   CONNECTED   │                              │
               └───────┬───────┘                              │
                       │                                      │
                       │ (Rút cáp / Mất kết nối / Crash)      │
                       ▼                                      │
               ┌───────────────┐                              │
               │  DEVICE_LOST  │                              │
               └───────┬───────┘                              │
                       │                                      │
                       ▼                                      │
               ┌───────────────┐                              │
               │ RECONNECTING  ├──────────────────────────────┘
               └───────────────┘ (Thử lại với Backoff: 1s -> 2s -> 4s -> max 5s)
```

### Các ưu điểm kỹ thuật:
- **Hướng sự kiện qua `adb track-devices`**: Lắng nghe trực tiếp luồng stream stdout của lệnh `adb track-devices` thay vì dùng vòng lặp `while (true) { adb devices; sleep(1); }`. Tiết kiệm tối đa CPU máy tính.
- **Auto Reconnect & Exponential Backoff**: Khi scrcpy văng hoặc thiết bị chập chờn, supervisor tự động tính toán khoảng thời gian chờ tăng dần (1s, 2s, 4s, tối đa 5s) để thử lại mà không gây nghẽn tiến trình.
- **Không reset giao diện người dùng**: Khi mất kết nối, khung màn hình chuyển sang thông báo *"Reconnecting..."* chứ không phá hủy các thành phần điều khiển bên cạnh.

---

## 4. Kỹ Thuật Nhúng Màn Hình Scrcpy (Win32 Window Embedding)

Scrcpy là ứng dụng SDL2 độc lập. Để đưa được cửa sổ này nằm lọt vào bên trong khung của WPF Window, hệ thống thực hiện quy trình sau:

1. **Khởi chạy scrcpy với tiêu đề định danh riêng**:
   - Tham số: `--window-title "Android_Screen_{serial}_{tickCount}"`
2. **Dò tìm Handle cửa sổ (HWND)**:
   - Sử dụng Win32 API `FindWindow("SDL_app", screenTitle)` với cơ chế lặp chờ (timeout 8 giây).
3. **Tháo bỏ viền & Title bar của SDL2**:
   - Sử dụng `GetWindowLong` và `SetWindowLong` với cờ `GWL_STYLE` để loại bỏ các kiểu `WS_POPUP`, `WS_CAPTION`, `WS_THICKFRAME`.
4. **Gán làm cửa sổ con của WPF Host (SetParent)**:
   - Sử dụng Win32 API `SetParent(scrcpyHwnd, wpfContainerHwnd)`.
   - Cập nhật vị trí và kích thước đồng bộ qua `MoveWindow`.

---

## 5. Chiến Lược Tối Ưu Encoder Động & Zero-Hardcoding (ScrcpyProfile)

Toàn bộ hệ thống tuân thủ nghiêm ngặt nguyên tắc **Zero Hardcoding**: Không cố định bất kỳ model hay tên hãng nào trong mã nguồn. Mọi thiết bị Android (Samsung, Xiaomi, Oppo, Vivo, Realme, Google Pixel, Asus, OnePlus...) đều được quản lý đồng nhất thông qua cơ chế kiểm thử và tự phục hồi động:

[`ScrcpyProfile`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/UI/Helpers/ScrcpyProfile.cs) áp dụng chiến lược 3 tầng:
1. **Khởi tạo tối ưu phần cứng (Auto Hardware)**:
   - Ban đầu, scrcpy chạy với encoder phần cứng gốc của SoC (Qualcomm Snapdragon, MediaTek, Exynos, Tensor...) nhằm đạt hiệu năng render 60 FPS và độ trễ thấp nhất.
2. **Tự động bắt lỗi văng sớm (Crash Fallback Detection)**:
   - Nếu phần cứng mã hóa của máy bị lỗi driver hoặc quá tải gây văng trong 4 giây đầu (`MediaCodec$CodecException`), supervisor lập tức ghi nhận Serial thiết bị vào `DevicePreferences`.
   - Lần kết nối ngay sau đó sẽ tự động chuyển sang bộ mã hóa phần mềm chuẩn của Google:
     `--video-codec=h264 --video-encoder=OMX.google.h264.encoder`
3. **Cấu hình tối ưu độ tương thích mọi đời Android**:
   - Thêm cờ `--no-audio`: Loại bỏ nguy cơ crash demuxer âm thanh trên các phiên bản Android cũ (Android 5.0 - 9.0) mà vẫn giữ kết nối siêu nhẹ.
   - Giới hạn kích thước truyền tối đa 720p (`--max-size 720`) để đảm bảo tốc độ phản hồi 30 FPS mượt mà trên mọi phân khúc cấu hình.

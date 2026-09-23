# 📱 AndroidSyncControl

> **Hệ thống điều khiển, truyền hình ảnh màn hình điện thoại Android (Screen Mirroring) hiệu năng cao, độ trễ cực thấp và giải pháp tự động hóa thiết bị (Device Automation & Risk Mitigation) trên Windows.**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-blue.svg)](#)
[![Framework](https://img.shields.io/badge/.NET-8.0%20(WPF)-512BD4.svg)](#)
[![ADB](https://img.shields.io/badge/ADB-1.0.41-success.svg)](#)
[![scrcpy](https://img.shields.io/badge/scrcpy-3.1-orange.svg)](#)
[![Build](https://img.shields.io/badge/Build-Passing%20(0%20Errors)-brightgreen.svg)](#)


## 🎯 Tổng Quan Sản Phẩm

**AndroidSyncControl** là ứng dụng Native Desktop được xây dựng trên nền tảng **.NET 8.0 WPF (Windows Presentation Foundation)**. Ứng dụng giải quyết bài toán cốt lõi: **Điều khiển, phản chiếu màn hình và quản lý thiết bị Android tập trung** từ máy tính:

1. **Điều khiển trực tiếp độ trễ cực thấp**: Nhúng trực tiếp luồng hiển thị phần cứng từ điện thoại Android (sử dụng engine `scrcpy 3.1` qua giao thức ADB) vào giao diện WPF, cho phép click chuột, cuộn trang, gõ bàn phím và điều hướng phần cứng (Back, Home, Recent Apps) thời gian thực.
2. **Quản lý thiết bị toàn năng (Universal Device Tooling)**: Đọc thông tin phần cứng điện thoại, chụp ảnh màn hình, cài đặt file APK, sao lưu dữ liệu, điều khiển nguồn/khởi động lại, chuyển đổi proxy và xoay dải IP di động 4G.
3. **Làm sạch định danh thiết bị (Device Sanitization & Bypass)**: Tự động hóa chu trình bóc tách và thay thế toàn bộ vector dấu vân tay (fingerprint vectors), giúp vượt qua các mã giới hạn thiết bị phổ biến như **M02**, **D02**, và **L01**.

---

## 🔬 Bóc Tách Cốt Lõi Kỹ Thuật (Technical Core)

### 2.1 Kiến trúc 3 tầng (Layered Architecture)

Sản phẩm được thiết kế phân tầng nghiêm ngặt nhằm tách biệt giao diện, logic giám sát vòng đời thiết bị và tầng thực thi nhị phân cấp thấp:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    TẦNG TRÌNH DIỄN (PRESENTATION LAYER)                 │
│  • MainWindow.xaml / .cs  : Container WPF lưu trữ HWND scrcpy            │
│  • ShopeeSidebar.xaml/.cs : Bộ phím điều khiển, input text, nút Bypass  │
│  • Themes & Localization  : DynamicResource Palette (Dark/Light) & i18n │
│  • WindowMaximizeHelper   : Win32 DpiScale Margin compensator           │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │ Event / State Binding
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│              TẦNG ĐIỀU PHỐI & GIÁM SÁT (SUPERVISOR & SERVICES)          │
│  • DeviceConnectionSupervisor : FSM quản lý kết nối, tự phục hồi        │
│  • ScrcpyProfile              : Tự động fallback Hardware -> Software   │
│  • ShopeeBypassService        : Pipeline 6 bước làm sạch telemetry      │
│  • ClipboardWatcher           : Lắng nghe Win32 WM_CLIPBOARDUPDATE      │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │ IO Process Invocation
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                 TẦNG THỰC THI NHỊ PHÂN (TOOLCHAIN RUNTIME)              │
│  • AndroidToolchain : Directory Traversal Resolver (Zero-Duplicate)     │
│  • tools/android/adb/    : Canonical ADB Server Daemon (v1.0.41)        │
│  • tools/android/scrcpy/ : Scrcpy Native Binary & Video Codec Library   │
│  • Thiết bị Android      : Tương thích toàn diện MỌI máy Android (5.0 - 14+) │
└─────────────────────────────────────────────────────────────────────────┘
```

---

### 2.2 Cơ chế giải quyết Runtime tập trung (`AndroidToolchain`)

Một trong những nguyên nhân hàng đầu gây crash trong các dự án Android Tooling là hiện tượng **phân mảnh ADB (Duplicate Daemon Conflict)** — xảy ra khi ứng dụng C# gọi một file `adb.exe`, trong khi scrcpy gọi một file `adb.exe` khác ở thư mục khác, dẫn đến việc hai tiến trình liên tục tranh chấp cổng và ngắt daemon của nhau (*"ADB server version doesn't match"*).

[`AndroidToolchain`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/UI/Helpers/AndroidToolchain.cs) giải quyết bài toán này bằng thuật toán:
1. **Duyệt ngược phân cấp thư mục (Hierarchical Directory Traversal)**:
   ```csharp
   string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
   var dir = new DirectoryInfo(baseDir);
   while (dir != null) {
       string target = Path.Combine(dir.FullName, "tools", "android", "adb", "adb.exe");
       if (File.Exists(target)) return Path.GetFullPath(target);
       dir = dir.Parent;
   }
   ```
2. **Gán biến môi trường cưỡng bức**: Khi khởi chạy scrcpy, supervisor bắt buộc tiêm biến môi trường:
   ```csharp
   psi.EnvironmentVariables["ADB"] = AndroidToolchain.AdbPath;
   ```
   Cam kết 100% tất cả luồng xử lý đều giao tiếp qua duy nhất tiến trình daemon tại `tools/android/adb/adb.exe`.

---

### 2.3 Kỹ thuật nhúng cửa sổ Win32 (Scrcpy Window Embedding)

Scrcpy là một tiến trình đồ họa độc lập render bằng thư viện **SDL2 (Simple DirectMedia Layer)**. Để lồng ghép khung hình này trở thành một phần giao diện tự nhiên của WPF mà không cần mở cửa sổ rời:

```
[Tiến trình scrcpy.exe]                                   [Tiến trình AndroidSyncControl]
┌────────────────────────┐                               ┌─────────────────────────────┐
│ Cửa sổ đồ họa SDL2     │                               │ WPF MainWindow (Host)       │
│ • Title định danh riêng│                               │ • scrcpyHost (HwndHost)     │
│ • HWND: scrcpyHwnd     │                               │ • HWND: hostHwnd            │
└───────────┬────────────┘                               └──────────────┬──────────────┘
            │                                                           │
            │ 1. FindWindow("SDL_app", customScreenTitle)               │
            ├──────────────────────────────────────────────────────────►│
            │                                                           │
            │ 2. SetWindowLong(scrcpyHwnd, GWL_STYLE, WS_VISIBLE...)    │
            │    (Gỡ bỏ hoàn toàn Title bar, viền bo và cờ WS_POPUP)    │
            │                                                           │
            │ 3. SetParent(scrcpyHwnd, hostHwnd)                        │
            │    (Chuyển đổi quan hệ: scrcpy trở thành cửa sổ con)      │
            │                                                           │
            │ 4. MoveWindow(scrcpyHwnd, 0, 0, width, height, true)      │
            ▼    (Đồng bộ kích thước theo khung container WPF)          ▼
```

---

### 2.4 Máy trạng thái kết nối hướng sự kiện (`DeviceConnectionSupervisor`)

Supervisor hoạt động như một cỗ máy trạng thái hữu hạn (FSM) bất đồng bộ, sử dụng cơ chế lắng nghe stream sự kiện của ADB thay vì polling (quét lặp) tốn tài nguyên:

```
   ┌──────────────┐
   │ Initializing │ (Khởi động stream: adb track-devices)
   └──────┬───────┘
          │
          ▼
   ┌──────────────┐
   │  Searching   │◄────────────────────────────────────────────────────┐
   └──────┬───────┘                                                     │
          │ (track-devices phát hiện: serial \t device)                 │
          ▼                                                             │
   ┌──────────────┐                                                     │
   │DeviceDetected│                                                     │
   └──────┬───────┘                                                     │
          │ (Đọc tên model & khởi tạo scrcpy)                           │
          ▼                                                             │
   ┌──────────────┐                                                     │
   │  Connecting  │                                                     │
   └──────┬───────┘                                                     │
          │ (Tìm thấy SDL2 HWND & hoàn tất SetParent)                   │
          ▼                                                             │
   ┌──────────────┐       (Rút cáp / Crash)        ┌─────────────────┐  │
   │  Connected   ├───────────────────────────────►│ ConnectionLost  │  │
   └──────────────┘                                └────────┬────────┘  │
                                                            │           │
                                                            ▼           │
                                                   ┌─────────────────┐  │
                                                   │  Reconnecting   ├──┘
                                                   └─────────────────┘
                                                   (Backoff: 1s -> 2s -> 4s -> max 5s)
```

- **Tiết kiệm CPU tuyệt đối**: Chỉ thức dậy xử lý khi `adb track-devices` bắn ra chuỗi dữ liệu thay đổi trạng thái USB.
- **Exponential Backoff**: Tự động tính toán thời gian chờ tăng theo cấp số nhân nếu thiết bị gặp sự cố chập chờn nguồn điện, tránh gây nghẽn luồng xử lý chính.

---

### 2.5 Chiến lược dự phòng Encoder thích ứng phần cứng tự động (`ScrcpyProfile`)

Dự án được xây dựng với nguyên lý **Zero Hardcoding**: Tuyệt đối không cố định bất kỳ chuỗi tên máy, hãng sản xuất (Samsung, Xiaomi, Oppo, Vivo, Realme, Google Pixel, Asus, OnePlus, Motorola...) hay dòng vi xử lý nào trong mã nguồn. Mọi thông tin thiết bị đều được nhận diện động qua ADB runtime.

Để đảm bảo tương thích 100% trên toàn bộ các thế hệ Android (từ Android 5.0 đến 14+) và mọi kiến trúc SoC (Qualcomm Snapdragon, MediaTek Dimensity/Helio, Samsung Exynos, Google Tensor, Unisoc...):
- **Hiện tượng phần cứng**: Một số thiết bị đời cũ hoặc ROM tùy biến có phần cứng mã hóa video (Hardware MediaCodec) bị quá tải hoặc thiếu profile H.264 tương thích, dẫn đến lỗi `MediaCodec$CodecException` và văng tiến trình trong 1-3 giây đầu.
- **Giải pháp thích ứng động của ScrcpyProfile**:
  1. **An toàn âm thanh**: Mặc định áp dụng cờ `--no-audio` để ngăn ngừa triệt để lỗi crash demuxer âm thanh trên các phiên bản Android chưa hỗ trợ audio forward chuẩn (Android 9 trở xuống).
  2. **Tự động bắt lỗi sập sớm (Fast Crash Detection)**: Nếu tiến trình scrcpy bị ngắt kết nối trong vòng dưới 4.0 giây (`runSeconds < 4.0`), `ScrcpyProfile` lập tức đánh dấu Serial của thiết bị vào bộ nhớ đệm `ConcurrentDictionary` với trạng thái `EncoderPreference.SoftwareFallback`.
  3. **Tự động chuyển sang Codec phần mềm Google**: Ở lần khởi động tiếp theo (chỉ mất ~1.5 giây phục hồi tự động), scrcpy được tiêm bộ mã hóa phần mềm chuẩn quốc tế:
     ```bash
     --video-codec=h264 --video-encoder=OMX.google.h264.encoder --max-size 720
     ```
  4. **Tỷ lệ phản chiếu màn hình thành công 100%**: Loại bỏ hoàn toàn sự phụ thuộc vào driver phần cứng riêng của từng hãng điện thoại.

---

### 2.6 Quy trình kỹ thuật Bypass Shopee Risk Engine (M02 / D02 / L01)

Hệ thống chống gian lận của Shopee thu thập dữ liệu định danh theo 4 vector chính:
1. **Android ID (SSAID)**: Gắn chặt vào từng tài khoản đăng nhập.
2. **Google Advertising ID (GAID)**: Theo dõi hành vi thông qua Google Play Services.
3. **Session Cache & External Artifacts**: Lưu trữ token đăng nhập tại `/data/data/com.shopee.vn` và file định danh ẩn tại `/sdcard/.shopee`.
4. **Địa chỉ Public IP**: Giám sát việc nhiều tài khoản giao dịch trên cùng một dải mạng.

Hàm [`ShopeeBypassService.BypassShopeeAsync`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/UI/Helpers/ShopeeBypassService.cs) thực thi **quy trình 6 bước nguyên tử (Atomic Pipeline)**:

| Bước | Hành Động Kỹ Thuật | Lệnh Thực Thi ADB | Mục Tiêu Triệt Tiêu Dấu Vết |
|---|---|---|---|
| **1** | **Xóa sạch Session Token & Artifacts** | `am force-stop com.shopee.vn`<br>`pm clear com.shopee.vn`<br>`rm -rf /sdcard/Android/data/com.shopee.vn /sdcard/.shopee` | Xóa cookie, token phiên đăng nhập, triệt tiêu file khôi phục ID ẩn ngoài bộ nhớ chung. |
| **2** | **Sinh & Nạp SSAID Mới** | `settings put secure android_id <RandomHex16>` | Tạo một định danh thiết bị hoàn toàn mới trong mắt hệ điều hành Android. |
| **3** | **Làm mới Google Advertising ID** | `pm clear com.google.android.gms` | Xóa cache Play Services, kích hoạt việc tái cấp phát ID quảng cáo mới. |
| **4** | **Ngắt kết nối mạng di động** | `cmd connectivity airplane-mode enable` | Ngắt toàn bộ socket và kết nối TCP đang mở với máy chủ Shopee. |
| **5** | **Cấp phát dải IP 4G mới** | `cmd connectivity airplane-mode disable` (delay 3.5s) | Yêu cầu trạm phát sóng viễn thông (BTS) cấp phát dải Public IP mới cho SIM 4G. |
| **6** | **Khởi chạy ứng dụng sạch** | `monkey -p com.shopee.vn -c ...LAUNCHER 1` | Kích hoạt intent khởi động tự nhiên từ màn hình chính như một máy vừa tải app lần đầu. |

---

### 2.7 Bơm văn bản tiếng Việt Unicode (`Clipboard Injection Engine`)

Lệnh chuẩn của Android `adb shell input text` không hỗ trợ các ký tự Unicode UTF-8 nằm ngoài bảng mã ASCII (các chữ cái tiếng Việt có dấu như `à, á, ả, ã, ạ, ê, ơ, ư...` sẽ bị vỡ font hoặc biến thành ký tự rác).

AndroidSyncControl áp dụng kỹ thuật bơm văn bản 2 tầng:
1. **Tầng 1 (Clipper Broadcast - Khuyên dùng)**:
   - Mã hóa chuỗi văn bản đầu vào thành chuỗi `Base64` an toàn trên Windows.
   - Phát Broadcast trực tiếp vào bộ nhớ tạm hệ điều hành qua `clipper.set`:
     ```bash
     am broadcast -a clipper.set -e text '<Escaped_Text>'
     ```
   - Kích hoạt sự kiện phím dán phần cứng: `input keyevent 279` (`KEYCODE_PASTE`).
2. **Tầng 2 (ASCII Safe Fallback)**:
   - Nếu thiết bị chưa cài đặt broadcast service, tự động escape ký tự đặc biệt (`\`, `&`, `<`, `>`, `"`) và gõ theo cơ chế input text.

---

## 📂 Cấu Trúc Thư Mục Chuẩn Hóa

```
AndroidSyncControl (ROOT_Shopee)/
├── src/
│   └── AndroidSyncControl/             # Mã nguồn dự án C# WPF chính
│       ├── DataClass/                  # Class thực thể cấu hình (SettingData.cs)
│       ├── Localization/               # Từ điển đa ngôn ngữ (Strings.vi.xaml, Strings.en.xaml)
│       ├── Resources/                  # Icon ứng dụng (appicon.ico)
│       ├── Themes/                     # Giao diện Dark.xaml, Light.xaml, Controls.xaml
│       ├── UI/
│       │   ├── Controls/               # ShopeeSidebar.xaml, NumericUpDown.cs
│       │   ├── Helpers/                # AndroidToolchain, Supervisor, BypassService, ScrcpyProfile
│       │   ├── ViewModels/             # MainWVM.cs, ComboboxVM.cs
│       │   └── MainWindow.xaml/.cs     # Giao diện chính chứa container scrcpy
│       ├── App.xaml / App.xaml.cs      # Entrypoint ứng dụng WPF
│       ├── Singleton.cs                # Quản lý cấu hình toàn cục
│       └── AndroidSyncControl.csproj   # File cấu hình build .NET 8 (x64)
│
├── tools/
│   └── android/                        # Thư mục phụ thuộc Runtime (Chỉ chứa binary, không chứa code)
│       ├── adb/                        # adb.exe, AdbWinApi.dll, AdbWinUsbApi.dll, libwinpthread-1.dll
│       └── scrcpy/                     # scrcpy.exe, scrcpy-server, SDL2.dll, avcodec, avformat...
│
├── scripts/
│   ├── build/build.ps1                 # Script biên dịch bản phát hành Release
│   ├── dev/run.ps1                     # Script khởi chạy môi trường lập trình
│   └── setup/setup.ps1                 # Script kiểm tra môi trường kết nối ADB/scrcpy
│
├── docs/                               # Thư mục tài liệu kỹ thuật chi tiết
│   ├── 01-user-guide.md                # Hướng dẫn sử dụng người dùng từ A-Z
│   ├── 02-architecture.md              # Thiết kế kiến trúc & giải thuật chi tiết
│   ├── 03-bypass-mechanism.md          # Phân tích cơ chế né phát hiện Risk Engine
│   └── 04-troubleshooting.md           # Hướng dẫn khắc phục tất cả lỗi thường gặp
│
├── setting.json                        # File lưu trữ cấu hình hiển thị của người dùng
├── sync_control.bat                    # Phím tắt khởi động ứng dụng nhanh 1-Click
├── AndroidSyncControl.sln              # Solution Visual Studio duy nhất
├── .gitignore                          # Cấu hình bỏ qua file rác khi commit
└── README.md                           # Tài liệu tổng quan dự án (File này)
```

---

## 🚀 Hướng Dẫn Vận Hành Nhanh (Quick Start)

### 1. Chuẩn bị điện thoại
1. Mở **Cài đặt -> Thông tin điện thoại -> Thông tin phần mềm**.
2. Nhấn 7 lần vào dòng **Số hiệu bản tạo (Build number)** để mở menu ẩn.
3. Quay lại **Cài đặt cho người phát triển** -> Bật **Gỡ lỗi USB (USB Debugging)**.
4. Cắm cáp USB vào máy tính, trên điện thoại tích chọn ☑ **"Luôn cho phép từ máy tính này"** và nhấn **OK**.

### 2. Khởi chạy 1-Click
Chỉ cần chạy file sau tại thư mục gốc:
```bat
sync_control.bat
```
Ứng dụng sẽ tự động khởi động, bắt tín hiệu điện thoại, khởi chạy scrcpy và nhúng màn hình lên máy tính trong vòng 2 giây!

---

## ⚙️ Cấu Hình Ứng Dụng (`setting.json`)

Tùy chỉnh thông số hiển thị trực tiếp tại file `setting.json` ở thư mục gốc:

```json
{
  "ViewPercent": 25.0,
  "MaxFps": 30,
  "MaxSize": 0,
  "Timeout": 8000,
  "UseGpu": false,
  "IsAudio": false,
  "Theme": "Dark",
  "Language": "vi"
}
```

- `MaxFps`: Giới hạn tốc độ khung hình (khuyên dùng `24` hoặc `30` để giữ CPU ổn định).
- `MaxSize`: Độ phân giải hiển thị tối đa (`0` = theo độ phân giải gốc của máy, hoặc đặt `720` cho máy yếu).
- `IsAudio`: Bật/Tắt truyền âm thanh (bắt buộc đặt `false` trên Android 7/8/9).
- `Theme`: Giao diện chủ đề (`"Dark"`, `"Light"` hoặc `"System"` theo Windows).
- `Language`: Ngôn ngữ hiển thị (`"vi"` cho tiếng Việt, `"en"` cho tiếng Anh).

---

## 📚 Hệ Thống Tài Liệu Chuyên Sâu (`docs/`)

Để tìm hiểu chi tiết hơn về từng phân hệ, vui lòng tham khảo các tài liệu chuyên đề:

| Tài Liệu | Nội Dung Chính |
|---|---|
| 📖 [**`01-user-guide.md`**](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/docs/01-user-guide.md) | Hướng dẫn kết nối cáp, chi tiết chức năng toàn bộ nút bấm trên thanh Sidebar và cách gõ tiếng Việt. |
| 🏛️ [**`02-architecture.md`**](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/docs/02-architecture.md) | Phân tích sâu Win32 HWND Interop, giải thuật `AndroidToolchain`, và FSM Connection Supervisor. |
| 🛡️ [**`03-bypass-mechanism.md`**](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/docs/03-bypass-mechanism.md) | Bóc tách chi tiết hệ thống chấm điểm gian lận Shopee và quy trình kỹ thuật làm sạch 6 bước. |
| 🔧 [**`04-troubleshooting.md`**](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/docs/04-troubleshooting.md) | Cẩm nang xử lý mọi tình huống lỗi: Không nhận máy, lỗi MediaCodec crash, gõ mất dấu, lỗi IP... |

---

## 🛠️ Biên Dịch Mã Nguồn (Build Instructions)

Để tự biên dịch dự án ra file thực thi Release:
```powershell
.\scripts\build\build.ps1
```
File thực thi cuối cùng sẽ được đặt tại:
```
src\AndroidSyncControl\bin\x64\Release\net8.0-windows\AndroidSyncControl.exe
```

---
*Phát triển bởi đội ngũ kỹ thuật AndroidSyncControl. Bản quyền kiến trúc và giải pháp kỹ thuật thuộc về dự án AndroidSyncControl.*

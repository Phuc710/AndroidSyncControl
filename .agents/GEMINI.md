# ROOT_Shopee — Project Context (GEMINI.md)

## Mục Tiêu Dự Án
Hệ thống điều khiển màn hình Android (Screen Mirroring) hiệu năng cao và giải pháp tự động hóa Bypass Shopee Risk Engine (lỗi M02 / D02 / L01) trên Windows.
Hỗ trợ phổ quát **TOÀN BỘ thiết bị Android** (từ Android 5.0 Lollipop đến Android 14+), áp dụng nghiêm ngặt nguyên lý **Zero Hardcoding** — không phụ thuộc hay gán cố định bất kỳ model hay thương hiệu điện thoại nào.

## Tech Stack
- **Application Platform:** C# .NET 8.0 WPF (`net8.0-windows`, Architecture: x64)
- **Toolchain Runtime:**
  - ADB: `tools/android/adb/adb.exe` (Canonical Daemon v1.0.41)
  - scrcpy: `tools/android/scrcpy/scrcpy.exe` (v3.1, SDL2 HWND embedding qua Win32 `SetParent`)
- **Target devices:** TOÀN BỘ điện thoại Android (Samsung, Xiaomi, Redmi, Oppo, Vivo, Realme, Google Pixel, OnePlus, Asus, Motorola, Tecno...)
- **Target app:** `com.shopee.vn`

## Project Structure
```
ROOT_Shopee/
├── .agents/
│   ├── GEMINI.md                       # Project Context chính
│   ├── rules/
│   │   ├── 01-csharp-wpf-architecture.md # Tiêu chuẩn C# .NET 8, MVVM, DynamicResource, Win32 P/Invoke
│   │   ├── 02-adb-telemetry-safety.md    # Bề mặt phát hiện Shopee & quy chuẩn an toàn ADB
│   │   ├── 03-keeprule.md                # Bộ Keeprules bất biến KR-01 đến KR-08
│   │   ├── 04-senior-code-standards.md   # Chuẩn kỹ sư SC-11 Verified Knowledge & Workflow
│   │   └── INTELLIGENT EXECUTION.md      # Chuẩn SC-12 Intelligent Decision & Real-World Execution
│   └── skills/
│       ├── shopee-bypass-engine/       # Điều phối pipeline bypass M02/D02/L01
│       ├── scrcpy-wpf-embed/           # Kỹ thuật nhúng HWND SDL2 & tuning video encoder
│       ├── adb-toolchain-manager/      # Quản trị single canonical daemon & reaper
│       ├── adb-fingerprint/            # (Reference) Shell script reset device fingerprint
│       └── xposed-setup/               # (Root route) Cẩm nang TWRP/Magisk/LSPosed
├── src/
│   └── AndroidSyncControl/             # C# WPF Native Desktop Application
│       ├── Agent/                      # Subsystem tự học & thực thi thông minh (SC-11 & SC-12)
│       │   ├── Domain/                 # Playbook, Experience, TestCase, EvaluationReport
│       │   ├── Abstractions/           # IPlaybookMatcher, IPlaybookEvaluator, IAgentReflector...
│       │   ├── Matching/               # Bm25PlaybookMatcher, Bm25Index
│       │   ├── Learning/               # AgentReflector, PlaybookOptimizer
│       │   ├── Evaluation/             # PlaybookEvaluator (Quality Gate 4 trọng số)
│       │   ├── Execution/              # AndroidActionEngine, PlaybookExecutor
│       │   ├── Repository/             # JsonPlaybookRepository (agent-data/)
│       │   └── Orchestration/          # AgentOrchestrator
│       ├── UI/
│       │   ├── Controls/               # ShopeeSidebar.xaml, NumericUpDown.cs
│       │   ├── Helpers/                # AndroidToolchain, Supervisor, BypassService, ScrcpyProfile
│       │   ├── ViewModels/             # MainWVM.cs, ComboboxVM.cs
│       │   └── MainWindow.xaml/.cs     # Scrcpy Win32 HWND Container
│       ├── Localization/               # Strings.vi.xaml, Strings.en.xaml
│       ├── Themes/                     # Dark.xaml, Light.xaml
│       ├── App.xaml / App.xaml.cs
│       └── AndroidSyncControl.csproj
├── tools/android/
│   ├── adb/                            # adb.exe, AdbWinApi.dll, AdbWinUsbApi.dll, libwinpthread-1.dll
│   └── scrcpy/                         # scrcpy.exe, scrcpy-server, SDL2.dll, avcodec, avformat...
├── scripts/
│   ├── build/build.ps1                 # Biên dịch Release
│   ├── dev/run.ps1                     # Chạy môi trường Dev
│   └── setup/setup.ps1                 # Kiểm tra môi trường kết nối
├── docs/                               # 01-user-guide, 02-architecture, 03-bypass-mechanism, 04-troubleshooting, 05-agent-learning-system
├── run.bat / sync_control.bat          # 1-Click Launchers
└── AndroidSyncControl.sln              # Visual Studio Solution duy nhất
```

## Quy Ước Kiến Trúc & Quy Tắc (Rules & Skills)
- **Kiến trúc C# WPF:** Tham khảo `.agents/rules/01-csharp-wpf-architecture.md`
- **An toàn ADB & Telemetry:** Tham khảo `.agents/rules/02-adb-telemetry-safety.md`
- **Keeprules Bất Biến (Bắt buộc tuân thủ):** Tham khảo `.agents/rules/03-keeprule.md`
- **Skills load on-demand:**
  - `shopee-bypass-engine`: Vận hành và khắc phục sự cố bypass Shopee M02 / D02 / L01.
  - `scrcpy-wpf-embed`: Xử lý nhúng cửa sổ Win32 HWND, resize, DPI scaling, MediaCodec crash.
  - `adb-toolchain-manager`: Quản lý daemon ADB duy nhất, giải phóng port 5037, xử lý zombie processes.
  - `adb-fingerprint`: Chi tiết kỹ thuật fingerprinting qua shell ADB.
  - `xposed-setup`: Hướng dẫn root nâng cao cho Samsung Galaxy J3 Pro.

## Shopee Detection Surface & Bypass Pipeline (Universal No-Root)
`ShopeeBypassService` thực thi pipeline 6 bước phổ quát qua ADB, hoạt động trên mọi điện thoại Android:
1. **Force Stop & Clear Data:** `am force-stop com.shopee.vn` + `pm clear com.shopee.vn` + xóa thư mục `/sdcard/.shopee` và `/sdcard/Android/data/com.shopee.vn`.
2. **Rotate Android ID (SSAID):** Sinh hex crypto-random 16 ký tự -> `settings put secure android_id <new_id>`.
3. **Reset GAID (Google Ads ID):** `pm clear com.google.android.gms`.
4. **Disconnect Sockets:** `cmd connectivity airplane-mode enable`.
5. **Acquire New Cellular IP:** `cmd connectivity airplane-mode disable` (delay tối thiểu 3.5s).
6. **Launch Fresh Intent:** `monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1`.

## Quy Tắc Kiến Trúc Bất Biến Cốt Tử (Core Invariants)
1. **Zero Hardcoding (KR-06):** Không hardcode model string trong source code, XAML hay script. Mọi thông số được truy vấn động tại runtime.
2. **Centralized Toolchain (KR-01):** Mọi lệnh ADB và tiến trình scrcpy bắt buộc thông qua `AndroidToolchain.cs` chỉ trỏ về `tools/android/`. Tuyệt đối không gọi ADB bên ngoài hay tạo bản sao binary.
3. **Adaptive Video Codec (KR-05):** `ScrcpyProfile` tự động thử hardware encoder gốc trước, nếu gặp crash sớm sẽ tự động chuyển sang `OMX.google.h264.encoder` cho thiết bị đó.
4. **Win32 Process Cleanup (KR-02):** Khi đóng app hoặc ngắt thiết bị, toàn bộ tiến trình `scrcpy.exe` và các stream liên đới phải bị kill dứt điểm, không để zombie process khóa cổng hoặc encoder.

# 🔒 BỘ KEEPRULES BẤT BIẾN — SHOPEECONTROL (03-keeprule.md)

Tài liệu này định nghĩa các nguyên tắc cốt lõi mang tính **bất biến (Invariants)** của toàn bộ hệ thống ShopeeControl. Bất kỳ thay đổi mã nguồn, refactor hay thêm tính năng mới nào vi phạm các quy tắc dưới đây đều bị coi là lỗi nghiêm trọng (Fatal Architecture Violation).

---

## 🛑 KR-01: Canonical ADB Resolver Duy Nhất (Zero Duplicate Daemon)
- **Quy định:** Toàn bộ lệnh gọi ADB (từ C#, PowerShell, hay subprocess scrcpy) bắt buộc phải giải quyết qua `AndroidToolchain.AdbPath` trỏ đến `tools/android/adb/adb.exe`.
- **Cấm:**
  - Tuyệt đối không hardcode đường dẫn tương đối như `"../../platform-tools/adb.exe"`.
  - Không gọi lệnh `adb` trần ngoài PATH hệ thống mà chưa qua resolver.
  - Khi kích hoạt `scrcpy.exe`, **bắt buộc** gán biến môi trường `ProcessStartInfo.EnvironmentVariables["ADB"] = AndroidToolchain.AdbPath`.
- **Hệ quả vi phạm:** Xuất hiện hiện tượng phân mảnh daemon, dẫn đến lỗi crash liên hoàn *"ADB server version (X) doesn't match this client (Y); killing..."*.

---

## 🛑 KR-02: Quản Lý Vòng Đời Win32 HWND & Diệt Tiến Trình Con (Zombie Reaper)
- **Quy định:** Cửa sổ `scrcpy.exe` được nhúng vào WPF bằng Win32 `SetParent`. Mọi vòng đời của tiến trình đồ họa này phải gắn chặt với vòng đời ứng dụng chính.
- **Cấm:**
  - Không để lại bất kỳ tiến trình `scrcpy.exe` hay `adb.exe` mồ côi (zombie) nào khi tắt app WPF hoặc khi thiết bị ngắt kết nối.
  - Phải gọi `process.Kill(entireProcessTree: true)` kèm timeout và dọn dẹp các handle `IntPtr` đã cấp phát.
- **Hệ quả vi phạm:** Tiến trình mồ côi giữ khóa (lock) video encoder phần cứng của chip Exynos/Snapdragon và chiếm port 5037, khiến lần kết nối tiếp theo bị đen màn hình hoặc treo vô hạn.

---

## 🛑 KR-03: Thứ Tự Bất Biến Của Bypass Pipeline 6 Bước
- **Quy định:** `ShopeeBypassService` bắt buộc phải thực thi theo đúng trình tự tuần tự 6 bước:
  1. `am force-stop com.shopee.vn` + `pm clear com.shopee.vn` + xóa sạch cache `/sdcard/Android/data/com.shopee.vn` & `/sdcard/.shopee`.
  2. Sinh và nạp Android ID (SSAID) mới (16 ký tự hex crypto-random) qua `settings put secure android_id <new_id>`.
  3. Reset Google Advertising ID (GAID) qua `pm clear com.google.android.gms`.
  4. Ngắt kết nối mạng: `cmd connectivity airplane-mode enable`.
  5. Bật lại kết nối mạng: `cmd connectivity airplane-mode disable` và **bắt buộc trễ tối thiểu 3.5s - 5s** để thiết bị bắt sóng và cấp IP Cellular 4G mới.
  6. Khởi động Shopee bằng Intent Launcher: `monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1`.
- **Cấm:** Tuyệt đối không khởi chạy ứng dụng Shopee trước khi dải IP mới được thiết lập và SSAID được ghi nhận.
- **Hệ quả vi phạm:** Shopee Risk Engine kích hoạt cờ đỏ M02 / D02 ngay tại thời điểm handshake do phát hiện phiên đăng nhập mới dùng chung IP mạng với phiên bị gắn cờ trước đó.

---

## 🛑 KR-04: Bất Đồng Bộ Tuyệt Đối Cho Mọi Tác Vụ I/O & ADB (Non-blocking UI)
- **Quy định:** Mọi tác vụ đọc/ghi tiến trình ngoài, chạy lệnh shell ADB, ping IP, hoặc đợi delay airplane mode bắt buộc phải chạy bất đồng bộ (`async`/`await`), sử dụng `Task.Run` hoặc `Process.WaitForExitAsync(cancellationToken)`.
- **Cấm:** Tuyệt đối không gọi `Process.WaitForExit()`, `Thread.Sleep()`, hoặc các phương thức `.Result` / `.Wait()` chặn đứng luồng UI chính (Dispatcher).
- **Hệ quả vi phạm:** Cửa sổ WPF bị đóng băng (Not Responding), khung hình scrcpy bị đứt luồng render và mất phản hồi cảm ứng chuột.

---

## 🛑 KR-05: Ma Trận Dự Phòng Video Codec Cho Dòng Máy Yếu (Exynos / Low-end)
- **Quy định:** Khi khởi động `scrcpy`, hệ thống phải cấu hình tham số video phù hợp và sẵn sàng cơ chế fallback:
  - Thử nghiệm Hardware Encoder mặc định (`omx.sec.avc.enc` hoặc encoder phần cứng của SoC).
  - Nếu tiến trình scrcpy thoát sớm (< 2000ms) do lỗi MediaCodec crash, tự động fallback sang Software Encoder (`omx.google.h264.encoder` hoặc `c2.android.avc.encoder`).
  - Bitrate tối đa cho các dòng chip yếu (như Exynos 7570 của J3 Pro) là 4M - 8M, `max-size` khuyên dùng 720p - 1024p.
- **Cấm:** Ép cứng bitrate cao (> 16M) hoặc cố định encoder duy nhất mà không bắt mã lỗi thoát của tiến trình.

---

## 🛑 KR-06: Zero Hardcoding Thông Số Thiết Bị (Universal Dynamic Architecture)
- **Quy định:** Toàn bộ hệ thống phải hỗ trợ phổ quát mọi thiết bị Android từ 5.0 đến 14+.
- **Cấm:**
  - Không hardcode chuỗi định danh model (như `"SM-J330G"`, `"Redmi"`, `"Pixel"`), serial number, hay cấu trúc file path riêng của hãng trong logic xử lý cốt lõi.
  - Không giả định trước phiên bản Android; mọi kiểm tra phải truy vấn trực tiếp qua `getprop ro.build.version.release` hoặc `getprop ro.build.version.sdk`.

---

## 🛑 KR-07: Chuẩn Hóa Giao Diện & Bù Trừ DPI Win32 (Window Chrome & Scaling)
- **Quy định:** Với cửa sổ WPF `WindowStyle="None"`, việc xử lý phóng to/thu nhỏ (Maximize/Restore) bắt buộc phải tích hợp `WindowMaximizeHelper` để hook thông điệp Win32 `WM_GETMINMAXINFO` nhằm bù trừ `DpiScale`.
- **Cấm:**
  - Không hardcode màu sắc trực tiếp (`#FF0000`, `White`, `Black`) trong các thẻ XAML; mọi màu sắc và brush phải dùng `DynamicResource` liên kết với theme (`Themes/Dark.xaml`, `Themes/Light.xaml`).
  - Không hardcode chuỗi text hiển thị; toàn bộ nhãn và thông báo phải bind qua `Localization/Strings.xaml`.

---

## 🛑 KR-08: Chuẩn Hoàn Tất Toàn Diện Subsystem (Subsystem Completeness, Test & Audit Trail)
- **Quy định:** Mọi tính năng, module, hoặc subsystem mới khi triển khai hoặc tái cấu trúc (refactor) xong **bắt buộc phải thỏa mãn trọn vẹn 6 trụ cột cốt tử** trước khi coi là hoàn thành:

```text
KR-08 Checklist
│
├── 01. Documentation
│     └── docs/XX-feature.md (Mô tả chi tiết triết lý, thiết kế, cấu trúc)
│
├── 02. Architecture
│     └── docs/02-architecture.md (Cập nhật sơ đồ phân tầng và cây thư mục dự án)
│
├── 03. Usage
│     └── Quickstart / API example (Cú pháp gọi 1 dòng code dễ dùng, dễ hiểu)
│
├── 04. Quality Gate
│     └── Test Suite + Evaluation (xUnit unit tests + SC-11 test cases pass 100%)
│
├── 05. Audit Trail
│     └── Decision / Experience / Result (DecisionRecord, Experience, AgentExecutionResult minh bạch)
│
└── 06. Agent Rule
      └── .agents/GEMINI.md (Đồng bộ quy chuẩn vào context chỉ đạo của Agent)
```

- **Mục tiêu tối thượng (5 Câu hỏi tự giải thích):** Bất kỳ ai (Kỹ sư người thật hay Agent kế thừa) nhìn vào subsystem đều phải trả lời được ngay 5 câu hỏi:
  1. **Nó là gì?** (Khái niệm, phạm vi trách nhiệm).
  2. **Tại sao tồn tại?** (Bài toán thực tế nó giải quyết, triết lý thiết kế).
  3. **Dùng nó thế nào?** (Cú pháp API tinh gọn, copy-paste chạy được ngay).
  4. **Làm sao biết nó hoạt động đúng?** (Bằng chứng test, pass rate, Quality Gate).
  5. **Tại sao Agent lại chọn cách này?** (Audit Trail qua `DecisionRecord` và `MatchEvidence`).

- **Cấm:**
  - Tuyệt đối không bàn giao code khi thiếu bất kỳ trụ cột nào trong 6 trụ cột trên.
  - Không viết code "chạy được một lần rồi thôi" mà không có Audit Trail và Test Suite kiểm chứng lặp lại.
- **Hệ quả vi phạm:** Bị coi là bàn giao mã nguồn rác (Incomplete Code Delivery), phá vỡ tính minh bạch và khả năng tái sử dụng của toàn bộ hệ sinh thái Agent.


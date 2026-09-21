# 🛡️ AN TOÀN ADB & BỀ MẶT ĐỊNH DANH TELEMETRY (02-adb-telemetry-safety.md)

Tài liệu này chi tiết hóa các bề mặt phát hiện (detection surfaces) của hệ thống chống gian lận Shopee (Risk Engine) và các nguyên tắc an toàn khi tương tác với thiết bị Android qua ADB.

---

## 1. Bóc Tách Bề Mặt Phát Hiện (Shopee Risk Detection Surface)

Shopee Client SDK liên tục thu thập và tổng hợp dấu vân tay thiết bị (Device Fingerprint) để gửi về máy chủ phân tích rủi ro. Các vector chính bao gồm:

### 1.1 Khóa Định Danh Cốt Lõi (Primary Identifiers)
- **SSAID (`Settings.Secure.ANDROID_ID`)**: Chuỗi 64-bit Hex. Trước Android 8.0, giá trị này là duy nhất toàn máy. Từ Android 8.0 trở lên, SSAID được cô lập theo từng signing key của ứng dụng. Do đó, việc can thiệp bằng lệnh:
  ```bash
  settings put secure android_id <16_hex_chars>
  ```
  sẽ ghi đè giá trị toàn cục, trực tiếp đánh lừa thuật toán băm định danh của Shopee.
- **Google Advertising ID (GAID)**: Quản trị bởi Google Play Services (`com.google.android.gms`). Khi người dùng đăng nhập nhiều tài khoản trên cùng 1 GAID, Shopee liên kết các tài khoản này thành một nhóm rủi ro (Cluster Flag). Lệnh `pm clear com.google.android.gms` buộc Play Services phải tái tạo GAID mới.

### 1.2 Dấu Vết Lưu Trữ (Persistent Storage Artifacts)
- Shopee lưu trữ token nhận diện thiết bị tại cả vùng nhớ nội bộ lẫn bộ nhớ ngoài:
  - `/data/data/com.shopee.vn/shared_prefs/` (bị xóa sạch bởi `pm clear com.shopee.vn`)
  - `/sdcard/Android/data/com.shopee.vn/`
  - `/sdcard/.shopee/` (thư mục ẩn chứa hardware identifier token)
- Nếu chỉ xóa app data (`pm clear`) mà không xóa thư mục ẩn `/sdcard/.shopee/`, Shopee SDK khi khởi động lại sẽ đọc token cũ từ thẻ nhớ ngoài và phục hồi định danh máy cũ ➔ Dẫn đến tái phát cờ **M02**.

### 1.3 Địa Chỉ IP Mạng & Subnet Tracking
- Shopee đánh dấu cụm thiết bị (Device Farm / Sybil Attack) nếu phát hiện hàng loạt yêu cầu đặt hàng/áp mã từ cùng một địa chỉ IP Public trong thời gian ngắn.
- Việc chuyển đổi kết nối qua mạng di động (Cellular 4G) kết hợp cơ chế Toggle Airplane Mode:
  ```bash
  cmd connectivity airplane-mode enable
  # Delay tối thiểu 3.5s
  cmd connectivity airplane-mode disable
  ```
  sẽ giải phóng socket TCP/IP hiện tại và buộc trạm phát sóng (Base Station / BTS) cấp một địa chỉ IP Public mới từ dải IP động của nhà mạng viễn thông.

---

## 2. Tiêu Chuẩn Thực Thi Lệnh ADB (ADB Execution Hygiene)

### 2.1 Timeout Guard Bắt Buộc
Mọi tương tác với tiến trình `adb.exe` đều tiềm ẩn rủi ro thiết bị rơi vào trạng thái `offline`, `unauthorized` hoặc cáp USB chập chờn.
- Bắt buộc phải gắn giới hạn thời gian (Timeout):
  - Lệnh kiểm tra (`devices`, `getprop`): Tối đa 3.000ms.
  - Lệnh dọn dẹp dữ liệu (`pm clear`): Tối đa 8.000ms.
  - Toàn bộ pipeline bypass: Tối đa 30.000ms.
- Khi hết thời gian, tiến trình ADB phải bị ép hủy (`Kill()`) và giải phóng luồng, trả về lỗi rõ ràng thay vì treo ứng dụng.

### 2.2 Sinh Giá Trị Ngẫu Nhiên Chuẩn Mật Mã (Cryptographic Randomness)
- Khi sinh SSAID mới, bắt buộc sử dụng bộ sinh ngẫu nhiên an toàn mã hóa:
  ```csharp
  using System.Security.Cryptography;

  byte[] bytes = new byte[8];
  RandomNumberGenerator.Fill(bytes);
  string newAndroidId = Convert.ToHexString(bytes).ToLowerInvariant();
  ```
- Tuyệt đối không dùng `System.Random` thông thường vì tính dự đoán cao (pseudo-random) dễ bị phát hiện bởi mô hình học máy (Machine Learning Model) của hệ thống phòng thủ.

### 2.3 Cơ Chế Bơm Text Unicode An Toàn (Clipboard Injection)
- Không truyền chuỗi tiếng Việt có dấu qua lệnh `adb shell input text "..."` vì cơ chế này dịch từng ký tự thành KeyCode ASCII, làm mất dấu, rụng ký tự tiếng Việt hoặc gây lỗi cú pháp shell khi gặp dấu cách và ký tự đặc biệt (`&`, `|`, `"`, `'`).
- Phương pháp chuẩn:
  1. Sử dụng ứng dụng trung gian hoặc ghi trực tiếp chuỗi Unicode vào clipboard của Android qua broadcast:
     ```bash
     am broadcast -a clipper.set -e text "Chuỗi tiếng Việt chuẩn"
     ```
  2. Hoặc gửi phím bấm dán (`adb shell input keyevent 279` - `KEYCODE_PASTE`).

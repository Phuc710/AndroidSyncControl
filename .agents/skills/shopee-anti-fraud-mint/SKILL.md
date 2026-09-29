---
name: shopee-anti-fraud-mint
description: >
  Phân tích và kỹ thuật bypass hệ thống phát hiện gian lận MINT (Machine Learning Anti-Fraud) của Shopee.
  Tập trung vào Behavioral Fingerprinting, IP Association và Account Velocity.
triggers:
  - "bypass mint"
  - "shopee anti fraud"
  - "phát hiện hành vi"
  - "chống liên kết tài khoản"
---

# KỸ NĂNG: BYPASS HỆ THỐNG MINT & PHÂN TÍCH HÀNH VI SHOPEE

## 1. Hệ thống MINT là gì?
MINT là core anti-fraud engine của Shopee sử dụng Machine Learning để chấm điểm tin cậy (Trust Score) cho mỗi tài khoản và thiết bị. Thay vì chỉ kiểm tra xem máy có root hay không (các lớp 1-4), MINT phân tích **Mối quan hệ (Graph)** và **Hành vi (Behavior)**.

## 2. Các Vector Phát Hiện Của MINT
- **Velocity (Vận tốc thao tác):** Tốc độ click, scroll, gõ phím quá đều hoặc quá nhanh so với con người.
- **Account Association (Liên kết tài khoản):** Đăng nhập nhiều tài khoản trên cùng một IP subnet, cùng một thiết bị phần cứng (dù đã fake nhưng nếu pattern fake bị trùng).
- **Affiliate Fraud:** Nếu một tài khoản mới tinh click vào link Affiliate và mua hàng ngay lập tức mà không có lịch sử lướt xem các mặt hàng khác -> Đánh dấu là gian lận tiếp thị.
- **Sensor Data (Dữ liệu cảm biến):** Thiếu dữ liệu gia tốc kế (accelerometer), con quay hồi chuyển (gyroscope) trong lúc thao tác (thường xảy ra nếu dùng emulator hoặc giả lập touch mà không map sensor).

## 3. Chiến Lược Bypass MINT (Lớp 5)
Trong pipeline `ShopeeBypassPipeline.cs` và các thao tác automation (`AdbInputService`), Agent CẦN TUÂN THỦ các quy tắc sau:

### 3.1 Humanized Input (Thao Tác Giống Người)
Tuyệt đối KHÔNG sử dụng tọa độ tĩnh cho mỗi lần click.
```csharp
// SAI:
adb shell input tap 450 800

// ĐÚNG:
var x = BaseX + RandomNumberGenerator.GetInt32(-10, 10);
var y = BaseY + RandomNumberGenerator.GetInt32(-10, 10);
adb shell input tap {x} {y}
```
*Ghi chú:* Thêm delay ngẫu nhiên (300ms - 1200ms) giữa các thao tác. Không gõ text bằng 1 lệnh `input text` dài, mà chia nhỏ hoặc truyền intent với delay.

### 3.2 IP & Fingerprint Consistency
Mỗi tài khoản Shopee cần được gán với một "Shadow Profile":
- Không thay đổi thiết bị liên tục giữa các lần login của cùng 1 account.
- Khi dọn dẹp thiết bị (Wipe data/Reset SSAID) cho account MỚI, bắt buộc phải reset lại IP (Airplane mode hoặc Residential Proxy).
- Tuyệt đối không dùng Data Center IPs (VPS, Cloud) để login. Phải là IP 4G/5G hoặc Residential (ISP dân cư).

### 3.3 Nuôi Tài Khoản (Account Warming)
Không thực hiện chốt đơn ngay lập tức. Agent cần có playbook "Warming":
1. Lướt trang chủ, xem 3-4 sản phẩm không liên quan.
2. Thêm vào giỏ hàng 1 sản phẩm khác.
3. Chờ 1 khoảng thời gian (vài giờ hoặc 1 ngày) trước khi quay lại check-out.

## 4. Kiểm tra tại code hiện tại
`ShopeeBypassPipeline.cs` đã xử lý 4 lớp đầu tiên cực kỳ tốt. Để tích hợp Lớp 5, mọi Executor gọi từ UI cần tuân thủ SC-12: **"Smarter, not faster"**. Nghĩa là việc bypass MINT không nằm ở một đoạn mã C# cụ thể, mà nằm ở **thời gian chờ** và **cách phân phối thao tác** của toàn bộ automation workflow.

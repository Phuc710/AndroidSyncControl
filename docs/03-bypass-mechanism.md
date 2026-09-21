# 🛡️ Cơ Chế Kỹ Thuật Bypass Shopee M02 / D02 / L01 (Bypass Mechanism)

Tài liệu này phân tích bề mặt phát hiện (detection surface) của hệ thống quản lý rủi ro Shopee (Risk Engine) và cơ chế kỹ thuật mà **ShopeeControl** áp dụng để làm sạch thiết bị mà không cần can thiệp sâu vào hệ điều hành.

---

## 1. Bảng Mã Lỗi Quản Lý Rủi Ro Của Shopee

Khi sử dụng nhiều tài khoản hoặc áp mã giảm giá trên một thiết bị di động, Shopee Risk Engine sẽ kiểm tra và kích hoạt các mã lỗi sau:

| Mã Lỗi | Tên Lỗi Hiển Thị | Nguyên Nhân Kỹ Thuật | Cách Khắc Phục |
|---|---|---|---|
| **M02 / D02** | *"Thanh toán không thành công / Mã giảm giá không hợp lệ"* | Dấu vân tay thiết bị (Device Fingerprint) đã bị gắn cờ (Blacklist) vì lưu trữ lịch sử áp voucher từ các tài khoản trước. | Chạy quy trình Bypass làm sạch token và đổi SSAID. |
| **L01** | *"Đăng nhập không thành công / Thiết bị bị giới hạn"* | Số lượng tài khoản đăng nhập trên cùng một Android ID vượt quá ngưỡng cho phép của Shopee trong một khoảng thời gian. | Xóa sạch session token, reset SSAID và GAID. |
| **M01 / D01** | *"Tài khoản của bạn đã bị giới hạn"* | Tài khoản bị khóa trực tiếp từ máy chủ (Server-side ban do gian lận thanh toán, đơn ảo). | Khóa ở cấp tài khoản, không khắc phục được bằng phần cứng. |

---

## 2. Bề Mặt Định Danh (Shopee Detection Surface)

Shopee SDK thu thập các thông số định danh từ thiết bị Android thông qua các kênh sau:

1. **Android ID (SSAID - Settings.Secure.ANDROID_ID)**:
   - Chuỗi Hex 64-bit ngẫu nhiên được hệ thống gán cho thiết bị. Đây là khóa chính (Primary Key) để Shopee liên kết thiết bị với tài khoản người dùng.
2. **Google Advertising ID (GAID)**:
   - Mã định danh quảng cáo do Google Play Services quản lý, dùng để theo dõi hành vi xuyên ứng dụng.
3. **Session Tokens & Cache Storage**:
   - Lưu trữ trong thư mục riêng tư của ứng dụng (`/data/data/com.shopee.vn/shared_prefs`) và thư mục ngoài (`/sdcard/Android/data/com.shopee.vn`, `/sdcard/.shopee`).
4. **Địa chỉ IP Mạng (IP Address)**:
   - Shopee giám sát việc nhiều tài khoản phát sinh giao dịch từ cùng một địa chỉ IP công cộng (Public IP) trong thời gian ngắn.

---

## 3. Quy Trình 6 Bước Kỹ Thuật Của `ShopeeBypassService`

Tính năng **Bypass Shopee** trong ShopeeControl thực thi tuần tự 6 bước qua kết nối ADB:

```
    ┌─────────────────────────────────────────────────────────────┐
    │ BƯỚC 1: DỪNG & XÓA SẠCH DỮ LIỆU TOKEN SHOPPE                │
    │ am force-stop com.shopee.vn                                 │
    │ pm clear com.shopee.vn                                      │
    │ rm -rf /sdcard/Android/data/com.shopee.vn /sdcard/.shopee   │
    └──────────────────────────────┬──────────────────────────────┘
                                   │
                                   ▼
    ┌─────────────────────────────────────────────────────────────┐
    │ BƯỚC 2: SINH & NẠP ANDROID ID (SSAID) MỚI                   │
    │ Sinh ngẫu nhiên chuỗi Hex 16 ký tự                          │
    │ settings put secure android_id <new_hex_id>                 │
    └──────────────────────────────┬──────────────────────────────┘
                                   │
                                   ▼
    ┌─────────────────────────────────────────────────────────────┐
    │ BƯỚC 3: RESET GOOGLE ADVERTISING ID (GAID)                  │
    │ pm clear com.google.android.gms                             │
    └──────────────────────────────┬──────────────────────────────┘
                                   │
                                   ▼
    ┌─────────────────────────────────────────────────────────────┐
    │ BƯỚC 4: BẬT CHẾ ĐỘ MÁY BAY (NGẮT MẠNG 3.5 GIÂY)             │
    │ cmd connectivity airplane-mode enable                       │
    └──────────────────────────────┬──────────────────────────────┘
                                   │
                                   ▼
    ┌─────────────────────────────────────────────────────────────┐
    │ BƯỚC 5: TẮT CHẾ ĐỘ MÁY BAY (NHẬN DẢI IP 4G MỚI)             │
    │ cmd connectivity airplane-mode disable (chờ 3.5s kết nối)   │
    └──────────────────────────────┬──────────────────────────────┘
                                   │
                                   ▼
    ┌─────────────────────────────────────────────────────────────┐
    │ BƯỚC 6: KHỞI CHẠY LẠI SHOPEE NHƯ THIẾT BỊ MỚI               │
    │ monkey -p com.shopee.vn -c ...LAUNCHER 1                    │
    └─────────────────────────────────────────────────────────────┘
```

### Chi tiết các bước thực hiện:

#### Bước 1: Dọn dẹp hoàn toàn Session Token & Dấu vết cũ
Lệnh `pm clear com.shopee.vn` xóa sạch phân vùng `/data/data/com.shopee.vn` (gồm SQLite database, Shared Preferences, cookie đăng nhập). Đồng thời, lệnh `rm -rf` dọn sạch thư mục ẩn `.shopee` trên bộ nhớ trong, ngăn ứng dụng khôi phục ID thiết bị cũ từ file lưu tạm ngoài.

#### Bước 2: Sinh và cập nhật SSAID mới
Ứng dụng sử dụng bộ tạo số ngẫu nhiên an toàn (`System.Security.Cryptography.RandomNumberGenerator`) để sinh 8 bytes (chuỗi 16 ký tự Hex). Sau đó ghi đè vào hệ thống Android bằng quyền ADB:
```bash
settings put secure android_id <new_id>
```
Khi Shopee khởi động lại, hàm `Settings.Secure.getString(..., ANDROID_ID)` sẽ nhận giá trị mới hoàn toàn.

#### Bước 3: Làm mới Google Advertising ID (GAID)
Lệnh `pm clear com.google.android.gms` xóa cache dịch vụ Google Play, buộc hệ điều hành sinh một mã GAID mới khi các API phân tích được gọi.

#### Bước 4 & 5: Đảo Chế độ máy bay để đổi dải IP 4G
Sử dụng Android Connectivity Service:
```bash
cmd connectivity airplane-mode enable
# Nghỉ 3.5 giây
cmd connectivity airplane-mode disable
# Nghỉ 3.5 giây chờ trạm BTS cấp dải IP mới
```
*Lưu ý: Để việc xoay IP có hiệu lực, thiết bị cần sử dụng kết nối dữ liệu di động (SIM 4G/LTE).*

#### Bước 6: Khởi chạy Shopee sạch
Dùng công cụ `monkey` để phát sự kiện khởi động ứng dụng tự nhiên từ màn hình chính (Launcher Intent). Ứng dụng Shopee mở lên trong trạng thái chào mừng như vừa tải từ Google Play Store về một chiếc điện thoại mới.

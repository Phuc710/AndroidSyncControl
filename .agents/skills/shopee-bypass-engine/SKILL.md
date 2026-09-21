---
name: shopee-bypass-engine
description: >
  Kỹ năng điều phối toàn diện quy trình làm sạch telemetry thiết bị Android,
  triệt tiêu vector định danh để bypass mã lỗi rủi ro Shopee M02 / D02 / L01.
triggers:
  - "bypass shopee"
  - "sửa lỗi M02"
  - "lỗi D02"
  - "fix L01"
  - "làm sạch thiết bị"
  - "đổi IP Shopee"
  - "reset SSAID Shopee"
---

# Skill: Shopee Bypass Engine (M02 / D02 / L01 Mitigation)

## 1. Bản Đồ Mã Lỗi Shopee (Risk Matrix)

| Mã Lỗi | Nguyên Nhân Gốc | Mức Độ | Biện Pháp Kỹ Thuật |
|---|---|---|---|
| **M02 / D02** | Dấu vân tay thiết bị (SSAID + Local Token) nằm trong blacklist áp mã giảm giá. | Thiết bị | Thực thi trọn vẹn Pipeline 6 bước làm sạch. |
| **L01** | Giới hạn số lượng tài khoản đăng nhập trên cùng một ID phần cứng. | Thiết bị | Xóa sạch session token, reset SSAID và GAID. |
| **M01 / D01** | Tài khoản bị khóa trực tiếp từ máy chủ (Server-side Fraud Flag). | Tài khoản | Không thể bypass bằng thiết bị; bắt buộc đổi tài khoản. |

---

## 2. Quy Trình 6 Bước Tiêu Chuẩn (Universal No-Root Pipeline)

### Bước 1: Dừng & Xóa Sạch Dữ Liệu Ứng Dụng
```bash
adb shell am force-stop com.shopee.vn
adb shell pm clear com.shopee.vn
adb shell rm -rf /sdcard/Android/data/com.shopee.vn
adb shell rm -rf /sdcard/.shopee
```
*Mục đích:* Tiêu hủy toàn bộ SQLite database, SharedPreferences chứa access token cũ, và hardware token ẩn trên thẻ nhớ ngoài.

### Bước 2: Sinh & Ghi Đè Android ID (SSAID)
Sinh chuỗi Hex 16 ký tự ngẫu nhiên bằng CSPRNG:
```bash
adb shell settings put secure android_id <16_hex_chars>
# Xác minh:
adb shell settings get secure android_id
```
*Mục đích:* Tạo định danh phần cứng giả lập mới hoàn toàn cho thiết bị.

### Bước 3: Reset Google Advertising ID (GAID)
```bash
adb shell pm clear com.google.android.gms
```
*Mục đích:* Buộc Google Play Services khởi tạo ID theo dõi quảng cáo mới, cắt đứt chuỗi liên kết hành vi xuyên ứng dụng.

### Bước 4: Kích Hoạt Chế Độ Máy Bay (Ngắt Socket)
```bash
adb shell cmd connectivity airplane-mode enable
```
*Mục đích:* Hủy các kết nối TCP/IP đang mở giữa thiết bị và server gateway của Shopee.

### Bước 5: Tắt Chế Độ Máy Bay & Nhận IP 4G Mới
```bash
adb shell cmd connectivity airplane-mode disable
```
*Lưu ý cốt tử:* **Bắt buộc chờ từ 3.5 đến 5.0 giây** để modem di động đàm phán lại với trạm BTS và nhận dải IP Public mới.
Kiểm tra IP mới qua:
```bash
adb shell ip addr show rmnet_data0
# Hoặc ping test
```

### Bước 6: Khởi Động Lại Ứng Dụng Bằng Launcher Intent
```bash
adb shell monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1
```
*Mục đích:* Khởi động ứng dụng như thể người dùng vừa bấm vào icon trên màn hình chính, kích hoạt chu trình onboarding mới toanh.

---

## 3. Điều Kiện Tiên Quyết (Pre-flight Checklist)
Trước khi chạy pipeline:
1. Thiết bị phải dùng **SIM 4G/5G** để kết nối mạng di động.
2. **Tắt kết nối Wi-Fi** hoàn toàn (vì bật/tắt airplane mode trên mạng Wi-Fi thông thường không làm thay đổi IP Public của Router).
3. Đảm bảo cáp USB kết nối ổn định, kiểm tra lệnh `adb devices` trả về trạng thái `device`.

---

## 4. Kịch Bản Kiểm Tra Xác Thực Sau Khi Bypass (Post-flight Verification)
Chạy script kiểm tra `check_info.bat` hoặc các lệnh shell sau:
```bash
# 1. Kiểm tra SSAID đã đổi chưa:
adb shell settings get secure android_id

# 2. Kiểm tra thư mục ẩn đã bị xóa sạch chưa:
adb shell ls -la /sdcard/.shopee
# Kết quả mong đợi: No such file or directory

# 3. Kiểm tra IP mạng di động hiện tại:
adb shell curl -s https://api.ipify.org
```

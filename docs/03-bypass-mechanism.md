# 🛡️ Cơ Chế Kỹ Thuật Bypass Shopee M02 / D02 / L01 / M04 (Bypass Mechanism)

Tài liệu này mô tả chi tiết bề mặt phát hiện (detection surface) của hệ thống quản lý rủi ro Shopee (Risk Engine & MINT AI) và cơ chế kỹ thuật được triển khai trong kiến trúc **`ShopeeBypassPipeline.cs`** kết hợp **`DeviceNetworkService.cs`** thuộc ứng dụng **AndroidSyncControl**.

Hệ thống hoạt động theo nguyên lý **Zero Hardcoding (KR-06)**, tương thích phổ quát với mọi dòng điện thoại Android (Android 5.0 đến Android 14+), tự động thích nghi giữa môi trường **Root (Zygisk/Shamiko/HMA/PrivacyKit)** và **Non-Root ADB**.

---

## 1. Bảng Mã Lỗi Quản Lý Rủi Ro Của Shopee

Khi vận hành nhiều tài khoản hoặc áp dụng voucher khuyến mãi, hệ thống Shopee Risk Engine kích hoạt các mã lỗi sau:

| Mã Lỗi | Tên Lỗi Hiển Thị | Cơ Chế Nhận Diện Của Shopee | Giải Pháp Kỹ Thuật Của Pipeline |
|---|---|---|---|
| **M02 / D02** | *"Thanh toán không thành công / Rất tiếc, ưu đãi này không còn khả dụng"* | Dấu vân tay thiết bị (Device Fingerprint bao gồm SSAID, token lưu ẩn tại `/sdcard/.shopee`, hoặc địa chỉ IP mạng di động) đã bị gắn cờ blacklist do từng áp voucher trước đó. | Chạy quy trình Bypass: Đột biến SSAID 2 tầng, xóa sạch 7 đường dẫn ẩn ngoài bộ nhớ trong, thanh trừng AccountManager và xoay IP mạng di động mới. |
| **L01** | *"Đăng nhập không thành công / Thiết bị bị giới hạn đăng nhập"* | Số lượng tài khoản đăng nhập trên cùng một Android ID hoặc AccountManager instance vượt ngưỡng an toàn trong một khoảng thời gian. | Xóa sạch session token, reset SSAID qua `settings_ssaid.xml`, dọn sạch SQLite AccountManager và reset Google Play Services. |
| **M04** | *"Tài khoản của bạn ghi nhận hành vi bất thường"* | MINT Anti-Fraud phát hiện môi trường can thiệp (Root, Xposed, Magisk binary, Debuggable props) hoặc tỷ lệ thao tác tự động quá nhanh. | Shamiko unmount namespace root, HideMyApplist tạo whitelist package sạch, pipeline khởi chạy app bằng Intent tự nhiên. |
| **M01 / D01** | *"Tài khoản của bạn đã bị giới hạn"* | Khóa tài khoản vĩnh viễn ở cấp máy chủ (Server-side ban do vi phạm chính sách thanh toán, tạo đơn ảo). | Đây là cấm cấp tài khoản trên máy chủ, thiết bị cần tạo tài khoản mới sau khi đã chạy Bypass sạch. |

---

## 2. Bề Mặt Định Danh Thực Tế (Shopee Detection Surface)

Shopee SDK và MINT Client thu thập dữ liệu định danh qua 6 kênh độc lập:

1. **Android ID (SSAID - Settings.Secure.ANDROID_ID):**
   - Từ Android 8.0 (API 26+), Google tách biệt SSAID cho từng ứng dụng và lưu trữ cố định trong `/data/system/users/0/settings_ssaid.xml`.
   - Lệnh `settings put secure android_id <new_id>` chỉ tác động lên Provider toàn cục, Shopee vẫn đọc ID cũ từ file XML nếu chưa được xóa dứt điểm.
2. **Android AccountManager Persistence:**
   - Shopee lưu persistent account token và device UUID vào AccountManager của Android OS (`accounts_de.db` hoặc `accounts.db`). Dữ liệu này **hoàn toàn không bị xóa** bởi lệnh `pm clear com.shopee.vn`.
3. **Bộ Nhớ Ngoài & Thư Mục Ẩn (7 External Storage Paths):**
   - Shopee ghi token định danh nhị phân bí mật lên External Storage để liên kết lại thiết bị ngay cả khi ứng dụng bị gỡ cài đặt:
     - `/sdcard/Android/data/com.shopee.vn`
     - `/sdcard/Android/media/com.shopee.vn`
     - `/sdcard/Android/obb/com.shopee.vn`
     - `/sdcard/.shopee` (File nhị phân chứa device GUID)
     - `/sdcard/Shopee`
     - `/sdcard/.system_id`
     - `/sdcard/Android/.system_setting`
4. **Google Advertising ID (GAID) & Google Services Framework (GSF):**
   - Mã quảng cáo và token liên kết thiết bị do Google Play Services (`com.google.android.gms`) và Google Services Framework (`com.google.android.gsf`) quản lý.
5. **Địa Chỉ IP Mạng & Trạm Thu Phát Sóng Di Động (Cellular BTS Telemetry):**
   - Shopee giám sát Public IP và dải subnet mạng di động. Nếu nhiều giao dịch phát sinh từ cùng một IP công cộng trong khoảng thời gian ngắn, hệ thống sẽ kích hoạt M02.
6. **Môi Trường Hệ Thống & Package List:**
   - Quét `/proc/self/mounts` để phát hiện Magisk/Zygisk và quét `PackageManager` để phát hiện công cụ gian lận/hooking.

---

## 3. Quy Trình Kỹ Thuật 9 Phase Của `ShopeeBypassPipeline.cs`

Mã nguồn `ShopeeBypassPipeline.cs` triển khai quy trình 9 Phase tuần tự, kết hợp quan sát (Observe) và xác thực (Verify) theo chuẩn **SC-11** và **SC-12**:

```text
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 0: KHÁM PHÁ MÔI TRƯỜNG & ĐỊNH TUYẾN CHIẾN LƯỢC (SC-12.1)         │
│ • Kiểm tra Root (su check), Android API Level (ro.build.version.sdk).  │
│ • Kiểm tra module: PrivacyKit, Device Masker, HideMyApplist, Shamiko.  │
│ • Kiểm tra kiểu mạng (Cellular/WiFi/VPN) & lệnh Airplane của ROM.      │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 1: TIÊU THỔ TOÀN BỘ DỮ LIỆU & TOKEN ẨN (Scorched Earth Wipe)     │
│ • am force-stop com.shopee.vn & pm clear com.shopee.vn.                │
│ • Xóa đồng loạt 7 đường dẫn ẩn trên /sdcard/.                         │
│ • Vòng lặp verify ls -d + leo quyền 'su -c rm -rf' nếu vướng Scoped    │
│   Storage trên Android 11+ [FIX-4, FIX-5].                            │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 2: ĐỘT BIẾN ĐỊNH DANH THIẾT BỊ SSAID (CSPRNG Hex-16)            │
│ • Sinh ngẫu nhiên Hex 16 ký tự an toàn (RandomNumberGenerator).        │
│ • Nạp vào Provider: settings put secure android_id <new_id>.           │
│ • Trên Root Android 8+: sed -i '/com.shopee.vn/d' settings_ssaid.xml   │
│   để xóa entry per-app cũ, buộc hệ thống tái sinh mới [FIX-1].        │
│ • Verify lại settings get secure android_id.                           │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 3: LÀM SẠCH TRACKING GOOGLE (GAID & GSF ID)                      │
│ • pm clear com.google.android.gms (reset Advertising ID).              │
│ • pm clear com.google.android.gsf (reset Services Framework ID).       │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 4: THANH TRỪNG TOKEN TRONG ACCOUNTMANAGER [FIX-2]                │
│ • Tuyến Root: Xóa trực tiếp SQLite 'DELETE FROM accounts WHERE type    │
│   LIKE "%shopee%" OR type LIKE "%sea.com%"' trong accounts_de.db       │
│   (Android 7+) và accounts.db (Android <7).                            │
│ • Tuyến Non-Root: Phân tích dumpsys account và xóa bằng                │
│   cmd account remove --account-name "..." --account-type "...".        │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 5: ĐỘT BIẾN PHẦN CỨNG SÂU (PrivacyKit / Device Masker)          │
│ • Chỉ chạy khi có Root và đã cài module hook Xposed/LSPosed.           │
│ • Gửi broadcast: com.sal.privacykit.RANDOMIZE và .masker.RANDOMIZE.    │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 6 & 7: LUÂN CHUYỂN IP MẠNG DI ĐỘNG & ADAPTIVE POLLER (SC-12.10)  │
│ • Ghi lại IP Public hiện tại (GetCurrentMobileIpAsync).                │
│ • Bật Airplane Mode -> Chờ 3.5s -> Tắt Airplane Mode [FIX-3: hỗ trợ    │
│   fallback cho MIUI, HyperOS, Vivo, OneUI].                           │
│ • Adaptive Poller: Kiểm tra định kỳ (chu kỳ 1s, timeout 30s) cho đến   │
│   khi modem cấp địa chỉ IP Public mới khác IP cũ.                     │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 8: KHỞI CHẠY SHOPEE SẠCH & XÁC MINH TIẾN TRÌNH                   │
│ • Mở app qua Explicit Intent HomeActivity_, fallback monkey launcher. │
│ • Kiểm tra pidof com.shopee.vn xác minh ứng dụng đã sống trong RAM.    │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Chi Tiết Kỹ Thuật Từng Phase Thực Thi

### Phase 0: Khám Phá Môi Trường Động (Dynamic Environment Discovery)
Không giả định cấu hình thiết bị trước (KR-06). Pipeline thực hiện:
```bash
# Kiểm tra quyền Root
su -c id
# Lấy API Level hệ điều hành
getprop ro.build.version.sdk
# Quét sự hiện diện của module Zygisk/LSPosed
pm list packages | grep -E "privacykit|masker|HideMyApplist|shamiko|lspatch"
# Quét interface mạng di động (hỗ trợ 8 loại interface)
ip -f inet addr
# Thử nghiệm tính khả dụng của lệnh airplane mode
cmd connectivity airplane-mode
```

### Phase 1: Tiêu Thổ Dữ Liệu & Quét 7 Thư Mục Ẩn
1. Dừng và xóa sandbox ứng dụng:
   ```bash
   am force-stop com.shopee.vn
   pm clear com.shopee.vn
   ```
2. Xóa đồng loạt 7 đường dẫn lưu trữ ngoài:
   ```bash
   rm -rf /sdcard/Android/data/com.shopee.vn
   rm -rf /sdcard/Android/media/com.shopee.vn
   rm -rf /sdcard/Android/obb/com.shopee.vn
   rm -rf /sdcard/.shopee
   rm -rf /sdcard/Shopee
   rm -rf /sdcard/.system_id
   rm -rf /sdcard/Android/.system_setting
   ```
3. **[FIX-5] Vòng lặp xác minh & Leo quyền Root:**
   Đối với mỗi thư mục, chạy `ls -d "<path>"`. Nếu Android 11+ Scoped Storage trả về *Permission denied* khiến file chưa bị xóa, pipeline tự động kích hoạt `su -c 'rm -rf "<path>"'`.

### Phase 2: Đột Biến Định Danh SSAID 2 Tầng
1. Tạo chuỗi Hex 16 ký tự an toàn bằng bộ sinh số ngẫu nhiên mật mã học (`RandomNumberGenerator`).
2. Ghi đè Provider toàn cục:
   ```bash
   settings put secure android_id <new_hex_id>
   ```
3. **[FIX-1] Xóa Per-App SSAID Entry trên Android 8.0+ (API 26+):**
   Nếu thiết bị có Root và `AndroidApiLevel >= 26`:
   ```bash
   su -c "sed -i '/com.shopee.vn/d' /data/system/users/0/settings_ssaid.xml"
   ```
   Hành động này ép Android OS khi cấp phát quyền truy cập cho Shopee lần tới sẽ tự động tạo một SSAID ngẫu nhiên mới hoàn toàn, loại bỏ 100% token cũ.

### Phase 3: Làm Sạch Dịch Vụ Google Play (GAID & GSF)
```bash
pm clear com.google.android.gms
pm clear com.google.android.gsf
```
Xóa sạch bộ nhớ tạm của Google Play Services, buộc hệ thống tái cấp phát Advertising ID và Services Framework token khi ứng dụng khởi chạy.

### Phase 4: Thanh Trừng Token Trong Android AccountManager
- **Tuyến Root [FIX-2]:**
  Truy cập trực tiếp vào SQLite database của hệ thống và xóa mọi tài khoản liên quan đến Shopee / Sea Group:
  ```bash
  su -c "sqlite3 /data/system_de/0/accounts_de.db \"DELETE FROM accounts WHERE type LIKE '%shopee%' OR type LIKE '%sea.com%';\""
  su -c "sqlite3 /data/system/accounts.db \"DELETE FROM accounts WHERE type LIKE '%shopee%' OR type LIKE '%sea.com%';\""
  ```
- **Tuyến Non-Root:**
  Đọc `dumpsys account`, tìm kiếm các instance có chứa `shopee` hoặc `sea.com` và xóa qua command line:
  ```bash
  cmd account remove --account-name "<NAME>" --account-type "<TYPE>"
  ```

### Phase 5: Kích Hoạt Đột Biến Phần Cứng Sâu (Xposed Hooks)
Nếu phát hiện thiết bị đã cài đặt PrivacyKit hoặc Device Masker:
```bash
am broadcast -a com.sal.privacykit.RANDOMIZE
am broadcast -a com.device.id.masker.RANDOMIZE
```
Đổi toàn bộ IMEI, Serial, MAC address, IMSI, Build Fingerprint trong bộ nhớ RAM trước khi mở ứng dụng.

### Phase 6 & 7: Luân Chuyển IP Di Động Đa Chipset & Adaptive Poller
1. **Lấy IP Ban Đầu Đa Chipset (`DeviceNetworkService.cs`):**
   Hỗ trợ toàn diện 8 kiểu interface mạng:
   - Samsung Exynos: `pdp_ip`
   - Qualcomm Android 12+: `v4-rmnet`, `rmnet_data`, `rmnet`
   - Unisoc / Spreadtrum: `seth`
   - MediaTek: `ccmni`
   - USB Modem / Dongle: `wwan`, `data0`
   - CLAT IPv6: `clat`
2. **Đảo Airplane Mode Có Fallback Cho ROM Tùy Biến [FIX-3]:**
   - Tiêu chuẩn: `cmd connectivity airplane-mode enable` -> delay 3.5s -> `cmd connectivity airplane-mode disable`.
   - Nếu gặp ROM Xiaomi (MIUI/HyperOS), Vivo, OneUI chặn lệnh: Tự động kích hoạt fallback kép:
     ```bash
     settings put global airplane_mode_on 1
     am broadcast -a android.intent.action.AIRPLANE_MODE --ez state true
     # Delay 3.5s
     settings put global airplane_mode_on 0
     am broadcast -a android.intent.action.AIRPLANE_MODE --ez state false
     ```
3. **Adaptive IP Poller (SC-12.10):**
   Liên tục thăm dò IP công cộng với chu kỳ 1s (timeout 30s) cho đến khi xác nhận `CurrentIP != InitialIp`. Không bao giờ dựa vào sleep mù quáng để giả định đã đổi IP thành công.

### Phase 8: Khởi Chạy Ứng Dụng Sạch & Giám Sát Tiến Trình
```bash
am start -n com.shopee.vn/com.shopee.app.ui.home.HomeActivity_
```
Nếu tiến trình chưa lên sau 1 giây (kiểm tra bằng `pidof com.shopee.vn`), hệ thống tự động kích hoạt fallback qua Launcher Intent của `monkey`:
```bash
monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1
```

---

## 5. Bảng Tổng Hợp 8 Điểm Nâng Cấp Cốt Tử (Code Fixes vs. Old Architecture)

| Điểm Fix | Mã Lỗi Xử Lý | Vấn Đề Ở Kiến Trúc Cũ | Cơ Chế Nâng Cấp Mới Trong Mã Nguồn |
|---|---|---|---|
| **FIX-1** | M02 / L01 | Trên Android 8+, `settings put secure` không đổi được Per-App SSAID trong `settings_ssaid.xml`. | Can thiệp Root dùng `sed -i` xóa dòng `com.shopee.vn` trong file XML hệ thống, ép OS tái sinh ID mới. |
| **FIX-2** | L01 | `pm clear` không xóa được account token lưu trong `AccountManager` của hệ thống. | Truy vấn trực tiếp SQLite `accounts_de.db` xóa sạch account Shopee/Sea; tuyến Non-Root dùng `cmd account remove`. |
| **FIX-3** | M02 | Xiaomi MIUI, HyperOS, Vivo Funtouch OS không hỗ trợ lệnh `cmd connectivity airplane-mode`. | Bổ sung fallback kép qua `settings put global airplane_mode_on` kết hợp broadcast intent. |
| **FIX-4** | M02 | Chỉ xóa 2-3 đường dẫn ngoài bộ nhớ, bỏ sót các thư mục `.system_id`, `Shopee`, `.system_setting`. | Mở rộng danh sách lên 7 đường dẫn ẩn lưu trữ token của Shopee SDK. |
| **FIX-5** | M02 | Android 11+ Scoped Storage chặn lệnh `rm -rf` từ shell thường, gây âm thầm thất bại (Fake Success). | Duyệt vòng lặp `ls -d` xác minh từng đường dẫn; tự động leo quyền `su -c 'rm -rf'` nếu vướng quyền. |
| **FIX-6** | Phổ quát | Không kiểm tra phiên bản Android, dẫn đến chạy lệnh không tương thích trên thiết bị cũ/mới. | Truy vấn động `ro.build.version.sdk` tại Phase 0 để phân nhánh logic chính xác. |
| **FIX-7** | M04 | Thiếu kiểm tra module Shamiko trong snapshot môi trường. | Thêm `env.HasShamiko` để xác thực cơ chế ẩn Root tầng Native trước khi thực thi. |
| **FIX-Cellular**| M02 | Chỉ nhận diện `rmnet` và `ccmni`, Samsung Exynos và máy chip Unisoc không lấy được IP di động. | Mở rộng regex bắt 8 họ interface: `pdp_ip`, `v4-rmnet`, `rmnet_data`, `seth`, `wwan`, `clat`... |

---

## 6. Tuân Thủ Tiêu Chuẩn Kỹ Sư Cao Cấp (SC-11 & SC-12)

1. **SC-11 (Verified Knowledge & Workflow):**
   - Mọi bản sửa đổi kiến trúc đều xuất phát từ bằng chứng phân tích lỗi thực tế (M02 / L01 / M04), có tài liệu hóa và đối chiếu mã nguồn 1:1.
   - Không ghi đè logic phỏng đoán; duy trì cơ chế Fallback an toàn cho từng nhánh thiết bị.
2. **SC-12 (Intelligent Decision & Real-World Execution):**
   - **SC-12.1 (No First-Solution Bias):** Lựa chọn phương án tối ưu dựa trên Snapshot môi trường thực tế (Root vs Non-Root).
   - **SC-12.2 (Real Data Only):** Địa chỉ IP, trạng thái tiến trình, token SSAID đều được truy vấn từ thiết bị thật.
   - **SC-12.10 & SC-12.11 (Real Verification & No Fake Success):** Xóa file phải có `ls` kiểm tra; đổi IP phải so sánh giá trị trước và sau; mở app phải kiểm tra `pidof`. Tuyệt đối không giả định thành công.

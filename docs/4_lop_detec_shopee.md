# 🛡️ 5 Lớp Phòng Thủ & Bề Mặt Nhận Diện Của Shopee Risk Engine (MINT)

Tài liệu này phân tích chi tiết 5 lớp phòng thủ (Detection Layers) mà Shopee Risk Engine (kết hợp Shopee MINT Machine Learning Anti-Fraud) sử dụng để phát hiện việc tạo nhiều tài khoản, gian lận voucher, áp mã khuyến mãi (M02 / D02 / L01 / M04), và cơ chế hóa giải tương ứng được triển khai trong mã nguồn thực tế của **`ShopeeBypassPipeline.cs`** và **`DeviceNetworkService.cs`**.

---

## Sơ Đồ Tổng Quan 5 Lớp Phòng Thủ Shopee

```text
┌────────────────────────────────────────────────────────────────────────┐
│ LỚP 1: QUÉT DANH SÁCH ỨNG DỤNG (Package & Module Scanner)              │
│ • Bề mặt: PackageManager.getInstalledPackages(), getInstalledApplications│
│   tìm Magisk, LSPosed, Shamiko, HMA, AutoClicker, Termux, HttpCanary... │
│ ➔ CƠ CHẾ BYPASS: HideMyApplist (HMA) bọc Zygote & system_server, tạo   │
│   Whitelist danh bạ hệ thống sạch cho com.shopee.vn.                  │
├────────────────────────────────────────────────────────────────────────┤
│ LỚP 2: NATIVE ROOT & MOUNT SCANNER (Tầng C/C++ Linux Namespace)        │
│ • Bề mặt: Quét /proc/self/mounts, /proc/mounts, các thư mục nhị phân    │
│   /sbin, /system/xbin/su, Magisk su socket, và Zygisk symbol memory.   │
│ ➔ CƠ CHẾ BYPASS: Module Shamiko / KernelSU unmount sạch toàn bộ        │
│   namespace root khỏi process Shopee ngay tại thời điểm Zygote fork.   │
├────────────────────────────────────────────────────────────────────────┤
│ LỚP 3: ĐỊNH DANH PHẦN CỨNG & TOKEN ẨN (Hardware & Persistent Token)    │
│ • Bề mặt: SSAID (Settings.Secure + per-app settings_ssaid.xml),        │
│   AccountManager SQLite (accounts_de.db), GAID, GSF ID, và 7 đường dẫn  │
│   lưu trữ token ẩn trên External Storage (/sdcard/.shopee, Shopee...). │
│ ➔ CƠ CHẾ BYPASS: CSPRNG Hex SSAID + xóa per-app XML trên Android 8+,   │
│   SQLite purge AccountManager, clear GMS/GSF, xóa & verify 7 paths.    │
├────────────────────────────────────────────────────────────────────────┤
│ LỚP 4: THEO DÕI ĐỊA CHỈ IP & MODEM CELLULAR (Network Telemetry)        │
│ • Bề mặt: Public IP, Subnet IP, MNC/MCC nhà mạng, phát hiện VPN/Proxy  │
│   qua network interface (tun, ppp), correlation cùng dải IP di động.   │
│ ➔ CƠ CHẾ BYPASS: Multi-chipset interface scanner (Exynos pdp_ip,       │
│   Qualcomm rmnet/v4-rmnet, Unisoc seth, MediaTek ccmni), đảo Airplane  │
│   kèm cơ chế Fallback Custom ROM + Adaptive IP Poller verify IP mới.   │
├────────────────────────────────────────────────────────────────────────┤
│ LỚP 5: HỆ THỐNG MINT & PHÂN TÍCH HÀNH VI (Machine Learning Anti-Fraud) │
│ • Bề mặt: MINT AI đánh giá vận tốc thao tác (velocity), gesture entropy│
│   (tọa độ click, bán kính chạm, gia tốc cuộn), hành vi mở app.        │
│ ➔ CƠ CHẾ BYPASS: Khởi chạy Clean Activity Intent HomeActivity_,        │
│   humanize random delays, không click tọa độ cứng, mô phỏng người thật.│
└────────────────────────────────────────────────────────────────────────┘
```

---

## Chi Tiết Kỹ Thuật Từng Lớp & Cơ Chế Xử Lý Trong Code

### Lớp 1: Quét Danh Sách Ứng Dụng (Package Scanner)
* **Cơ chế Shopee:**
  Shopee gọi API `PackageManager.getInstalledPackages()` và `getInstalledApplications()` thông qua Java framework hoặc IPC binder đến `system_server`. Shopee tìm kiếm các package name nhạy cảm như:
  - Công cụ can thiệp: `top.canyie.magisk`, `org.lsposed.manager`, `dranyer.HideMyApplist`, `com.sal.privacykit`, `com.device.id.masker`.
  - Tool tự động hóa/hooking: `com.keramidas.TitaniumBackup`, `com.android.vending.billing.InAppBillingService.LUCK`, `com.topjohnwu.magisk`.
* **Giải pháp trong hệ thống:**
  - Thiết lập module **HideMyApplist (HMA)** chạy qua Zygote: Tạo template Blacklist/Whitelist. Khi `com.shopee.vn` truy vấn package list, HMA hook vào `ApplicationPackageManager` và chặn `queryIntentActivities`, chỉ trả về danh sách các ứng dụng hệ thống mặc định của Google/AOSP.
  - Pipeline phát hiện trạng thái HMA động qua `DiscoverEnvironmentAsync()` (`env.HasHideMyApplist`).

---

### Lớp 2: Quét Dấu Vết Root Tầng Native (Native C/C++ Scanner)
* **Cơ chế Shopee:**
  Shopee nhúng file thư viện native C/C++ (`libshopee.so` hoặc `libsecurity.so`) để bypass Java hook:
  - Đọc trực tiếp `/proc/self/mounts` và `/proc/self/status` bằng system call `openat`, `read` để tìm đường dẫn `magisk`, `core/mirror`, `overlay`.
  - Kiểm tra các file nhị phân su trong PATH: `/sbin/su`, `/system/bin/su`, `/system/xbin/su`.
  - Kiểm tra thuộc tính hệ thống: `ro.debuggable`, `ro.secure`, `ro.build.tags` (`test-keys`).
* **Giải pháp trong hệ thống:**
  - Sử dụng **Shamiko (Zygisk)**: Shamiko hoạt động ở tầng Zygisk trước khi sandbox ứng dụng khởi tạo. Khi process Shopee được fork từ Zygote, Shamiko unmount toàn bộ mount point liên quan đến Magisk, Zygisk và KSU, khôi phục lại namespace mount sạch sẽ hoàn toàn trong `/proc/self/mounts`.
  - Pipeline giám sát module Shamiko qua `env.HasShamiko` tại runtime.

---

### Lớp 3: Thu Thập Định Danh Phần Cứng & Token Bền Vững (Hardware & Storage Tokens)
* **Cơ chế Shopee:**
  Đây là bề mặt phức tạp nhất kích hoạt lỗi **M02** (Voucher abuse) và **L01** (Device limit):
  1. **SSAID (Android ID):**
     - Từ Android 8.0 (API 26+), Google phân tách SSAID theo từng app (Per-app SSAID) và lưu trong file XML hệ thống `/data/system/users/0/settings_ssaid.xml`.
     - Lệnh `settings put secure android_id <new_id>` chỉ ghi vào Provider chung, Shopee vẫn đọc ID cũ từ `settings_ssaid.xml`.
  2. **AccountManager Token:**
     - Shopee đăng ký Account Authenticator vào Android AccountManager. Token xác thực và UUID thiết bị được Android OS lưu trữ trong SQLite database hệ thống (`/data/system_de/0/accounts_de.db` hoặc `/data/system/accounts.db`). Lệnh `pm clear com.shopee.vn` **không thể** xóa dữ liệu trong database hệ thống này.
  3. **Bộ nhớ ngoài (External Storage Hidden Tokens):**
     - Shopee tạo token định danh thiết bị ẩn tại 7 vị trí khác nhau để nhận diện lại thiết bị kể cả khi người dùng xóa app và cài lại:
       - `/sdcard/Android/data/com.shopee.vn`
       - `/sdcard/Android/media/com.shopee.vn`
       - `/sdcard/Android/obb/com.shopee.vn`
       - `/sdcard/.shopee` (File nhị phân chứa device GUID)
       - `/sdcard/Shopee`
       - `/sdcard/.system_id`
       - `/sdcard/Android/.system_setting`
  4. **Google Advertising ID (GAID) & GSF ID:**
     - Mã quảng cáo xuyên ứng dụng của Google Play Services.
* **Giải pháp đã triển khai trong `ShopeeBypassPipeline.cs`:**
  - **[FIX-1] Đột biến SSAID 2 tầng:**
    - Sinh Hex 16 ký tự an toàn bằng CSPRNG (`RandomNumberGenerator`).
    - Nạp vào hệ thống qua `settings put secure android_id`.
    - Trên thiết bị Root (Android 8.0+), dùng `sed -i '/com.shopee.vn/d' /data/system/users/0/settings_ssaid.xml` xóa sạch entry lưu vết của Shopee, buộc Android OS tạo lại entry mới dựa theo SSAID vừa nạp.
  - **[FIX-2] Xóa tận gốc AccountManager:**
    - Tuyến Root: Thực thi lệnh SQLite trực tiếp `DELETE FROM accounts WHERE type LIKE '%shopee%' OR type LIKE '%sea.com%'` trên `/data/system_de/0/accounts_de.db` (Android 7+) và `/data/system/accounts.db` (Android <7).
    - Tuyến Non-Root: Phân tích `dumpsys account` tìm tên account Shopee và gọi `cmd account remove --account-name "..." --account-type "..."`.
  - **[FIX-4 & FIX-5] Quét xóa 7 đường dẫn ẩn & Verify chống Scoped Storage:**
    - Duyệt xóa toàn bộ 7 đường dẫn trên `/sdcard/`.
    - Chạy vòng lặp `ls -d` xác minh từng đường dẫn. Nếu Scoped Storage trên Android 11+ gây quyền *Permission denied*, pipeline tự động leo quyền `su -c 'rm -rf ...'` để xóa dứt điểm.
  - **Reset GAID/GSF:** Gọi `pm clear com.google.android.gms` và `pm clear com.google.android.gsf`.
  - **[FIX-DeepRoot]:** Phát broadcast trigger đột biến thông số phần cứng tới PrivacyKit (`com.sal.privacykit.RANDOMIZE`) hoặc Device Masker nếu có.

---

### Lớp 4: Giám Sát Địa Chỉ IP Mạng & Trạm Thu Phát Sóng (Cellular Telemetry)
* **Cơ chế Shopee:**
  - Shopee kiểm tra Public IP khi thanh toán đơn hàng. Nếu nhiều tài khoản dùng chung một địa chỉ IP trong khoảng thời gian ngắn hoặc IP nằm trong blacklist gian lận, Shopee lập tức nhả lỗi **M02 / D02**.
  - Kiểm tra loại card mạng: Nếu phát hiện `tun0`, `ppp0`, `p2p0`, Shopee gắn cờ sử dụng VPN/Proxy.
  - Đối chiếu mã mạng di động (MNC/MCC) để xác thực kết nối thật.
* **Giải pháp đã triển khai trong `DeviceNetworkService.cs` & `ShopeeBypassPipeline.cs`:**
  - **Nhận diện Interface đa Chipset:** Không chỉ hỗ trợ `rmnet` (Qualcomm cũ) hay `ccmni` (MediaTek), regex mở rộng bắt chuẩn toàn bộ:
    - Samsung Exynos: `pdp_ip`
    - Qualcomm Android 12+: `v4-rmnet`, `rmnet_data`
    - Unisoc / Spreadtrum: `seth`
    - USB Modem / Dongle: `wwan`, `data0`
    - IPv6 translation: `clat`
  - **[FIX-3] Chuyển đổi Chế Độ Máy Bay Tương Thích ROM Tùy Biến:**
    - Thử `cmd connectivity airplane-mode enable/disable`.
    - Nếu gặp ROM lược bỏ lệnh này (Xiaomi MIUI, HyperOS, Vivo Funtouch, OneUI), tự động kích hoạt tuyến Fallback: `settings put global airplane_mode_on` kết hợp phát broadcast `android.intent.action.AIRPLANE_MODE`.
  - **Adaptive IP Poller (SC-12.10):** Chụp snapshot Public IP trước khi đảo mạng, chờ modem đàm phán IP mới với trạm BTS, lặp lại polling kiểm tra xác thực `CurrentIP != PreviousIP` (timeout 30 giây) trước khi mở ứng dụng.

---

### Lớp 5: Hệ Thống MINT & Phân Tích Hành Vi Người Dùng (AI Anti-Fraud)
* **Cơ chế Shopee:**
  Shopee MINT (Machine Learning Anti-Fraud Engine) thu thập dữ liệu cảm biến và hành vi:
  - Vận tốc thao tác (Velocity): Đăng nhập, thêm giỏ hàng, áp mã, nhấn đặt hàng trong vài giây là dấu hiệu của tool tự động.
  - Tọa độ chạm cố định: Click liên tục vào cùng tọa độ X, Y tuyệt đối không có độ lệch chuẩn.
  - Cách khởi chạy app: Khởi chạy từ ADB monkey hoặc command shell thiếu referrer launcher.
* **Giải pháp trong hệ thống:**
  - Khởi chạy app sạch thông qua Explicit Intent: `am start -n com.shopee.vn/com.shopee.app.ui.home.HomeActivity_`, fallback `monkey` nếu cần, kèm kiểm tra `pidof` xác minh tiến trình đã tồn tại.
  - Khi tự động hóa thao tác (Action Engine): Tuân thủ nguyên tắc SC-12: Áp dụng Humanize Delays (nghỉ ngẫu nhiên giữa các bước), áp dụng độ lệch vị trí (touch jitter/entropy), và tôn trọng trạng thái UI ổn định (`wait_for_ui_stable`) thay vì gửi input mù quáng.

---

## Bảng Đối Chiếu: Kiến Trúc Cũ vs. Pipeline Thực Tế

| Tiêu Chí Kỹ Thuật | Kiến Trúc Cũ (Basic Script) | Pipeline Thực Tế (`ShopeeBypassPipeline.cs`) |
|---|---|---|
| **Môi Trường Thực Thi** | Giả định cố định | `DiscoverEnvironmentAsync()` tự động phân tích Root, API level, ROM, Module (KR-06) |
| **Bypass SSAID** | Chỉ chạy `settings put` | Đột biến 2 tầng: `settings put` + Root `sed -i` xóa `settings_ssaid.xml` (Android 8+) |
| **AccountManager** | Bỏ qua hoặc chỉ gửi broadcast | Root: Trực tiếp xóa SQLite `accounts_de.db`<br>Non-Root: Phân tích `dumpsys` + `cmd account remove` |
| **Xóa File Rác Ẩn** | Chỉ xóa 2 đường dẫn | Quét sạch 7 đường dẫn ẩn + verify `ls -d` + leo quyền `su` cho Scoped Storage |
| **Bật/Tắt Airplane** | Chỉ dùng `cmd connectivity` | Hỗ trợ Fallback kép cho MIUI / HyperOS / Vivo / OneUI (`settings put global` + broadcast) |
| **Nhận Diện Mạng Di Động**| Chỉ bắt `rmnet`, `ccmni` | Mở rộng 8 mẫu interface: `pdp_ip`, `v4-rmnet`, `rmnet_data`, `seth`, `wwan`, `clat` |
| **Đổi IP Di Động** | Sleep cứng 3.5s | Adaptive Poller so sánh `CurrentIP != PreviousIP` (timeout 30s) chuẩn SC-12.10 |
| **Khởi Chạy Ứng Dụng** | Chỉ gọi `monkey` | Explicit Intent `HomeActivity_` + kiểm tra `pidof` tiến trình |

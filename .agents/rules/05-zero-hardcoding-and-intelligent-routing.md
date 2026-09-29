---
trigger: always_on
---

# RULE 05: ZERO HARDCODING & INTELLIGENT STRATEGY ROUTING

## 1. Nguyên Tắc Cốt Tử (Core Invariants)

1. **Zero Hardcoded Environment (KR-06):**
   - Tuyệt đối không giả định thiết bị là Root hay Non-Root, không gán cứng model điện thoại, MAC address, Serial, hay đường dẫn lưu trữ.
   - Mọi thông số môi trường (Root status `su`, module Xposed/PrivacyKit/HMA, loại card mạng `rmnet`/`wlan`) bắt buộc phải được truy vấn động tại runtime trước khi thực thi.

2. **No First-Solution Bias & Intelligent Routing (SC-12.1):**
   - Khi thực thi bypass hoặc thao tác hệ thống, Agent không được chạy cố định một sequence mù quáng.
   - Phải phân tích Snapshot môi trường (`DeviceEnvironment`) để lựa chọn nhánh thực thi tối ưu nhất:
     - **Có Root + Xposed/PrivacyKit:** Kích hoạt Broadcast mutation cấp phần cứng + xóa sâu bằng `su -c rm -rf`.
     - **Non-Root ADB:** Dùng CSPRNG SSAID override qua `settings put secure` + reset GAID/GSF + AccountManager eviction.

3. **No Fake Success & Verification Gates (SC-12.10 & SC-12.11):**
   - Mọi hành động làm sạch (Wipe Cache, Reset SSAID, Gạt Airplane Mode) bắt buộc phải có bước quan sát (Observation) và xác thực (Verification):
     - Xóa folder `/sdcard/.shopee` -> Kiểm tra lệnh `ls` để xác nhận đã biến mất hoàn toàn.
     - Sinh SSAID -> Kiểm tra `settings get secure android_id` xem ID mới đã được OS ghi nhận chưa.
     - Gạt Airplane -> Chờ modem di động đàm phán IP mới với trạm BTS, verify `CurrentIP != PreviousIP` trước khi cho phép khởi động ứng dụng.

4. **Modular Architecture & Single Responsibility (SRP):**
   - Tách biệt hoàn toàn các domain: ADB Core (`AdbExecutor`), Device Telemetry (`DeviceTelemetryService`), Network (`DeviceNetworkService`), Backup (`ShopeeBackupService`), và Bypass Engine (`ShopeeBypassPipeline`).
   - Mọi tương tác từ tầng UI phải thông qua Facade hoặc Interface contract.

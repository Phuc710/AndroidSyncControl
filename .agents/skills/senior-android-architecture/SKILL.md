---
name: senior-android-architecture
description: >
  Tiêu chuẩn thiết kế kiến trúc hệ thống điều khiển Android đa nền tảng,
  phân tách domain services, quản lý ADB process an toàn, và định tuyến chiến lược thông minh (SC-11 / SC-12).
triggers:
  - "thiết kế kiến trúc android"
  - "senior android architecture"
  - "quản lý tiến trình adb"
  - "phân rã service"
  - "intelligent strategy selection"
---

# Skill: Senior Android System & Telemetry Architecture

## 1. Bản Đồ Phân Tách Trách Nhiệm (Domain Separation)

Một hệ thống điều khiển và tự động hóa Android cấp độ Senior tuyệt đối không gom toàn bộ code vào một class helper duy nhất. Hệ thống phải phân tách thành 5 tầng độc lập:

```text
Services/
├── Adb/                     # Tầng giao tiếp ADB thô, quản lý process, buffer UTF-8, timeout
│   ├── AdbExecutor.cs
│   ├── AdbInputService.cs
│   ├── AdbPackageService.cs
│   └── AdbMediaService.cs
├── Device/                  # Tầng telemetry, nhận diện phần cứng, phân giải màn hình & network
│   ├── DeviceTelemetryService.cs
│   └── DeviceNetworkService.cs
├── Backup/                  # Tầng lưu trữ phiên làm việc, data snapshot & metadata JSON
│   ├── ShopeeBackupInfo.cs
│   └── ShopeeBackupService.cs
└── Bypass/                  # Tầng nghiệp vụ cốt lõi, pipeline SC-11 / SC-12
    ├── IShopeeBypassPipeline.cs
    ├── ShopeeBypassConfig.cs
    └── ShopeeBypassPipeline.cs
```

---

## 2. Tiêu Chuẩn Thực Thi ADB Process (Process Discipline)

1. **Async Non-Blocking:** Luôn sử dụng asynchronous process execution (`OutputDataReceived`, `ErrorDataReceived`), không bao giờ gọi `proc.StandardOutput.ReadToEnd()` đồng bộ gây freeze UI thread.
2. **Deterministic Timeout & Kill:** Mọi lệnh ADB phải có `timeoutMs`. Khi hết timeout, tự động `proc.Kill()` và thu hồi tài nguyên, không để zombie process khóa cổng 5037.
3. **Safe Shell Quoting:** Khi gửi chuỗi ký tự qua ADB Shell (`input text`), bắt buộc escape ký tự nháy đơn (`' -> '\''`) để tránh shell injection hoặc syntax error.

---

## 3. Intelligent Decision Matrix (Chuẩn SC-12)

Trước khi thực thi bất kỳ thao tác nhạy cảm nào, hệ thống phải chụp Snapshot trạng thái thực tế:

```csharp
var env = await DiscoverEnvironmentAsync(deviceId);
if (env.IsRooted && env.HasPrivacyKit)
{
    // Nhánh 1: Kích hoạt hook phần cứng sâu ở tầng Zygote/LSPosed
    await TriggerDeepRootHardwareMutationAsync(deviceId, env);
}
else
{
    // Nhánh 2: Fallback an toàn qua ADB Settings & GAID reset
    await MutateDeviceIdentityAsync(deviceId, env);
}
```

---

## 4. Verification Gates (No Fake Success)

| Action | Verification Method | Action Khi Fail |
|---|---|---|
| **Wipe tracking folder** | `ls -d /sdcard/.shopee` | Leo thang quyền `su -c rm -rf` nếu có Root |
| **Rotate SSAID** | `settings get secure android_id` | Thử lại qua `su` hoặc log warning |
| **Airplane Rotate** | Adaptive poller so sánh `CurrentIP != PreviousIP` | Lặp lại toggle tối đa 3 lần hoặc log cảnh báo kết nối |
| **Launch Target App** | `pidof <package_name>` | Fallback mở qua Explicit Activity Intent |

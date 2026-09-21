---
name: adb-toolchain-manager
description: >
  Quản trị tập trung runtime ADB & scrcpy, giải quyết triệt để lỗi phân mảnh
  daemon (version mismatch), giải phóng port 5037 và dọn dẹp tiến trình mồ côi.
triggers:
  - "lỗi adb"
  - "server version doesn't match"
  - "adb crash"
  - "quản lý toolchain"
  - "kill adb"
  - "giải phóng port adb"
---

# Skill: ADB & Scrcpy Toolchain Governance

## 1. Nguyên Lý Giải Quyết Đường Dẫn Đơn Điểm (Single Canonical Resolver)

Hiện tượng lỗi phổ biến nhất trong các dự án Android Tooling là **xung đột phiên bản ADB**:
```
adb server version (32) doesn't match this client (41); killing...
* daemon started successfully *
```
Điều này xảy ra khi ứng dụng gọi một file `adb.exe`, trong khi scrcpy gọi một file `adb.exe` khác tại một thư mục khác.

### Thuật Toán `AndroidToolchain`:
Lớp `AndroidToolchain.cs` duyệt ngược cây thư mục từ thư mục chứa file thực thi (`AppDomain.CurrentDomain.BaseDirectory`) lên thư mục gốc dự án để luôn tìm thấy duy nhất:
- `tools/android/adb/adb.exe` (v1.0.41)
- `tools/android/scrcpy/scrcpy.exe` (v3.1)

### Ép Buộc Scrcpy Dùng Chung Daemon:
Khi gọi `Process.Start` cho `scrcpy.exe`, **bắt buộc** truyền biến môi trường:
```csharp
psi.EnvironmentVariables["ADB"] = AndroidToolchain.AdbPath;
```
Lệnh này chỉ thị scrcpy không được dùng bất kỳ file `adb.exe` nào khác ngoài file chuẩn của dự án.

---

## 2. Kịch Bản Diệt Tiến Trình Mồ Côi & Giải Phóng Port 5037 (Process Reaper)

Khi thiết bị rút đột ngột hoặc app crash ngoài ý muốn, port 5037 có thể bị treo bởi tiến trình cũ.

### Lệnh PowerShell Giải Phóng Nhanh:
```powershell
# 1. Diệt toàn bộ tiến trình scrcpy và adb đang chạy ngầm
Get-Process -Name "scrcpy", "adb" -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Khởi động lại daemon chuẩn từ thư mục tools
& "tools\android\adb\adb.exe" kill-server
& "tools\android\adb\adb.exe" start-server

# 3. Kiểm tra danh sách thiết bị
& "tools\android\adb\adb.exe" devices
```

### Lệnh Command Prompt / Batch (1-Click Recovery):
```cmd
taskkill /f /im scrcpy.exe /t >nul 2>&1
taskkill /f /im adb.exe /t >nul 2>&1
tools\android\adb\adb.exe kill-server
tools\android\adb\adb.exe start-server
tools\android\adb\adb.exe devices
```

---

## 3. Kiểm Tra Tiến Trình Chiếm Giữ Port 5037
Nếu ADB không thể start server do port bị chiếm:
```cmd
netstat -ano | findstr :5037
# Tìm PID ở cột cuối cùng, sau đó diệt tiến trình chiếm dụng:
taskkill /f /pid <PID>
```

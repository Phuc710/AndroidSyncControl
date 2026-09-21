---
name: adb-fingerprint
description: >
  Skill để reset/spoof toàn bộ device fingerprint trên mọi thiết bị Android
  qua ADB (no-root) hoặc root route. Dùng khi cần đổi sang acc mới,
  hoặc khi máy bị Shopee flag M02/D02/L01.
triggers:
  - "reset fingerprint"
  - "đổi ID máy"
  - "bypass M02"
  - "spoof device"
  - "clean fingerprint"
---

# Skill: ADB Fingerprint Reset (Universal Android)

## Mô tả

Reset toàn bộ device fingerprint để Shopee nhận diện đây là một thiết bị mới,
bao gồm: Android ID, GAID, Shopee Device Token, IP, và các System Props (root).
Tương thích với mọi dòng máy Android (Samsung, Xiaomi, Oppo, Vivo, Realme, Pixel...).

## Prerequisite

- ADB Platform Tools đã cài đặt trong `tools/android/adb/`
- Điện thoại Android bật USB Debugging + kết nối cáp USB với máy tính
- (Root route) Magisk + LSPosed + PrivacyKit đã cài nếu muốn fake thông số phần cứng sâu

## No-Root Route — Thứ tự bắt buộc (KR-01 → KR-03)

### Step 1: Generate Android ID mới
```python
import secrets
new_android_id = secrets.token_hex(8)  # 16 ký tự hex, crypto-random
```

```powershell
$bytes = New-Object byte[] 8
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$newHex = ($bytes | ForEach-Object { '{0:x2}' -f $_ }) -join ''
```

### Step 2: Set + Verify Android ID
```bash
adb shell settings put secure android_id <new_id>
# Verify:
adb shell settings get secure android_id
# Expected output: <new_id>
```

Lệnh này hoạt động trực tiếp qua quyền ADB Shell tiêu chuẩn mà không cần root thiết bị.

### Step 3: Reset GAID
```bash
adb shell am broadcast -a "com.google.android.gms.ads.identifier.service.RESET"
adb shell pm clear com.google.android.gms
```

**Note thực tế:** `pm clear com.google.android.gms` sẽ reset Google Play Services data,
device sẽ re-sync lại. Chờ 5–10 giây sau bước này.

### Step 4: Wipe Shopee Data (KR-03)
```bash
adb shell pm clear com.shopee.vn
# Verify: output phải là "Success"
```

### Step 5: Spoof System Props (có thể không persist, temp only)
```bash
# Temp — reset khi reboot
adb shell setprop ro.serialno RF8MXXXXXX
adb shell setprop ro.product.model "SM-A505F"

# Verify:
adb shell getprop ro.serialno
adb shell getprop ro.product.model
```

### Step 6: Spoof MAC Address
```bash
# Cần root hoặc Android < 10
adb shell ip link set wlan0 address 02:xx:xx:xx:xx:xx

# Generate fake MAC (locally administered bit set):
# Byte đầu: bit 1 set (locally admin), bit 0 clear (unicast)
# Ví dụ hợp lệ: 02:3a:b4:c1:d2:e5
```

### Step 7: Flip Airplane Mode — đổi IP (KR-02)
```bash
adb shell cmd connectivity airplane-mode enable
# Sleep 3 giây
adb shell cmd connectivity airplane-mode disable
# Sleep 4 giây — chờ 4G reconnect
```

**Verify IP đã đổi:** Dùng `adb shell curl ifconfig.me` hoặc check trên Shopee.

---

## Root Route — PrivacyKit (Spoof đầy đủ)

Khi PrivacyKit đã cài qua LSPosed:

1. Mở LSPosed Manager → Modules → PrivacyKit → Enable for `com.shopee.vn`
2. PrivacyKit Settings → Randomize All → Apply
3. Force stop Shopee: `adb shell am force-stop com.shopee.vn`
4. Chạy Step 7 (airplane mode flip)

PrivacyKit spoof: Android ID, IMEI, Build Serial, Device Model, `ro.*` props, `/proc/cpuinfo`

---

## Checklist sau khi reset

- [ ] Android ID đã đổi (verify bằng `adb shell settings get secure android_id`)
- [ ] Shopee data đã clear (output "Success")
- [ ] IP đã đổi (verify)
- [ ] Delay đủ thời gian trước khi mở Shopee (min 5 giây)

## Xem thêm
- `scripts/py/core/fingerprint.py` — Python implementation
- `scripts/ps/bypass_shopee.ps1` — PowerShell implementation
- Rule KR-01, KR-02, KR-03 trong `rules/03-keeprule.md`

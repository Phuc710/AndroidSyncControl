name: xposed-setup
description: >
  Skill hướng dẫn flash Custom Recovery (TWRP/OrangeFox) + Magisk + LSPosed + PrivacyKit/XPrivacyLua
  trên điện thoại Android (Android 7.0 - 13+).
  Dùng khi cần root route để spoof fingerprint sâu cấp phần cứng.
triggers:
  - "setup root android"
  - "flash magisk"
  - "cài lsposed"
  - "privacykit config"
  - "xposed setup"
---

# Skill: Xposed / Magisk Setup (Universal Android Root Route)

## Prerequisites
- Máy tính Windows đã cài ADB / Fastboot driver hoặc Samsung USB Driver
- Bản Custom Recovery (TWRP / OrangeFox) tương thích với thiết bị
- File zip cài đặt Magisk (hoặc Magisk APK đổi tên `.zip`)
- Thiết bị đã Unlock Bootloader (OEM Unlocking: ON)
- Backup dữ liệu trước khi flash

---

## Phase 1: Unlock OEM + Enable Download Mode

```
Settings → Developer Options → OEM Unlocking → Enable
Settings → Developer Options → USB Debugging → Enable

# Vào Download Mode:
Tắt máy → Giữ Volume Down + Home + Power
(Hoặc: adb reboot download)
```

---

## Phase 2: Flash TWRP bằng Odin3

```
1. Tải Odin3 v3.14.x (stable nhất cho Samsung J-series)
2. Tải TWRP cho j3xlte:
   https://twrp.me/samsung/samsunggalaxj3pro.html
   File: twrp-3.x.x-0-j3xlte.img

3. Odin3:
   - AP slot → chọn twrp-3.x.x-0-j3xlte.img
   - Options → Auto Reboot: UNCHECK (quan trọng!)
   - Start

4. Khi Odin báo PASS:
   Ngay lập tức giữ Volume Up + Home + Power để boot vào TWRP
   (Nếu reboot trước, Samsung sẽ overwrite TWRP bằng stock recovery)
```

---

## Phase 3: Flash Magisk qua TWRP

```
1. Copy Magisk-v27.x.zip vào internal storage
   (Dùng TWRP file manager hoặc adb push)

2. Trong TWRP:
   Install → chọn Magisk-v27.x.zip → Swipe to Flash

3. Reboot System

4. Cài Magisk Manager APK:
   adb install Magisk-v27.x.apk
```

### Verify Magisk
```bash
adb shell su -c "id"
# Expected: uid=0(root) gid=0(root)
```

---

## Phase 4: Enable Zygisk + Cài LSPosed

```
Magisk App:
  → Settings → Zygisk: ON
  → Reboot

Cài LSPosed (Zygisk variant):
  Tải: https://github.com/LSPosed/LSPosed/releases
  File: LSPosed-v1.x.x-xxxx-zygisk-release.zip

  Magisk → Modules → Install from storage → chọn LSPosed zip → Reboot
```

---

## Phase 5: Cài PrivacyKit

```
Tải APK: https://github.com/Xposed-Modules-Repo/com.sal.privacykit/releases
adb install com.sal.privacykit.apk

LSPosed Manager:
  → Modules → Privacy Kit → Enable
  → Scope: chọn "com.shopee.vn"
  → Reboot (hoặc force-stop Shopee)
```

### PrivacyKit Config cho Shopee
```
Privacy Kit App → Profile cho Shopee:
  ✅ Android ID → Random
  ✅ IMEI → 000000000000000 (hoặc random)
  ✅ Serial → Random
  ✅ Build.MODEL → random từ pool
  ✅ Build.MANUFACTURER → Samsung (giữ realistic)
  ✅ GAID → Random (nếu supported)
```

**Quan trọng:** Mỗi lần đổi acc → vào PrivacyKit → Randomize All → Apply
→ Force stop Shopee → Airplane flip → Login acc mới

---

## Phase 6: Root Hiding (cho SafetyNet/Root Detection)

Android 7 Shopee không dùng Play Integrity nhưng vẫn check root:

```
Magisk → Settings → MagiskHide: ON
MagiskHide → Thêm com.shopee.vn

# Hoặc cài Shamiko (mạnh hơn):
Flash shamiko-x.x.x-release.zip qua Magisk Modules
```

### Verify SafetyNet pass
```bash
# Dùng YASNAC app hoặc SafetyNet Test
adb shell am start -n rikka.safetynetchecker/.MainActivity
```

---

## XPrivacyLua (Fallback nếu không dùng PrivacyKit)

```
Tải: https://github.com/M66B/XPrivacyLua/releases
adb install XPrivacyLua.apk

LSPosed → Enable XPrivacyLua → Scope: com.shopee.vn

XPrivacyLua App → Chọn Shopee:
  ✅ Identification (IMEI, Android ID, Serial)
  ✅ Location (optional)
  ✅ Sensor (optional — tắt gyroscope để chống device fingerprint qua sensor)
```

**Note:** XPrivacyLua không còn maintained. Nếu có vấn đề compatibility thì
ưu tiên PrivacyKit.

---

## Checklist sau setup

- [ ] `adb shell su -c id` → trả về uid=0
- [ ] Zygisk enabled trong Magisk
- [ ] LSPosed hiện trong notification drawer
- [ ] PrivacyKit enabled, scope = Shopee
- [ ] MagiskHide / Shamiko = Shopee trong list
- [ ] Test: mở Shopee → không báo "môi trường không an toàn"

## Xem thêm
- flow.md → Root workflow diagram
- Rule KR-01 (fingerprint riêng biệt)

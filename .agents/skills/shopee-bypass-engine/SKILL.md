---
name: shopee-bypass-engine
description: >
  Kỹ năng điều phối toàn diện quy trình làm sạch telemetry thiết bị Android,
  triệt tiêu vector định danh để bypass mã lỗi rủi ro Shopee M02 / D02 / L01 / M04.
  Hỗ trợ định tuyến chiến lược thông minh (Adaptive Routing) cho cả máy Rooted (Xposed/LSPosed) và Non-Root.
triggers:
  - "bypass shopee"
  - "sửa lỗi M02"
  - "lỗi D02"
  - "fix L01"
  - "làm sạch thiết bị"
  - "đổi IP Shopee"
  - "reset SSAID Shopee"
  - "deep root bypass"
---

# Skill: Shopee Bypass Engine (M02 / D02 / L01 / M04 Mitigation)

## 1. Bản Đồ Mã Lỗi Shopee (Risk Matrix)

| Mã Lỗi | Nguyên Nhân Gốc | Mức Độ | Biện Pháp Kỹ Thuật Senior |
|---|---|---|---|
| **M02 / D02** | Dấu vân tay thiết bị (SSAID + Local Token + Account Token) nằm trong blacklist áp mã giảm giá. | Thiết bị | Thực thi trọn vẹn Pipeline làm sạch + Xóa AccountManager + Đổi SSAID + Đổi IP 4G. |
| **L01** | Giới hạn số lượng tài khoản đăng nhập trên cùng một ID phần cứng. | Thiết bị | Xóa sạch session token, reset SSAID, GAID và GSF ID. |
| **M04** | Flag bất thường môi trường (Root detection, Proxy/VPN leak, Emulator signature). | Môi trường | Giấu Root bằng Shamiko + chặn quét app bằng HideMyApplist + xóa GSF token. |
| **M01 / D01** | Tài khoản bị khóa trực tiếp từ máy chủ (Server-side Fraud Flag). | Tài khoản | Không thể bypass bằng thiết bị; bắt buộc đổi tài khoản mới. |

---

## 2. Intelligent Adaptive Pipeline (Chuẩn SC-11 / SC-12)

Pipeline tự động nhận diện môi trường thực tế tại runtime và lựa chọn nhánh thực thi tối ưu nhất:

```mermaid
flowchart TD
    A[Bắt Đầu: Trigger Bypass] --> B[Phase 0: Environment Discovery (Root, Xposed, Network Type)]
    B --> C[Phase 1: Scorched Earth - Wipe Data & /sdcard/.shopee]
    C --> D{Verify /sdcard/.shopee đã biến mất?}
    D -- Chưa --> E[Leo thang quyền su -c rm -rf]
    D -- Đã sạch --> F[Phase 2: Mutate Device SSAID qua CSPRNG Hex]
    E --> F
    F --> G[Phase 3: Reset GAID com.google.android.gms & GSF com.google.android.gsf]
    G --> H[Phase 4: Evict Shopee AccountManager Tokens ngoài Sandbox]
    H --> I{Thiết bị có Root + PrivacyKit?}
    I -- Có --> J[Phase 5: Broadcast Intent kích hoạt Hardware Hook Mutation]
    I -- Không --> K[Bỏ qua Phase 5]
    J --> L[Phase 6: Gạt Airplane Mode -> Delay 2s]
    K --> L
    L --> M[Phase 7: Tắt Airplane Mode -> Adaptive IP Polling 3.5s-5s]
    M --> N{Verify IP Public mới khác IP cũ?}
    N -- Chưa đổi --> M
    N -- Đã đổi --> O[Phase 8: Launch Clean Intent & Process Verification]
```

---

## 3. Quy Trình Chi Tiết Từng Bước

### Bước 0: Nhận Diện Môi Trường (Real State Discovery)
- Kiểm tra quyền Root: `su -c id`
- Kiểm tra module Xposed/PrivacyKit: `pm list packages | grep com.sal.privacykit`
- Kiểm tra card mạng: `rmnet`/`ccmni` (Cellular) vs `wlan` (WiFi)

### Bước 1: Tiêu Hủy Bằng Chứng (Scorched Earth)
```bash
adb shell am force-stop com.shopee.vn
adb shell pm clear com.shopee.vn
adb shell rm -rf /sdcard/Android/data/com.shopee.vn /sdcard/.shopee /sdcard/Android/media/com.shopee.vn
# Verification Gate:
adb shell ls -d /sdcard/.shopee 2>/dev/null
```

### Bước 2: Sinh & Ghi Đè Android ID (SSAID)
```bash
adb shell settings put secure android_id <16_hex_chars_csprng>
# Verification Gate:
adb shell settings get secure android_id
```

### Bước 3: Reset Google Advertising ID (GAID) & Google Services Framework (GSF)
```bash
adb shell pm clear com.google.android.gms
adb shell pm clear com.google.android.gsf
```

### Bước 4: Xóa Persistent Tokens Trong Android AccountManager
Quét các tài khoản Shopee/Sea nằm ngoài App Sandbox và phát broadcast thu hồi:
```bash
adb shell am broadcast -a android.accounts.action.ACCOUNT_REMOVED --es account_name "<NAME>" --es account_type "<TYPE>"
```

### Bước 5: Kích Hoạt Phần Cứng Sâu (Nếu Có Root + PrivacyKit)
```bash
adb shell am broadcast -a com.sal.privacykit.RANDOMIZE
adb shell am broadcast -a com.device.id.masker.RANDOMIZE
```

### Bước 6 & 7: Gạt Airplane Mode & Adaptive IP Poller
```bash
adb shell cmd connectivity airplane-mode enable
# Nghỉ 2 giây
adb shell cmd connectivity airplane-mode disable
# Vòng lặp quan sát IP mới (Adaptive Poller) với timeout 25s:
adb shell ip -f inet addr
```

### Bước 8: Khởi Động & Xác Thực Tiến Trình
```bash
adb shell monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1
# Verification Gate:
adb shell pidof com.shopee.vn
```

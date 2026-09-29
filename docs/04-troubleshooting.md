# 🔧 Cẩm Nang Xử Lý Sự Cố (Troubleshooting Guide)

Tài liệu này tổng hợp toàn bộ các tình huống lỗi thường gặp trong quá trình kết nối và vận hành **ShopeeControl**, kèm theo nguyên nhân và phương án khắc phục nhanh.

---

## 1. Lỗi Kết Nối ADB & Thiết Bị

### 🔴 Lỗi 1: Thiết bị hiển thị trạng thái `Unauthorized` hoặc `Offline`
- **Hiện tượng**: App báo không tìm thấy thiết bị hoặc không điều khiển được, chạy lệnh `adb devices` thấy dòng chữ `unauthorized`.
- **Nguyên nhân**: Bạn chưa cấp quyền cho máy tính trên màn hình điện thoại hoặc khóa màn hình khiến ADB bị ngắt kết nối tạm thời.
- **Cách khắc phục**:
  1. Mở khóa màn hình điện thoại.
  2. Rút cáp USB ra và cắm lại vào cổng USB phía sau thùng máy tính (để nguồn điện và tín hiệu ổn định hơn).
  3. Khi điện thoại hiện thông báo *"Cho phép gỡ lỗi USB?"*, hãy **tích vào ô ☑ "Luôn cho phép từ máy tính này"** rồi nhấn **OK**.
  4. Nếu vẫn không hiện, vào **Cài đặt -> Cài đặt cho người phát triển -> Thu hồi quyền ủy quyền gỡ lỗi USB (Revoke USB debugging authorizations)**, sau đó cắm lại cáp.

---

### 🔴 Lỗi 2: Báo lỗi `ADB unavailable` / Không tìm thấy file `adb.exe`
- **Hiện tượng**: Màn hình hiển thị thông báo đỏ *"ADB unavailable"* hoặc *"The system cannot find the file specified"*.
- **Nguyên nhân**: File `adb.exe` bị phần mềm diệt virus (Windows Defender / Antivirus) chặn hoặc đường dẫn bị thay đổi.
- **Cách khắc phục**:
  1. Mở PowerShell tại thư mục dự án và chạy script kiểm tra:
     ```powershell
     .\scripts\setup\setup.ps1
     ```
  2. Kiểm tra xem thư mục `tools\android\adb\` có đủ 4 file: `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll`, `libwinpthread-1.dll`.
  3. Nếu thiếu, tắt tạm tính năng quét file tải về của antivirus rồi khôi phục lại file.

---

## 2. Lỗi Truyền Màn Hình Scrcpy

### 🔴 Lỗi 3: Scrcpy vừa bật lên bị tắt ngay (Crash MediaCodec)
- **Hiện tượng**: Cửa sổ kết nối hiện lên rồi văng ngay lập tức, log ghi nhận lỗi `MediaCodec$CodecException`.
- **Nguyên nhân**: Bộ giải mã phần cứng (Hardware Encoder) trên một số dòng máy đời cũ hoặc ROM tùy biến bị quá tải, lỗi driver hoặc không hỗ trợ profile mã hóa mặc định của scrcpy.
- **Cách khắc phục**:
  - Hệ thống [`ScrcpyProfile`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/UI/Helpers/ScrcpyProfile.cs) đã tích hợp sẵn cơ chế **Auto-Fallback**: Khi phát hiện scrcpy văng trong vòng 4 giây đầu, hệ thống sẽ tự động ghi nhớ Serial thiết bị và chuyển sang bộ mã hóa phần mềm an toàn chuẩn Google (`OMX.google.h264.encoder`).
  - Hệ thống tự động phục hồi kết nối lại trong ~1.5 giây, hoặc bạn có thể bấm nút **"Retry"** trên giao diện để kết nối lại ngay lập tức.
  - Ngoài ra, kiểm tra file `setting.json` đảm bảo:
    ```json
    "IsAudio": false,
    "MaxSize": 720
    ```

---

### 🔴 Lỗi 4: Màn hình scrcpy bị lag, giật hoặc trễ cao
- **Nguyên nhân**: Cáp USB chất lượng kém hoặc truyền khung hình với FPS quá cao.
- **Cách khắc phục**:
  1. Đổi cáp USB khác hoặc cắm vào cổng USB 3.0 (màu xanh dương).
  2. Mở file `setting.json` tại thư mục gốc và chỉnh thông số:
     ```json
     "MaxFps": 24,
     "ViewPercent": 25.0
     ```
  3. Khởi động lại ứng dụng.

---

## 3. Lỗi Tính Năng Nhập Liệu & Chuột

### 🔴 Lỗi 5: Click chuột bị lệch toạ độ (Bấm phím G lại ăn thành T)
- **Hiện tượng**: Click chuột vào hàng phím thứ 3 trên bàn phím ảo (ví dụ chữ `G`) nhưng hệ thống lại nhận thành chữ `T` (hàng phím thứ 2 nằm ngay phía trên).
- **Nguyên nhân**: Cửa sổ `scrcpy.exe` (chạy SDL2) mặc định được Windows tạo kèm thanh tiêu đề (Title Bar) cao khoảng 30–35px. Khi app dùng Win32 `SetParent` và `SetWindowLong` lột thanh tiêu đề để nhúng vào giao diện, SDL2 bên trong không cập nhật lại Non-Client metrics, khiến toàn bộ toạ độ click chuột bị dịch lệch lên trên đúng 1 hàng phím (~32px).
- **Cách khắc phục**:
  - Hệ thống đã tự động kích hoạt cờ `--window-borderless` trong [`ScrcpyProfile.cs`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/UI/Helpers/ScrcpyProfile.cs) để cửa sổ khởi tạo không viền ngay từ đầu.
  - Gọi Win32 `SetWindowPos` kèm cờ `SWP_FRAMECHANGED` để đồng bộ toạ độ chuột chuẩn xác từng pixel.

---

### 🔴 Lỗi 6: Dán text (Ctrl+V hoặc Send) bị ra ký tự rác hoặc nội dung clipboard cũ (`NEWUSER...`)
- **Hiện tượng**: Copy email mới (ví dụ `paulcampbell350@dewii.dpdns.org`), ấn nút **Send** hoặc nhấn `Ctrl + V` nhưng trên điện thoại lại paste ra đoạn chữ cũ rích trước đó (`NEWUSERIYHGRW4RE`) hoặc bị bộ gõ Telex trên điện thoại biến dạng (`aádasdadaádasdad`).
- **Nguyên nhân**:
  1. Khi cửa sổ scrcpy bị nhúng vào WPF dưới dạng Child Window, lệnh phím ảo `Ctrl + V` (`PostMessage`) không có quyền đồng bộ clipboard từ máy tính sang Android. Điện thoại chỉ nhận phím Paste và lôi nội dung clipboard cũ trong máy ra dán.
  2. Bàn phím điện thoại đang bật chế độ gõ Tiếng Việt Telex.
- **Cơ chế khắc phục (Direct ADB Input Text Injection)**:
  - Cả nút **Send** trên Sidebar và phím tắt **`Ctrl + V`** toàn cục đều được chuyển sang cơ chế **Direct ADB Input Text Injection**:
    ```bash
    adb -s <deviceId> shell input text '<escaped_string>'
    ```
  - Bơm trực tiếp từng ký tự vào ô đang focus trên Android, chấp hết mọi ký tự đặc biệt (`@`, `.`, `_`, `-`, số, chữ hoa/thường), không còn phụ thuộc vào clipboard của Android hay scrcpy.
  - *Lưu ý:* Nên chuyển bàn phím ảo trên điện thoại sang **English (US)** hoặc ấn ẩn bàn phím ảo trước khi paste để tránh bộ gõ Telex tự động ghép dấu.

---

### 🔴 Lỗi 7: Bấm "Đổi IP (4G)" nhưng IP không thay đổi
- **Hiện tượng**: Bấm nút đổi IP thành công nhưng kiểm tra lại IP mạng vẫn giữ nguyên như cũ.
- **Nguyên nhân**:
  1. Điện thoại đang kết nối Wi-Fi (Chế độ máy bay chỉ làm mới IP của mạng dữ liệu di động SIM 4G, không đổi được IP modem Wi-Fi nhà bạn).
  2. Thiết bị chưa bật tính năng Dữ liệu di động (Mobile Data).
- **Cách khắc phục**:
  1. Vuốt thanh thông báo trên điện thoại xuống, **tắt Wi-Fi**.
  2. **Bật Dữ liệu di động (4G)**.
  3. Bấm lại nút **"Đổi IP (4G)"** trên giao diện ShopeeControl.

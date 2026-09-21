# 📖 Hướng Dẫn Sử Dụng AndroidSyncControl (User Guide)

**AndroidSyncControl** là ứng dụng điều khiển, truyền hình ảnh trực tiếp (Screen Mirroring) và quản lý thiết bị Android chuyên dụng trên máy tính Windows, tích hợp sẵn các công cụ điều khiển phần cứng, kiểm tra thông số máy và tự động hóa thao tác thiết bị.

---

## 1. Yêu Cầu Hệ Thống & Chuẩn Bị Thiết Bị

### Yêu cầu máy tính
- **Hệ điều hành**: Windows 10 / Windows 11 (64-bit).
- **Runtime**: [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) trở lên (đã cài sẵn khi chạy môi trường dev).
- Cổng kết nối USB hoạt động tốt, cáp dữ liệu Micro-USB / Type-C truyền được dữ liệu (không dùng cáp chỉ sạc).

### Chuẩn bị trên điện thoại Android (Tương thích mọi hãng: Samsung, Xiaomi, Oppo, Vivo, Realme, Pixel...)

#### Bước 1: Kích hoạt chế độ "Tùy chọn nhà phát triển" (Developer Options)
Tùy theo giao diện từng hãng, thao tác như sau:
- **Samsung (One UI)**: Mở *Cài đặt* -> *Thông tin điện thoại* -> *Thông tin phần mềm* -> Nhấn liên tục **7 lần** vào dòng **Số hiệu bản tạo (Build number)**.
- **Xiaomi / Redmi / POCO (MIUI / HyperOS)**: Mở *Cài đặt* -> *Giới thiệu điện thoại* -> Nhấn liên tục **7 lần** vào dòng **Phiên bản OS / Phiên bản MIUI**.
- **Oppo / Realme (ColorOS / Realme UI)**: Mở *Cài đặt* -> *Giới thiệu thiết bị* -> *Phiên bản* -> Nhấn liên tục **7 lần** vào **Số bản dựng**.
- **Vivo / iQOO (Funtouch OS / OriginOS)**: Mở *Cài đặt* -> *Giới thiệu điện thoại* (hoặc *Quản lý hệ thống*) -> *Thông tin phần mềm* -> Nhấn **7 lần** vào **Số bản dựng**.
- **Google Pixel / Motorola / Android thuần**: Mở *Cài đặt* -> *Giới thiệu về điện thoại* -> Kéo xuống dưới cùng nhấn **7 lần** vào **Số bản dựng**.

Khi thành công, màn hình sẽ hiển thị thông báo: *"Bạn đã là nhà phát triển!"*.

#### Bước 2: Bật "Gỡ lỗi USB" (USB Debugging)
1. Quay lại màn hình **Cài đặt** chính.
2. Tìm và chọn mục: **Cài đặt cho người phát triển** (hoặc nằm trong *Cài đặt bổ sung* / *Hệ thống*).
3. Gạt công tắc sang **BẬT (ON)**.
4. Kéo tìm dòng **Gỡ lỗi USB (USB debugging)** và gạt sang **BẬT** -> Nhấn **OK**.
5. *(Riêng Xiaomi / HyperOS)*: Bật thêm **Cài đặt qua USB** và **Gỡ lỗi USB (Cài đặt bảo mật)** để cấp toàn quyền input chuột/bàn phím.

#### Bước 3: Cấp quyền kết nối máy tính
1. Cắm cáp USB nối điện thoại với máy tính.
2. Trên màn hình điện thoại sẽ xuất hiện hộp thoại: *"Cho phép gỡ lỗi USB?" (Allow USB debugging?)*.
3. **Tích chọn ô**: ☑ **"Luôn cho phép từ máy tính này" (Always allow from this computer)**.
4. Bấm **OK**.

---

## 2. Khởi Chạy Ứng Dụng

Tại thư mục gốc của dự án `ROOT_Shopee`, bạn có thể khởi động theo 2 cách:

### Cách 1: Khởi động nhanh (Khuyên dùng)
- Click đúp vào file:
  ```bat
  sync_control.bat
  ```
  File này sẽ tự động tìm bản build mới nhất của ứng dụng và khởi chạy ngay lập tức.

### Cách 2: Khởi động qua PowerShell
- Mở PowerShell tại thư mục dự án và chạy:
  ```powershell
  .\scripts\dev\run.ps1
  ```

---

## 3. Chi Tiết Các Tính Năng Trên Giao Diện

Giao diện ứng dụng chia làm 2 khu vực chính:
- **Khu vực trung tâm (Màn hình điện thoại)**: Hiển thị trực tiếp màn hình cảm ứng điện thoại qua scrcpy siêu mượt, độ trễ cực thấp. Có thể dùng chuột để vuốt, chạm, click và dùng bàn phím máy tính để thao tác.
- **Thanh công cụ bên phải (Sidebar)**: Tập hợp các tính năng điều khiển và quản lý chuyên sâu.

```
┌──────────────────────────────────────────────┬────────────────────────────┐
│                                              │ 📱 TÊN THIẾT BỊ / SERIAL   │
│                                              │ 🟢 Trạng thái kết nối      │
│                                              ├────────────────────────────┤
│                                              │ THIẾT BỊ:                  │
│                                              │ • [Phone Info] [Power]     │
│                                              │ • [Reboot]     [Screenshot]│
│                                              ├────────────────────────────┤
│             MÀN HÌNH SCRCPY                  │ BỘ NHỚ TẠM & VĂN BẢN:      │
│         TRUYỀN TRỰC TIẾP TỪ                  │ [ Ô nhập văn bản / Link  ] │
│             ĐIỆN THOẠI                       │ • [Gửi text]   [Dán nhanh] │
│       (Thao tác chuột & bàn phím)            ├────────────────────────────┤
│                                              │ MẠNG:                      │
│                                              │ • [Fake Proxy] [ADB Shell] │
│                                              │ • [Đổi IP 4G]              │
│                                              ├────────────────────────────┤
│                                              │ QUẢN LÝ SHOPEE:            │
│                                              │ • [Mở Shopee]              │
│                                              │ • [Bypass Shopee] (Hot)    │
│                                              ├────────────────────────────┤
│                                              │ ◀ Back   ⌂ Home   ≡ Menu   │
└──────────────────────────────────────────────┴────────────────────────────┘
```

### 1. Nhóm Điều Khiển Thiết Bị (Device)
- **Phone Info**: Đọc ngay lập tức thông số phần cứng thiết bị gồm: Android ID (SSAID), Model, Serial Number và địa chỉ IP hiện tại (Wi-Fi LAN / 4G / VPN).
- **Power**: Bật hoặc tắt màn hình điện thoại (tương đương nhấn nút Nguồn vật lý).
- **Reboot**: Khởi động lại điện thoại (có hộp thoại xác nhận tránh bấm nhầm).
- **Screenshot**: Chụp màn hình điện thoại và tự động lưu vào thư mục `screenshots/` trên máy tính, đồng thời mở thư mục chứa ảnh.

### 2. Nhóm Nhập Liệu & Bộ Nhớ Tạm (Clipboard & Text)
- **Ô nhập văn bản (`txt_input`)**: Tự động bắt nội dung clipboard máy tính khi vừa click vào ô.
- **Gửi text (`btn_send_text`)**: Gõ chuỗi ký tự trong ô vào ô đang chọn trên điện thoại.
- **Dán trực tiếp (`btn_paste_direct`)**: Hỗ trợ truyền ký tự tiếng Việt có dấu Unicode, ký tự đặc biệt mà bàn phím gõ tay ADB thông thường không gõ được.

### 3. Nhóm Mạng & Kết Nối (Network)
- **Đổi IP (4G)**: Tự động bật và tắt chế độ máy bay (Airplane Mode) sau 3 giây để nhà mạng cấp phát dải IP di động 4G/LTE mới.
- **Fake Proxy**: Hộp thoại cấu hình HTTP Proxy (`host:port`). Để trống và bấm Áp dụng nếu muốn gỡ bỏ proxy.
- **ADB Shell**: Mở ngay cửa sổ `cmd.exe` kết nối trực tiếp vào ADB shell của thiết bị hiện tại để chạy các lệnh quản trị cấp thấp.

### 4. Nhóm Quản Lý Shopee & Bypass (Shopee Control)
- **Mở Shopee**: Khởi chạy ứng dụng `com.shopee.vn`.
- **Bypass Shopee (Quy trình 6 bước tự động)**:
  1. Đóng ứng dụng và xóa toàn bộ dữ liệu session token cũ (`pm clear com.shopee.vn`, dọn thư mục cache ngoài).
  2. Tạo ngẫu nhiên chuỗi Hex 16 ký tự và nạp Android ID (SSAID) mới vào `settings secure`.
  3. Xóa dữ liệu Google Play Services (`com.google.android.gms`) để làm mới GAID.
  4. Kích hoạt Chế độ máy bay để ngắt kết nối mạng.
  5. Tắt Chế độ máy bay để nhận IP mới từ trạm phát sóng.
  6. Tự động mở Shopee sạch sẽ như một thiết bị mới hoàn toàn.

### 5. Thanh Phím Điều Hướng Ảo (Navigation)
Nằm ở góc dưới cùng thanh bên phải, hỗ trợ điều hướng nhanh mà không cần với tay bấm phím vật lý trên điện thoại:
- **Phím Menu (`≡`)**: Mở trình quản lý đa nhiệm (Recent Apps).
- **Phím Home (`⌂`)**: Về màn hình chính.
- **Phím Back (`◀`)**: Quay lại màn hình trước.

---

## 4. Tùy Chỉnh Cấu Hình Giao Diện (`setting.json`)

File `setting.json` nằm tại thư mục gốc của dự án cho phép tùy chỉnh hành vi mặc định của ứng dụng:

```json
{
  "ViewPercent": 25.0,
  "MaxFps": 30,
  "MaxSize": 0,
  "Timeout": 8000,
  "UseGpu": false,
  "IsAudio": false,
  "Theme": "Dark",
  "Language": "vi"
}
```

- `MaxFps`: Giới hạn khung hình scrcpy (mặc định 24 - 30 fps để tiết kiệm tài nguyên máy).
- `Theme`: Chế độ giao diện (`"Dark"`, `"Light"`, hoặc `"System"` theo Windows).
- `Language`: Ngôn ngữ hiển thị (`"vi"` cho tiếng Việt, `"en"` cho tiếng Anh).
- `IsAudio`: Bật/tắt truyền âm thanh (Android cũ nên để `false` để tránh lỗi giải mã âm thanh).

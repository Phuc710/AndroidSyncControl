# 🏛️ QUY CHUẨN KIẾN TRÚC C# .NET 8 WPF (01-csharp-wpf-architecture.md)

Tài liệu này quy định các tiêu chuẩn kỹ thuật khi phát triển, bảo trì và refactor mã nguồn C# và giao diện WPF trong dự án **ShopeeControl**.

---

## 1. Kiến Trúc Phân Tầng (Layered Separation)
Mã nguồn được tổ chức thành 4 tầng rõ ràng, nghiêm cấm việc rò rỉ logic tầng dưới lên tầng giao diện:

```
UI (Views, UserControls, XAML)
       │
       ▼  (Data Binding, ICommand, INotifyPropertyChanged)
ViewModels (MainWVM, ComboboxVM)
       │
       ▼  (Events, State Machine)
Supervisors & Services (DeviceConnectionSupervisor, ShopeeBypassService, ScrcpyProfile)
       │
       ▼  (ProcessStartInfo, Win32 P/Invoke)
Platform & Toolchain (AndroidToolchain, NativeMethods, tools/android/)
```

- **Views (`UI/MainWindow.xaml`, `UI/Controls/*.xaml`)**: Chỉ chịu trách nhiệm hiển thị và nhận tương tác người dùng. Code-behind (`.xaml.cs`) chỉ chứa logic liên quan trực tiếp đến Win32 Window handle (`HwndSource`), hoạt họa (animations), hoặc đo đạc kích thước giao diện.
- **ViewModels (`UI/ViewModels/`)**: Quản trị trạng thái của UI thông qua `INotifyPropertyChanged`. Không trực tiếp gọi lệnh `Process.Start` hay thao tác với Win32 Handle.
- **Supervisors (`UI/Helpers/DeviceConnectionSupervisor.cs`)**: Đóng vai trò máy trạng thái hữu hạn (FSM) giám sát thiết bị, quản lý vòng đời tiến trình scrcpy, tự động kết nối lại khi có sự cố.
- **Services (`UI/Helpers/ShopeeBypassService.cs`, `ScrcpyProfile.cs`)**: Đóng gói các nghiệp vụ chuyên biệt thành các hàm thuần túy hoặc luồng bất đồng bộ độc lập.

---

## 2. Tiêu Chuẩn Giao Diện XAML & Theming

### 2.1 Tuyệt Đối Sử Dụng DynamicResource
Mọi thuộc tính màu sắc, brush, và style của control bắt buộc phải liên kết qua `DynamicResource`:
```xml
<!-- ĐÚNG: Tự động đổi màu mượt mà khi switch Dark/Light theme -->
<Border Background="{DynamicResource PrimaryBackgroundBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1" />

<!-- SAI: Hardcode màu sắc trực tiếp -->
<Border Background="#1E1E2E" BorderBrush="Gray" />
```

### 2.2 Đa Ngôn Ngữ Hóa (Localization i18n)
Mọi chuỗi văn bản hiển thị trên UI phải được khai báo trong từ điển ngôn ngữ (`Localization/Strings.vi.xaml` và `Localization/Strings.en.xaml`):
```xml
<!-- ĐÚNG -->
<TextBlock Text="{DynamicResource Str_BypassShopee}" />

<!-- SAI -->
<TextBlock Text="Làm sạch thiết bị (Bypass)" />
```

### 2.3 Quản Lý Cửa Sổ Không Viền (Custom Window Chrome)
Khi dùng `WindowStyle="None"` kèm `AllowsTransparency="False"`:
- Bắt buộc phải gắn hook thông điệp cửa sổ qua `WindowMaximizeHelper.Register(this)` trong sự kiện `SourceInitialized`.
- Xử lý thông điệp Win32 `WM_GETMINMAXINFO` để hệ điều hành Windows không làm mất phần viền cửa sổ hoặc thanh taskbar khi người dùng phóng to ứng dụng.

---

## 3. Tiêu Chuẩn Win32 Interop & P/Invoke

### 3.1 Tập Trung Định Nghĩa P/Invoke
Mọi hàm Win32 API phải được khai báo trong lớp chuyên dụng hoặc vùng `NativeMethods`:
- Sử dụng đúng kiểu dữ liệu an toàn (`IntPtr` cho HWND/HANDLE, `uint` cho message flags, `RECT` struct cho tọa độ).
- Khai báo rõ thuộc tính `[DllImport("user32.dll", SetLastError = true)]`.

### 3.2 Cơ Chế Nhúng Cửa Sổ Scrcpy (Window Embedding Pattern)
1. Khởi động tiến trình `scrcpy.exe` với cờ `--window-title="<unique_title>"`.
2. Sử dụng vòng lặp thăm dò (polling loop) có giới hạn thời gian (tối đa 5.000ms) kết hợp `FindWindow(null, windowTitle)`.
3. Khi tìm thấy HWND hợp lệ:
   - Thay đổi thuộc tính cửa sổ: Loại bỏ viền và thanh tiêu đề (`WS_BORDER`, `WS_CAPTION`, `WS_THICKFRAME`) bằng `SetWindowLongPtr`.
   - Thiết lập cửa sổ cha: `SetParent(scrcpyHwnd, hostContainerHwnd)`.
   - Đồng bộ kích thước và vị trí: Gọi `MoveWindow` hoặc `SetWindowPos` với cờ `SWP_NOACTIVATE | SWP_NOZORDER | SWP_FRAMECHANGED`.
4. Khi cửa sổ WPF resize: Bắt sự kiện `SizeChanged` và cập nhật lại vị trí scrcpy HWND tương ứng.

---

## 4. Quản Lý Vòng Đời & Thu Hồi Tài Nguyên (IDisposable / Clean Shutdown)
- Mọi tiến trình con (`Process`) khi khởi tạo phải được lưu trữ tham chiếu.
- Khi ứng dụng đóng (`App.Current.Exit` hoặc `MainWindow_Closing`):
  ```csharp
  if (scrcpyProcess != null && !scrcpyProcess.HasExited)
  {
      scrcpyProcess.Kill(entireProcessTree: true);
      scrcpyProcess.Dispose();
  }
  ```
- Luôn dọn dẹp các Hook Win32 (`RemoveClipboardFormatListener`, gỡ bỏ `HwndSourceHook`) trước khi hủy cửa sổ để chống rò rỉ bộ nhớ (Memory Leak) và handle leak.

---

## 5. Quy Chuẩn Bất Đồng Bộ (Async/Await Discipline)
- Hàm async phải luôn hỗ trợ nhận `CancellationToken`.
- Không sử dụng `async void` ngoại trừ các Event Handler cấp giao diện (ví dụ: `private async void OnButtonClick(...)`).
- Khi đọc luồng đầu ra (`StandardOutput`, `StandardError`) của tiến trình ngoài, ưu tiên dùng `BeginOutputReadLine()` kết hợp sự kiện `DataReceived` hoặc `ReadToEndAsync()` để tránh tình trạng deadlock bộ đệm (Buffer Deadlock).

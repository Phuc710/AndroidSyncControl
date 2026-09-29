# SC-13: BEHAVIORAL ANTI-FRAUD & HUMANIZED EXECUTION

## Mục tiêu
Hệ thống Automation không chỉ cần qua mặt các công cụ dò quét phần cứng hay ứng dụng (Lớp 1-4), mà còn phải đánh lừa được hệ thống phân tích hành vi của Machine Learning (Lớp 5 - MINT). Sự hoàn hảo trong code không nằm ở tốc độ, mà nằm ở tính "giống người".

## SC-13.1 — NO STATIC COORDINATES
Không bao giờ được click vào cùng một tọa độ tĩnh nhiều lần. Hành vi click của người dùng thật luôn có sự sai số.

```csharp
// ❌ CẤM SỬ DỤNG
await _adb.ExecuteAsync($"input tap {targetX} {targetY}");
```

```csharp
// ✅ BẮT BUỘC SỬ DỤNG
int jitterX = Random.Shared.Next(-15, 15);
int jitterY = Random.Shared.Next(-15, 15);
await _adb.ExecuteAsync($"input tap {targetX + jitterX} {targetY + jitterY}");
```

## SC-13.2 — DYNAMIC DELAYS
Người dùng thật có độ trễ suy nghĩ (cognitive delay) trước khi thực hiện hành động tiếp theo. Automation phải mô phỏng được độ trễ này.

* Thao tác nối tiếp nhanh (Scroll, tap): Delay 200ms - 600ms.
* Chuyển trang/Load UI mới: Delay 1000ms - 3000ms.
* Quyết định quan trọng (Checkout, Add to Cart): Delay ngẫu nhiên từ 3000ms - 8000ms.

## SC-13.3 — TYPING VELOCITY
Không gõ toàn bộ text bằng một lệnh `input text` dài, đặc biệt với các form login hoặc search. Hệ thống MINT đo vận tốc gõ phím.

* Thay vì truyền toàn bộ string trong 1ms, hãy cân nhắc chèn các nhịp nghỉ hoặc copy-paste (nếu app cho phép).

## SC-13.4 — ACCOUNT WARMING PATTERN
Agent không được vào thẳng màn hình thanh toán.
Mọi Playbook thực thi chốt đơn (Checkout) hoặc tạo tài khoản mới phải bao gồm các Action mồi (Warming Actions):
1. Lướt Home page (Scroll up/down).
2. Tương tác với banner.
3. Xem ít nhất 1-2 sản phẩm phụ trước khi vào sản phẩm chính.
4. Có khoảng nghỉ giữa các bước.

## Tóm lại (Core Rule)
> **If it looks like a script, clicks like a script, and acts like a script... MINT will ban it like a script.**
>
> **Làm cho nó chậm lại. Làm cho nó có sai số. Đừng tối ưu hóa thời gian chạy, hãy tối ưu hóa tỷ lệ sống sót của tài khoản.**

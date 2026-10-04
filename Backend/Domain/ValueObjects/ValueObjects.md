## 🧩 Value Object là gì?

**Value Object** là một kiểu đối tượng trong domain model dùng để biểu diễn các khái niệm **không cần định danh (ID)**. Thay vì quan tâm đến "đối tượng nào", bạn chỉ quan tâm đến **giá trị bên trong** của nó.

---

### 📌 Đặc điểm của Value Object

- **Không có Identity**: Không cần ID duy nhất. Hai Value Object có cùng giá trị thì được xem là giống nhau.
- **Bất biến (Immutable)**: Sau khi tạo, giá trị không thay đổi.
- **So sánh bằng giá trị**: Dùng `Equals()` hoặc `==` để so sánh nội dung, không phải tham chiếu.
- **Tự kiểm tra hợp lệ khi khởi tạo**: Logic kiểm tra thường nằm trong constructor hoặc factory method.

---

### 📦 Ví dụ thực tế

- `Email`: bạn không cần biết đây là email số mấy, chỉ cần biết nó có hợp lệ hay không.
- `Money`: biểu diễn số tiền, đơn vị tiền tệ.
- `Address`: địa chỉ giao hàng, không cần ID.

---

### 🛡️ Validation trong Value Object

Value Object thường **tự kiểm tra tính hợp lệ** ngay khi được tạo. Ví dụ:

```csharp
public class Email
{
    public string Value { get; }

    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("@"))
            throw new ArgumentException("Email không hợp lệ");

        Value = value;
    }

    public override bool Equals(object obj) =>
        obj is Email other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();
}
```

## Benefits of Using Value Objects

**Value Object** giúp bạn mô hình hóa những khái niệm **không cần identity** (không cần ID duy nhất).

---

### 📌 Ví dụ

- Email  
- Địa chỉ  
- Số tiền  

Bạn chỉ quan tâm đến **giá trị bên trong**, không quan tâm "thằng này là Email số 1 hay số 2".

---

### ✅ Đảm bảo tính hợp lệ ngay từ khi tạo

Value Object thường có **logic validate trong constructor hoặc factory**.

Ví dụ: bạn **không thể tạo Email mà không có ký tự @**.  
→ Điều này **đẩy luật nghiệp vụ xuống domain** thay vì rải rác trong service.

---

### 🔁 Giảm duplication logic

Nếu bạn **không dùng VO**, mỗi chỗ nhập Email, Address… lại phải validate.

Dùng VO → **chỉ validate một lần duy nhất khi tạo**, sau đó **tái sử dụng ở mọi nơi**.

---

### 🔒 Immutable (bất biến)

Giá trị của Value Object thường **không thay đổi sau khi tạo**.

Nếu muốn thay đổi → bạn **tạo mới**.

→ Giúp code **an toàn, dễ reason** (không lo bị sửa lung tung như entity).

---

### 💬 Tăng tính diễn đạt của domain

Value Object:

Code an toàn hơn (quản lý logic trong constructor / factory).

Tránh trùng lặp logic (luôn validate từ khi tạo).

Đơn giản hóa so sánh (chỉ cần so giá trị).

So sánh 2 Value Object dễ dàng hơn: **chỉ cần so sánh giá trị**.

Ví dụ:
new Email("a@gmail.com") == new Email("a@gmail.com") // → true
# ChoThueGiupViec

Nền tảng cho thuê người giúp việc dọn dẹp nhà theo ca: Khách đặt đơn, hệ thống điều phối Thợ tự do hoặc Thợ của Agency.

## Language

### Con người

**Khách (Customer)**:
Người đặt đơn thuê dọn dẹp. Khách chọn gói, ca và địa chỉ; không chọn kỹ năng của Thợ.
_Avoid_: client, người dùng

**Thợ (Worker)**:
Người trực tiếp dọn dẹp tại nhà Khách. Mọi Thợ đều làm cùng một việc là dọn dẹp; được nhận việc tức là đủ năng lực dọn dẹp.
_Avoid_: nhân viên, người giúp việc (trong code)

**Thợ tự do (Freelancer)**:
Thợ tự đăng ký, đủ điều kiện nhận việc sau khi qua eKYC và Admin duyệt.

**Thợ Agency (Agency Staff)**:
Thợ thuộc một Agency, đủ điều kiện nhận việc nhờ cam kết bảo lãnh điện tử của Agency, không qua hàng đợi Admin duyệt.

**Agency (Đối tác B2B)**:
Công ty cung cấp Thợ cho đơn Premium, chịu trách nhiệm bằng Quỹ Ký quỹ và điểm SLA.
_Avoid_: partner company, công ty dịch vụ

### Bỏ khỏi sản phẩm

**Skill (Kỹ năng)** — đã bỏ (leader, 2026-10-13). Sản phẩm chỉ có dịch vụ dọn dẹp và Khách không chọn kỹ năng, nên không có danh mục Skill, không có kỹ năng của Thợ, không ghép ca theo kỹ năng.
_Avoid_: skill, kỹ năng yêu cầu, ma trận kỹ năng

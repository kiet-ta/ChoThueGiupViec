# M1 — Nền tảng (BASE backend) + Identity + Customer

> **Bắt buộc đọc trước khi làm bất kỳ task nào: [`../decisions.md`](../decisions.md) (tiếng Anh, giá trị & quy tắc đã chốt). Task ghi `decisions: Q##` → đọc mục đó. Mâu thuẫn → decisions.md thắng; thiếu → hỏi leader, không đoán.**
> Vai trò: **người dựng base backend — đường găng của cả nhóm.** Làm G0 trước (nhanh, tối thiểu) để M2–M6 không phải đợi, rồi G1, rồi Identity (G2).
> Tổng quan, gate, quy tắc chống xung đột, port/event: [00-overview.md](00-overview.md). Spec: [../spec.md](../spec.md).
> Quy ước: `[ ]`→`[x]` + `— evidence: <log|PR#>` trong chính PR của task. *needs* = phải xong trước. Số liệu PRD → xem `spec.md`.

**Allowed mặc định** — ticket `BASE-*`: `Backend/**`, `.spec/plan/**` (+ nguyên văn `Backend/appsettings*.json` khi đụng cấu hình).
Ticket module: Identity, Customers theo mẫu ở overview §3 (+ `Mobile/lib/features/{identity,customers}/**`).

## Wave 0 — BASE backend (G0 → G1). Làm theo thứ tự; thông báo cả nhóm khi mỗi gate mở.

- [x] **BASE-01** Skeleton & project test — *needs:* — · Tạo quy ước thư mục module (`Features/<Module>`, `Controllers/<Module>`, `Infrastructure/Modules/<Module>`, `Tests/<Module>`) cho 12 module (Identity, Customers, Booking, Payments, Dispatch, Workers, Agencies, Skills, Ratings, Disputes, Payouts, Admin); tạo xUnit project dưới `Backend/Tests`, loại khỏi `CommonService.csproj`, thêm vào `CommonService.sln`; cập nhật `Backend/ARCHITECTURE.md`. *done:* `dotnet test` chạy 1 test mẫu PASS (log). — evidence: `.harness/evidence/20261004-220319-L3.log` (L3 PASSED; `Passed! Failed: 0, Passed: 48, Total: 48` in CommonService.Tests.dll, test `ModuleFolderConventionTests.Module_folder_exists`)
- [ ] **BASE-02** Auto-đăng ký module — *needs:* BASE-01 · `IModule` quét assembly: controller/MediatR handler/validator/EF config/DI của module tự nạp; `Program.cs` không phải sửa khi thêm module. *done:* test thêm module giả mà KHÔNG sửa `Program.cs` vẫn resolve được dịch vụ.
- [ ] **BASE-03** Port + DTO + Fake — *needs:* BASE-02 · Tạo mọi port ở overview §4 (interface + DTO + Fake in-memory, đăng ký `TryAdd` sau các module để bản thật thắng); gồm cả `IPasswordHasher`. *done:* mỗi port có Fake + 1 test; bảng port trong `ARCHITECTURE.md`.
- [ ] **BASE-04** Event catalog + dispatcher — *needs:* BASE-02 · Các event ở overview §5 (MediatR notification) + test publish/subscribe xuyên module bằng handler giả.
- [ ] **BASE-05** `BusinessRules` options + cấu hình — *needs:* BASE-02 · Options class gom hằng số nghiệp vụ (overview §3.8) + placeholder config cho MoMo/VietQR, OTP, eKYC, storage; **giá trị mặc định lấy đúng bảng ở `decisions.md` §4** (không tự đặt số). appsettings **chỉ placeholder, không secret**. *allowed thêm:* `Backend/appsettings*.json` (nguyên văn).
- [x] **BASE-06** Domain: 27 bảng + state machine — *needs:* BASE-01 · Dựng từ `Backend/GiupViec_Physical_DB_MVP5.drawio`: entity `partial`, enum, value object (tái dùng `Money`/`Address`), `Worker` STI (`worker_type` FREELANCER|AGENCY_STAFF, `agency_id` null cho freelancer), `JobAssignment` nút giao phẳng (`order_id, customer_id, worker_id, agency_id, slot_id, service_tier, payout_amount`), state machine `JobOrder`/`JobAssignment`/`Worker` (PAID, ASSIGNED, COMPLETED, IDLE… theo PRD). *done:* test unit mọi chuyển trạng thái hợp lệ/không hợp lệ. **Thêm schema SC-1..SC-8 ở `decisions.md` §3** (cột `PARTNER_AGENCY`, bảng `PRICE_RULE`, `ADMIN_AUDIT_LOG`, `ESCROW_TRANSACTION`, UNIQUE rating, bảng `OTP_CODE`, `REFRESH_TOKEN`, cột khoá đăng nhập trên `ADMIN`/`PARTNER_AGENCY`) và cập nhật drawio hoặc ghi delta vào `Backend/ARCHITECTURE.md`; phải có trạng thái assignment riêng "thợ tự huỷ" (decisions Q15). — evidence: `.harness/evidence/20261004-234207-L3.log` (L3 PASSED; `Passed! Failed: 0, Passed: 446, Total: 446`; tests `Backend/Tests/Domain/StateMachineTests.cs`, `DomainModelTests.cs`, `JobOrderProgressTests.cs`, `VndTests.cs`; decisions D1-D7 in `Backend/ARCHITECTURE.md` section 2.4)
- [ ] **BASE-07** Persistence EF Core — *needs:* BASE-06 · SQL Server (D1), `AppDbContext`, `IEntityTypeConfiguration` mỗi entity (tự áp dụng), `UNIQUE(worker_id, slot_date, shift_code)` trên BOOKING_SLOT, index `worker_id/agency_id/customer_id/order_id` trên JOB_ASSIGNMENT, migration đầu, hướng dẫn chạy DB local. *allowed thêm:* `Backend/appsettings*.json` (placeholder chuỗi kết nối). *done:* `dotnet ef database update` + test schema (log). **→ mở G1.**
- [ ] **BASE-08** Repository/UoW — *needs:* BASE-07 · Quy ước repo mỗi aggregate + `IUnitOfWork`; module chỉ thêm repo trong thư mục của mình.
- [ ] **BASE-09** Gỡ code mẫu template — *needs:* BASE-08 · Xoá demo `User/Order/OrderItem/BankAccount/ShippingCalculator/PaymentIntent` & test liên quan; verify không còn tham chiếu (grep làm bằng chứng).
- [ ] **BASE-10** Seed — *needs:* BASE-07, BASE-03 (`IPasswordHasher`) · (a) **`AdminSeeder` chỉ chạy ở Development**: DB mới → tự tạo 1 admin (`admin@dev.local`, cấu hình `Seed:Admin:*`), chạy lại không tạo trùng, Production không bao giờ chạy — **đặc tả đầy đủ ở overview §10**; (b) seed dữ liệu mặc định ở **mọi môi trường**: 6 dòng `PRICE_RULE` (Q01) và 3 gói FREE/PRO_MONTHLY/PRO_QUARTERLY (Q08) đúng giá trị trong `decisions.md`, cùng Skill mẫu. *allowed thêm:* `Backend/appsettings*.json` (nguyên văn). *done:* 3 bằng chứng ở overview §10 (1 dòng ADMIN; lần 2 vẫn 1; Production 0).
- [ ] **BASE-11** Hạ tầng dùng chung — *needs:* BASE-03 · `IClock`, `ICurrentUser`, `IGeoService` (khoảng cách GPS), `IFileStorage` (đĩa local khi dev) bản thật; phân trang/validation/ProblemDetails/idempotency helper (chống double-click, replay webhook).
- [ ] **BASE-12** Kênh realtime/thông báo — *needs:* BASE-03 · SignalR đã chốt (*decisions:* Q07); hiện thực `INotificationService` (khách: trạng thái thanh toán + theo dõi ca realtime trước; FCM hoãn = Q07b, KHÔNG làm).
- [ ] **BASE-13** OpenAPI — *needs:* BASE-02 · Swagger ổn định (operationId nhất quán, bearer), xuất snapshot vào `.spec/contracts/openapi.json`. **→ mở G4** khi Web/Mobile sinh client chạy được.
- [ ] **BASE-14** Thông báo gate — *needs:* BASE-01..05 / BASE-06..08 · Tick G0 / G1 ở `00-overview.md` (kèm evidence) và báo nhóm. *allowed:* `.spec/plan/00-overview.md`.

## Wave 1 — Identity & Customer (backend)
- [ ] **BE-M1-00** Contract Identity + Customers → `.spec/contracts/identity.md`, `customers.md` (endpoint, DTO, lỗi) · *needs:* — (làm ngay, không cần base) · *done:* người điều phối duyệt → G3 phần M1.
- [ ] **BE-M1-01** Đăng nhập Customer bằng SĐT + OTP (§1.3, §4.1 bước 1): gửi/kiểm OTP (hết hạn, giới hạn thử, rate-limit), tạo Customer lần đầu; dev dùng Fake `IOtpSender` · *needs:* BASE-03 · tham số OTP theo *decisions:* Q06 (6 số, 5 phút, 5 lần thử, cooldown 60 s, giới hạn theo SĐT/IP; Fake bị cấm ngoài Development); SMS thật hoãn = Q06b, KHÔNG làm.
- [ ] **BE-M1-02** JWT access + refresh, role `Customer/Worker/Partner/Admin` + policy, `ICurrentUser` thật · *needs:* BE-M1-01, BASE-07 · **→ mở G2.**
- [ ] **BE-M1-03** Xác thực Worker / Partner / Admin theo *decisions:* Q16 (Customer/Worker = SĐT+OTP; Admin/Partner = email+mật khẩu; chính sách mật khẩu & khoá tài khoản nằm trong decisions) · *needs:* BE-M1-02.
- [ ] **BE-M1-04** Hồ sơ Customer (SĐT, điểm tin cậy) · *needs:* BASE-07.
- [ ] **BE-M1-05** Sổ địa chỉ `CustomerAddress`: loại nhà (chung cư / nhà riêng / phòng trọ), số tầng, **`S_total = S_sàn × N_tầng` tính ở server** (§1.1), toạ độ GPS, chỉ chủ sở hữu thao tác · *needs:* BASE-07.
- [ ] **BE-M1-06** Thợ quen `FavoriteWorker` (thêm/bỏ/liệt kê; bảng `FAVORITE_WORKER` trong drawio) · *needs:* BASE-07.
- [ ] **BE-M1-07** Test module Identity + Customers (OTP hết hạn/quá số lần, S_total, quyền sở hữu địa chỉ) · *needs:* BE-M1-01..06.

## Wave 3 — Mobile (Flutter) · *needs:* nền Mobile của M3 (MOB-BASE-01..03) + G3/G4
- [ ] **MOB-M1-01** Đăng nhập OTP + lưu token.
- [ ] **MOB-M1-02** Sổ địa chỉ (chọn vị trí GPS, nhập diện tích/tầng, hiển thị S_total).
- [ ] **MOB-M1-03** Danh sách thợ quen · **MOB-M1-04** Hồ sơ.

## Hỗ trợ cả nhóm (khi được ticket "Scope exception" yêu cầu)
- [ ] Xử lý thay đổi schema / port được các module đề nghị (mỗi yêu cầu = 1 issue riêng, chạy tuần tự).

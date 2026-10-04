# Kế hoạch triển khai MVP5 — 6 người song song

> Nguồn: `.spec/spec.md` (PRD-MVP5-FINAL) · **Quyết định đã chốt (tiếng Anh, bắt buộc tuân theo): `.spec/decisions.md`** · ERD vật lý: `Backend/GiupViec_Physical_DB_MVP5.drawio` (22 bảng) + 5 bảng bổ sung (`decisions.md` §3) = 27.
> Trạng thái: **DRAFT** — chờ người điều phối duyệt (checkpoint 2 trong HARNESS.md §6). Chưa có ticket GitHub nào cho các task bên dưới.
> Lệnh cho agent: nói **"phân tích dự án"** → agent hỏi tên → in checklist của bạn (xem `AGENTS.md`).

## 0. Nguyên tắc sản phẩm (leader Kiệt chốt, 2026-10-04) — dùng để quyết định mọi chỗ PRD để trống
Đây là **nền tảng**. **Bên bỏ tiền ra nuôi hệ thống được ưu tiên: Khách hàng (trả tiền từng ca) và Doanh nghiệp đối tác/Agency (thuê bao + ký quỹ).** Freelancer là lực lượng được nền tảng **quản lý**, không phải bên được ưu tiên.
Áp dụng khi PRD không nói rõ: (1) tiền của khách/Agency luôn được bảo vệ trước (hoàn tiền, đối soát, không mất giao dịch); (2) khách bị thiệt thì được bù trước, chi phí đẩy sang bên vi phạm (Freelancer bị phạt/khóa, Agency bị trừ ký quỹ); (3) quy tắc đối với Freelancer chặt hơn (eKYC, hậu kiểm, phạt, khóa); (4) trải nghiệm trả tiền (thanh toán, theo dõi, hoàn tiền) làm realtime và chạy ổn định trước các tính năng khác.
**Không** dùng nguyên tắc này để sửa điều PRD đã nói rõ (vd. §2.1 Agency không vào luồng Economy, thợ nhận 80 %, BR-05 thợ nhận 40 %). Mâu thuẫn → hỏi leader, không tự quyết.

## 1. Chia người (mỗi người sở hữu dọc: Backend + Mobile + Web của module mình)

| Slot | Vai trò | Backend modules | Mobile (Flutter, `Mobile/`) | Web (`Frontend/`) | File checklist |
|---|---|---|---|---|---|
| **M1** | **Người dựng BASE backend** + Identity + Customer | Foundation, Identity, Customers | login OTP, sổ địa chỉ, thợ quen | — | `M1-platform-identity.md` |
| **M2** | Đặt đơn & Thanh toán | Booking, Payments | luồng đặt đơn, QR, theo dõi, "Làm lần 2" | — | `M2-booking-payments.md` |
| **M3** | Điều phối & Hiện trường + **Mobile base** | Dispatch (matching, offer 30s, failover, check-in, vắng mặt, sự cố) | nền Flutter, nhận cuốc 30s, báo sự cố | giám sát điều phối (Admin) | `M3-dispatch-field.md` |
| **M4** | Thợ & Chất lượng ca làm | Workers (eKYC, Block Slots, ảnh VoL, nghiệm thu, hoàn tất) | eKYC, lịch rảnh, thi công, nghiệm thu | hậu kiểm eKYC (Admin) | `M4-workers-quality.md` |
| **M5** | Đối tác B2B (Agency) + Skill | Agencies, Skills | lịch ca của thợ Agency | Partner Portal (desktop), danh mục Skill | `M5-agencies-b2b.md` |
| **M6** | Đánh giá, Khiếu nại, Payout, Admin + **Web base** | Ratings, Disputes, Payouts, Admin | đánh giá, khiếu nại, thu nhập | nền Web, Admin console | `M6-ratings-disputes-payouts.md` |

Vì sao tách như vậy: **backend base là đường găng duy nhất → chỉ M1 làm**. Nền Web (M6) và nền Mobile (M3) không phụ thuộc backend (code theo contract) nên chạy **song song** với M1, không ai phải đợi.

## 2. Giai đoạn & cổng (gate) — M1 tick gate kèm bằng chứng

| Gate | Nghĩa là | Mở khoá cho | Done |
|---|---|---|---|
| **G0 Skeleton** | cấu trúc module, project test, auto-đăng ký module, port + DTO + Fake, event catalog, `BusinessRules` options | M2–M6 code backend theo **port/fake**, không cần DB thật | [ ] |
| **G1 Data** | 27 bảng (22 + 5 bổ sung, decisions §3) + EF config + DbContext + migration đầu + repo/UoW | M2–M6 thay fake bằng repo EF, integration test | [ ] |
| **G2 Auth** | OTP + JWT + role/policy + `ICurrentUser` thật | endpoint có phân quyền thật; mobile/web login thật | [ ] |
| **G3 Contract** | contract từng module trong `.spec/contracts/` được duyệt | Web/Mobile code song song với backend bằng mock | [ ] |
| **G4 OpenAPI** | swagger.json xuất ổn định + sinh client TS/Dart chạy được | Web/Mobile thay mock bằng API thật | [ ] |

Thứ tự: **Phase 0** (Wave 0: contract + BASE + nền Web/Mobile) → **Phase 1** (BE module song song) → **Phase 2** (E2E liên module, §7) → **Phase 3** (màn hình Web/Mobile hoàn thiện) → **Phase 4** (hardening). Mobile/Web được phép bắt đầu sớm bằng mock khi G3 xong; "backend lên trước" nghĩa là ưu tiên review/merge BE trước.
Phụ thuộc chéo giữa người (vd. `needs: BE-M4-01`) chỉ chặn bước **tích hợp cuối** của task đó — vẫn code và test được bằng Fake/event; nếu bị kẹt thật, ghi `blocked` vào ticket thay vì làm lách.

## 3. Quy tắc chống xung đột (M1 dựng sẵn ở G0; mọi người tuân thủ)

1. **Mỗi module một thư mục** (sở hữu độc quyền, không ai khác sửa):
   `Backend/Application/Features/<Module>/**` · `Backend/WebAPI/Controllers/<Module>/**` · `Backend/Infrastructure/Modules/<Module>/**` · `Backend/Tests/<Module>/**` · `Frontend/src/features/<module>/**` · `Mobile/lib/features/<module>/**` · `Mobile/test/features/<module>/**`.
2. **File dùng chung bị đóng băng sau G0** (không ai sửa tay): `Backend/Program.cs`, `Backend/Application/DependencyInjection.cs`, `Backend/Infrastructure/DependencyInjection.cs`, DbContext, `Migrations/**`, `appsettings*.json`, `Mobile/lib/app/**` (router/registry), `Frontend/src/app/**`. Module tự đăng ký qua `IModule` / `routes` riêng → không cần sửa chúng.
3. **Schema chỉ M1 sửa** (DB steward). Cần đổi cột/bảng → mở GitHub Issue "Scope exception" gán M1. Không bao giờ hand-merge `ModelSnapshot`; nếu xung đột migration → bỏ migration của mình, rebase, tạo lại.
4. **Hành vi domain thêm bằng `partial class`** (`Domain/Entities/<Entity>.<Module>.cs`, file thuộc module) — không sửa file entity gốc của M1.
5. **Module không gọi trực tiếp module khác**: chỉ qua **port** (§4) hoặc **domain event** (§5), đều do M1 định nghĩa ở G0 kèm Fake. Cần sửa port → Scope exception cho M1.
6. **Contract trước code**: task `*-00` của mỗi module viết `.spec/contracts/<module>.md` (endpoint + DTO + lỗi) → người điều phối duyệt (G3). Agent KHÔNG tự đặt endpoint ngoài contract đã duyệt.
7. **Mỗi người chỉ tick checklist của mình** (file `.spec/plan/Mx-*.md` riêng, nằm trong `allowed` của ticket) → không xung đột khi merge.
8. **Hằng số nghiệp vụ nằm một chỗ** (`BusinessRules` options, BASE-05): bán kính 5/7/10 km, timeout 30 s, GPS ≤100 m, ca ≤4 h, ngưỡng 80 m², hoa hồng 20 %, bồi hoàn vắng mặt 40 %, ngưỡng eKYC 85 %, tỉ lệ hậu kiểm 10–20 %, ngưỡng VoL, … Không rải số cứng trong handler. Giá trị mặc định lấy đúng bảng `decisions.md` §4; không tự đặt số.

9. **`.spec/decisions.md` thắng mọi chỗ PRD để trống** (giá trị, quy tắc, schema bổ sung SC-1..SC-5). Thứ tự ưu tiên: `spec.md` > `decisions.md` > `plan/*.md`. Không có trong cả ba → dừng và hỏi leader trong issue, không đoán.
10. **Tài khoản Admin dev tự tạo** (không cần làm tay để test): xem §10. Mọi agent/dev dùng tài khoản này để kiểm tra API/Web; không tự tạo admin kiểu khác.

### Cấu hình agent dùng chung (đã vào repo, harness kiểm tra)
`.agents/skills` (nguồn) → `.claude/skills` (bản sao sinh tự động) · `.claude/settings.json` (hook chặn lệnh nguy hiểm) · `.codex/rules` (luật lệnh Codex) · `CLAUDE.md` → `AGENTS.md`. Sửa chúng cần ticket liệt kê nguyên văn `.agents/**`, `.claude/**`, `.codex/**`, `CLAUDE.md`; chi tiết ở mục "Skills and agent config" của `AGENTS.md`.

### Allowed paths mặc định của một ticket module (dán vào issue; thêm đúng file cần thêm)
```
Backend/Application/Features/<Module>/**
Backend/WebAPI/Controllers/<Module>/**
Backend/Infrastructure/Modules/<Module>/**
Backend/Tests/<Module>/**
Backend/Domain/Entities/*.<Module>.cs
Frontend/src/features/<module>/**          (nếu ticket có Web)
Mobile/lib/features/<module>/**            (nếu ticket có Mobile)
Mobile/test/features/<module>/**
.spec/plan/<Mx>-*.md
.spec/contracts/<module>.md
```
Ticket `BASE-*` (chỉ M1): `Backend/**`, `.spec/plan/**`, và liệt kê **nguyên văn** `Backend/appsettings*.json` nếu đụng cấu hình. Ticket đụng `harness/**`, `.github/**`, `.husky/**`, `AGENTS.md` phải liệt kê nguyên văn (protected paths).

## 4. Port liên module (M1 tạo interface + DTO + Fake ở BASE-03; "Implementer" thay Fake bằng bản thật)
> Tên là đề xuất; M1 chốt khi làm BASE-03 và ghi vào `Backend/ARCHITECTURE.md`.

| Port | Implementer | Consumers | Ghi chú |
|---|---|---|---|
| `IPaymentGateway` (tạo QR, verify IPN) | M2 | M2, M5 | MoMo QR / VietQR (Q4) |
| `IRefundService` (hoàn 100 %) | M2 | M3 | hết thợ >10 km, sự cố |
| `IAgencyCapacityService` (kiểm công suất + khoá vị trí nguyên tử) | M5 | M2, M3 | Pha 1 Premium |
| `ISlaPenaltyService` (trừ điểm SLA + khấu trừ ký quỹ) | M5 | M3, M6 | Pha 2, tranh chấp |
| `IWorkerAvailabilityQuery` (thợ freelancer rảnh theo slot/vị trí) | M4 | M3 | Economy pool |
| `IWorkerReputation` (rating, tỉ lệ ca thành công) | M6 | M3 | MatchingScore |
| `INotificationService` | M1 | tất cả | kênh realtime chốt ở Q7 |
| `IOtpSender` | M1 | M1 | SMS nhà cung cấp (Q6) |
| `IAuditLog` (ghi `ADMIN_AUDIT_LOG` append-only) | M6 | M2, M5, M6 | sửa giá/gói phải có log (decisions G-5) |
| `IPasswordHasher` | M1 | M1 | băm mật khẩu Admin/Partner (seed admin dev, đăng nhập email+mật khẩu) |
| `IEkycProvider` (OCR CCCD + Face Matching) | M4 | M4 | nhà cung cấp (Q5) |
| `IImageQualityService` (VoL) | M4 | M4 | ngưỡng (Q3) |
| `IFileStorage` | M1 | M4, M6 | ảnh Before/After, bằng chứng |
| `IWorkerProfileQuery` (tên/rating/số ca/trạng thái của thợ + kiểm tra tồn tại) | M4 | M1 | danh sách Thợ quen (decisions Q21 C3); tạm Fake |
| `IClock`, `ICurrentUser`, `IGeoService` | M1 | tất cả | test xác định (timeout 30 s) |

## 5. Domain event (MediatR notification; M1 tạo ở BASE-04)
`OrderPaid` (M2→M3) · `OrderCancelled`/`OrderRefunded` (M2) · `JobAssigned` (M3) · `AssignmentFailed` (M3→M2 hoàn tiền) · `WorkerCheckedIn` (M3) · `CustomerAbsentReported` (M3→M6) · `CustomerAbsentApproved` (M6→M2,M3,M4) · `IncidentReported` (M3) · `JobCompleted` (M4→M6,M5) · `ExtensionPaid` (M2→M4) · `ExtensionDeclined` (M4→M3) · `SubscriptionActivated` (M5) · `DisputeResolved` (M6→M5,M2) · `RatingSubmitted` (M6) · `PayoutBatchClosed` (M6).

## 6. Sở hữu thực thể (22 bảng từ Physical drawio + 5 bảng bổ sung = 27)
Cột/khoá/quan hệ do **M1** quản (BASE-06/07). Hành vi domain thêm bằng partial class của module.

| Module | Entity |
|---|---|
| M1 | CUSTOMER, CUSTOMER_ADDRESS, FAVORITE_WORKER, **OTP_CODE** (mới, SC-6), **REFRESH_TOKEN** (mới, SC-7), cột khoá đăng nhập trên ADMIN/PARTNER_AGENCY (SC-8) |
| M2 | JOB_ORDER, JOB_ORDER_EXTENSION, PAYMENT_TRANSACTION, **PRICE_RULE** (mới, SC-2) |
| M3 | INCIDENT_LOG, CHECK_IN_LOG, JOB_ASSIGNMENT (vòng đời gán/offer/check-in) |
| M4 | WORKER (STI `worker_type`/`agency_id`), JOB_PHOTO, BOOKING_SLOT (freelancer Block Slots), JOB_ASSIGNMENT (vòng đời thi công/nghiệm thu/hoàn tất) |
| M5 | PARTNER_AGENCY (+cột mới SC-1), SUBSCRIPTION_PACKAGE, PARTNER_SUBSCRIPTION, SKILL, WORKER_SKILL, BOOKING_SLOT (Shift Roster Agency), **ESCROW_TRANSACTION** (mới, SC-4) |
| M6 | DISPUTE_TICKET, TWO_WAY_RATING (+UNIQUE SC-5), ADMIN, PAYOUT_BATCH, PAYOUT_ITEM, **ADMIN_AUDIT_LOG** (mới, SC-3) |

`JOB_ASSIGNMENT` là nút giao phẳng dùng chung: **M1 định nghĩa toàn bộ state machine ở BASE-06** (OFFERED→ASSIGNED→CHECKED_IN→IN_PROGRESS→AWAITING_ACCEPTANCE→COMPLETED + CANCELLED/CANCELLED_BY_WORKER/ABSENT/INCIDENT/REASSIGNED; offer được lưu, decisions Q22), M3/M4 chỉ gọi phương thức chuyển trạng thái → không ai sửa chung một file.

## 7. Phase 2 — E2E liên module (sau khi các BE module xong; mỗi dòng 1 ticket `shared`, chạy riêng)
- [ ] **E2E-01** Economy: đặt đơn → thanh toán IPN → PAID → offer 30 s → ASSIGNED (M2+M3)
- [ ] **E2E-02** Hết thợ 5→7→10 km → huỷ + hoàn 100 % (M3+M2)
- [ ] **E2E-03** Premium: kiểm công suất → khoá → auto-assign Agency → failover (M2+M5+M3)
- [ ] **E2E-04** Thi công: check-in → ảnh Before → After → nghiệm thu → COMPLETED + `payout_amount` (M3+M4)
- [ ] **E2E-05** Vắng mặt 15 phút → Admin duyệt → thợ nhận 40 % → IDLE (M3+M6+M2)
- [ ] **E2E-06** Khiếu nại → phán quyết → khấu trừ ký quỹ Agency (M6+M5)
- [ ] **E2E-07** Cuối tháng: Payout batch freelancer (80 %) + Agency tổng hợp → Admin giải ngân (M6)

## 8. Câu hỏi mở — ĐÃ CHỐT (leader Kiệt, 2026-10-04). Chi tiết, giá trị và schema: **`.spec/decisions.md`** (tiếng Anh).
Agent chỉ coi một `Q#` là đã chốt khi cột đầu có `✅`. Dòng `⏳` là hạng mục **hoãn có chủ đích — KHÔNG implement**, hỏi leader trước.

| Trạng thái | Nội dung (mục trong decisions.md) |
|---|---|
| ✅ Q01 | Giá nằm trong DB (`PRICE_RULE`), có giá mặc định, Admin sửa được, mọi lần sửa ghi `ADMIN_AUDIT_LOG` |
| ✅ Q03 | VoL: ngưỡng 100.0, ảnh thu về rộng 640 px, server quyết định |
| ✅ Q04 | Thanh toán: **chỉ MoMo sandbox, không tiền thật**; IPN idempotent + job đối soát; tiền giữ tới khi COMPLETED |
| ✅ Q05 | eKYC giai đoạn đầu **chỉ Fake** |
| ✅ Q06 | OTP: 6 số, 5 phút, 5 lần thử, cooldown 60 s; Fake bị cấm ngoài Development |
| ✅ Q07 | Realtime: SignalR (khách trước) |
| ✅ Q08 | Gói: Free quota 3 / Pro quota 50, ân hạn 7 ngày; giá mặc định có trong decisions.md |
| ✅ Q09 | Ký quỹ min 5.000.000đ, SLA 100 điểm, thứ tự khấu trừ khách-trước, bảng `ESCROW_TRANSACTION` |
| ✅ Q10 | Khách vắng mặt: tính đúng 40 %, hoàn 60 %, điều kiện duyệt chặt |
| ✅ Q11 | Hoa hồng: Freelancer 20 %, Agency Free 20 %, Pro 0 % |
| ✅ Q12 | Super-Freelancer: Admin duyệt tay, tiêu chí chặt, tự thu hồi khi <4.70 |
| ✅ Q13 | Premium đặt trước tối thiểu 4 giờ |
| ✅ Q14 | Đánh giá: cửa sổ 48 giờ, tiêu chí cố định |
| ✅ Q15 | Huỷ: >2 giờ hoàn 100 %, ≤2 giờ tính 40 %; thợ huỷ 3 lần/30 ngày bị khoá |
| ✅ Q16 | Xác thực: Customer/Worker = SĐT+OTP; Admin/Partner = email+mật khẩu |
| ✅ Q17 | Gọi: đếm `call_attempts`, ẩn SĐT khách ngoài khung giờ ca |
| ✅ Q18 | Import `.xlsx` dry-run rồi commit all-or-nothing; xuất payout `.xlsx` |
| ✅ Q19 | Bảo lãnh: Agency ký (upload PDF) trước khi import thợ |
| ✅ Q20 | Identity: thợ mới nhận registration token và phải hoàn thiện hồ sơ; OTP/refresh/khoá đăng nhập lưu trong DB (SC-6..8, 27 bảng); token 15 phút / 30 ngày |
| ✅ Q21 | Customer: C1–C5 theo mặc định của contract (`trust_score` tạm thời, port `IWorkerProfileQuery`) |
| ✅ Q22 | Domain (review BASE-06): lưu offer + `accepted_at` NULL (SC-9), slot giải phóng khi thợ tự huỷ, `fault_party` FREELANCER/AGENCY/CUSTOMER, entity `AdminAccount`, helper `Vnd`, `BUSY` = đang ở hiện trường, trạng thái đơn theo các ca |
| ⏳ Q04b | VietQR / tiền thật — HOÃN |
| ⏳ Q05b | Nhà cung cấp eKYC thật — HOÃN (chặn BE-M4-03) |
| ⏳ Q06b | Nhà cung cấp SMS thật — HOÃN |
| ⏳ Q07b | FCM push — HOÃN (cần trước khi chạy pilot thật) |

Đã chốt (có bằng chứng): **D1** CSDL = SQL Server (kiểu `DATETIME2`/`NVARCHAR`/`BIGINT` trong `Backend/GiupViec_Physical_DB_MVP5.drawio`). **D2** Mobile = Flutter trong `Mobile/` cho Customer + Worker (người điều phối chọn 2026-10-04; PRD §1.2 ghi Mobile Web Viewport 390×844 — giữ khung 390×844 làm chuẩn thiết kế). **D3** `Frontend/` chỉ làm Admin + Partner Portal desktop 1440×900.
Q2 trong bản nháp đã được giải quyết: `FAVORITE_WORKER` có trong Physical drawio (xem M1).

## 9. Definition of Done của mọi ticket
1. `sh harness/verify.sh L3` (hoặc mức ticket yêu cầu) PASS — dán dòng cuối + đường dẫn `.harness/evidence/*.log` vào PR.
2. Test của module có tên cụ thể được trích trong PR (Golden Rule: mock/stub, không DB thật ở unit test).
3. Ô checklist của task trong `.spec/plan/Mx-*.md` đổi `[x]` + `— evidence: <log|PR#>` trong cùng PR.
4. Không chạm ngoài `allowed`; không thêm package/endpoint/biến môi trường không có trong ticket/contract.

## 10. Tài khoản Admin dev tự tạo khi chạy backend (BASE-10, M1 làm; mọi agent tuân theo)
- **Khi nào:** mỗi lần backend khởi động ở môi trường **Development**, sau khi migration đã áp dụng, một startup task (`AdminSeeder`, đăng ký qua cơ chế module của BASE-02 — **không sửa `Program.cs`**) kiểm tra bảng `ADMIN`. Database mới/trống → tự tạo 1 admin. Đã có admin cùng email → **không làm gì** (idempotent, không đổi mật khẩu, không tạo trùng; `email` là UNIQUE).
- **Dữ liệu:** cấu hình `Seed:Admin:Email`, `Seed:Admin:Password`, `Seed:Admin:FullName`. Giá trị dev mặc định đặt trong `Backend/appsettings.Development.json`: `admin@dev.local` / `Admin@123456` / `Dev Admin` — **tài khoản bỏ đi chỉ dùng local**, ghi đè bằng `dotnet user-secrets` hoặc biến môi trường `Seed__Admin__Password`. Mật khẩu băm bằng `IPasswordHasher` rồi lưu `ADMIN.password_hash`; `admin_role` = vai trò cao nhất (M1 chốt tên ở BASE-06), `is_active = 1`.
- **Chỉ Development:** môi trường khác (Staging/Production) **không bao giờ** chạy seeder, kể cả khi có cấu hình `Seed:*`. Có test chứng minh. Log chỉ ghi email, **không log mật khẩu**.
- **Đăng nhập:** Admin vào bằng email + mật khẩu (`BE-M1-03`, theo đề xuất Q16).
- **Bằng chứng khi xong BASE-10:** (1) DB trống → chạy backend → đúng 1 dòng `ADMIN`; (2) chạy lần 2 → vẫn 1 dòng; (3) `ASPNETCORE_ENVIRONMENT=Production` → 0 dòng. Dán log test vào PR.
- **Không** commit mật khẩu thật. Production có admin riêng, tạo ngoài repo.

# M6 — Đánh giá, Khiếu nại, Payout, Admin (+ nền Web)

> **Bắt buộc đọc trước khi làm bất kỳ task nào: [`../decisions.md`](../decisions.md) (tiếng Anh, giá trị & quy tắc đã chốt). Task ghi `decisions: Q##` → đọc mục đó. Mâu thuẫn → decisions.md thắng; thiếu → hỏi leader, không đoán.**
> Module backend: **Ratings**, **Disputes**, **Payouts**, **Admin**. Thực thể: TWO_WAY_RATING, DISPUTE_TICKET, ADMIN, PAYOUT_BATCH, PAYOUT_ITEM.
> Tổng quan/gate/port/event: [00-overview.md](00-overview.md) · Spec: [../spec.md](../spec.md) §1.3, §4.3, §4.4, BR-05 (phần Admin), BR-09.
> Bạn **implement** `IWorkerReputation`. Bạn **dùng** `ISlaPenaltyService` (M5), `IRefundService` (M2), `IFileStorage`. Bạn **phát** `CustomerAbsentApproved`, `DisputeResolved`, `RatingSubmitted`, `PayoutBatchClosed`; **nghe** `JobCompleted`, `CustomerAbsentReported`.
> **Nền Web (React) do bạn dựng — chạy ngay Wave 0, không phụ thuộc backend.** Cả nhóm Web chờ WEB-BASE-01..03.

**Allowed mặc định:** `Backend/Application/Features/{Ratings,Disputes,Payouts,Admin}/**`, `Backend/WebAPI/Controllers/{Ratings,Disputes,Payouts,Admin}/**`, `Backend/Infrastructure/Modules/{Ratings,Disputes,Payouts,Admin}/**`, `Backend/Tests/{Ratings,Disputes,Payouts,Admin}/**`, `Backend/Domain/Entities/*.{Ratings,Disputes,Payouts,Admin}.cs`, `Mobile/lib/features/{ratings,disputes,payouts}/**`, `Mobile/test/features/{ratings,disputes,payouts}/**`, `Frontend/src/features/{ratings,disputes,payouts,admin}/**`, `.spec/plan/M6-*.md`, `.spec/contracts/{ratings,disputes,payouts,admin}.md`.
Ticket `WEB-BASE-*`: `Frontend/**`, `.spec/plan/M6-*.md` (đụng `Frontend/AGENTS.md` thì liệt kê nguyên văn).

## Wave 0 — Nền Web (làm ngay, song song với BASE của M1) — Frontend/ chỉ Admin + Partner Portal desktop
- [x] **WEB-BASE-01** Router + layout theo role: Admin & Partner (desktop 1440×900); **tự nạp route** từ `src/features/*/routes.tsx` (`import.meta.glob`) để không ai sửa `src/app/**`; tạo sẵn thư mục feature (agencies, skills, workers, dispatch, ratings, disputes, payouts, admin). Cập nhật `Frontend/ARCHITECTURE.md`. *done:* thêm feature giả không sửa file chung vẫn hiện route; `npm run lint` + `tsc -b` PASS (log). — evidence: `.harness/evidence/20261006-163330-L3.log` (L3 PASSED, run with DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1); route auto-load proven by a dummy feature in the build bundle (see PR)
- [ ] **WEB-BASE-02** Đăng nhập + lưu token + role guard (theo contract Identity, mock cho tới G2).
- [x] **WEB-BASE-04** Bảng dữ liệu dùng chung (nhiều cột, lọc, phân trang, xuất file) + **trình xem chuỗi ảnh Before/After** (so sánh) dùng cho Dispute/eKYC/Agency. — evidence: `.harness/evidence/20261006-162440-L3.log` (L3 PASSED, run with DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1); `npm test` 12/12 pass (Frontend/tests/components.test.ts); table states and viewer checked on the dev server (see PR)
- [x] **WEB-BASE-03** API client theo envelope `ApiResponse<T>` + kiểu sinh từ OpenAPI (sau G4; trước đó mock theo contract) — dùng `src/services/api.ts`, đường dẫn tương đối `/api/...`. — evidence: `.harness/evidence/20261006-162718-L3.log` (L3 PASSED, run with DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1); `npm test` 11/11 pass (Frontend/tests/client.test.ts)
- [x] **BE-M6-00** Contract Ratings + Disputes + Payouts + Admin → `.spec/contracts/*.md`. — evidence: `.harness/evidence/20261006-162921-L3.log` (L3 PASSED, run with DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1, see PR); files `.spec/contracts/{ratings,disputes,payouts,admin}.md`

## Wave 1 — Backend (sau G0, Fake; EF sau G1)
- [ ] **BE-M6-01** Đánh giá 2 chiều (BR-09): mở khi `COMPLETED`, đóng sau **48 h** (*decisions:* Q14; tiêu chí cố định trong decisions; UNIQUE(assignment_id, rater_role) SC-5), khách→thợ 1–5 sao + tiêu chí, thợ→khách; lưu vĩnh viễn, không sửa/đánh trùng; `IWorkerReputation` cho MatchingScore · *needs:* BASE-07 · nghe `JobCompleted`.
- [ ] **BE-M6-02** Khiếu nại (§4.3): tạo Dispute trong **24 h** kèm ảnh bằng chứng (khách hoặc thợ); hàng đợi Admin có hạn xử lý **24–48 h**; phán quyết: lỗi Freelancer → trừ thù lao ca / khoá tài khoản; lỗi thợ Agency → trừ điểm SLA + khấu trừ ký quỹ qua `ISlaPenaltyService` · *needs:* BASE-11, BE-M5-07 (Fake trước).
- [ ] **BE-M6-03** Duyệt khách vắng mặt (BR-05): Admin xem lịch sử cuộc gọi + GPS → duyệt huỷ ca: thợ nhận **40 %** giá trị ca, thợ về IDLE, khách bị tính phí (phát `CustomerAbsentApproved`) · *needs:* BE-M3-06 · *decisions:* Q10 (hệ thống từ chối duyệt nếu thiếu GPS/≥2 cuộc/≥15 phút/`customer_absent_at`; tính khách đúng 40 %, hoàn 60 %).
- [ ] **BE-M6-04** Kỳ Payout tháng (§4.4): tổng hợp `JOB_ASSIGNMENT` **0 JOIN** (`agency_id IS NULL` → từng Freelancer sau trừ 20 %; `IS NOT NULL` → gộp theo Agency), `PayoutBatch`/`PayoutItem`, idempotent chạy lại, Admin xác nhận giải ngân đóng kỳ (`PayoutBatchClosed`) · *needs:* G1, BE-M4-08. — *đã làm, chờ review:* ticket #136, `api/admin/payout-batches` (dựng/dựng lại kỳ, danh sách, chi tiết, xác nhận giải ngân) + `IPayoutPenaltySource`; chưa tick (xem PR của #136)
- [ ] **BE-M6-05** Xuất file chuyển khoản ngân hàng hàng loạt (Freelancer; Agency tổng hợp) · *needs:* BE-M6-04 · *decisions:* Q18 (`.xlsx`; Agency nhận thêm file chi tiết từng ca).
- [ ] **BE-M6-06** Tài khoản Admin + chỉ số vận hành cho dashboard (số đơn, ca, tranh chấp tồn) · *needs:* G2.
- [ ] **BE-M6-07** Thu nhập thợ theo tháng (freelancer 80 %) · *needs:* BE-M4-08. — *đã làm, chờ review:* ticket #140 (xếp chồng trên #136), `GET api/workers/me/earnings` và `GET api/workers/me/payouts`, dùng chung `PayoutCalculator`; chưa tick (xem PR của #140)
- [ ] **BE-M6-09** `IAuditLog` thật: ghi `ADMIN_AUDIT_LOG` append-only (không có API sửa/xoá), endpoint đọc lịch sử cho Admin; Admin duyệt/thu hồi `is_super_freelancer` (tiêu chí & tự thu hồi <4.70 theo decisions Q12) · *needs:* BASE-07 · *decisions:* G-5, Q12. — *một phần:* `IAuditLog` thật xong (ticket #70, evidence: `.harness/evidence/20261006-163138-L3.log`); endpoint đọc audit log và Super-Freelancer còn mở, chưa tick
- [ ] **BE-M6-08** Test: cửa sổ đánh giá, 1 đánh giá/chiều, hạn khiếu nại 24 h, 40 % vắng mặt, payout đúng 80 %/tổng hợp & chạy lại không trùng · *needs:* BE-M6-01..07.

## Wave 3 — Web (Admin console desktop) & Mobile · *needs:* WEB-BASE-01..04, G4
- [ ] **WEB-M6-01** Dispute console: đối soát chuỗi ảnh Before/After + biên bản nghiệm thu, phán quyết.
- [ ] **WEB-M6-02** Duyệt khách vắng mặt (cuộc gọi + GPS).
- [ ] **WEB-M6-03** Payout batch: xem/duyệt/xuất file/giải ngân.
- [ ] **WEB-M6-04** Dashboard vận hành.
- [ ] **MOB-M6-01** Khách đánh giá thợ · **MOB-M6-02** Thợ đánh giá khách.
- [ ] **MOB-M6-03** Tạo khiếu nại kèm ảnh (khách + thợ), trong 24 h.
- [ ] **MOB-M6-04** Thợ xem thu nhập thực nhận.

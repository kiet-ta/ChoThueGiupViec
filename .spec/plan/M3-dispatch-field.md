# M3 — Điều phối & Hiện trường (+ nền Mobile)

> **Bắt buộc đọc trước khi làm bất kỳ task nào: [`../decisions.md`](../decisions.md) (tiếng Anh, giá trị & quy tắc đã chốt). Task ghi `decisions: Q##` → đọc mục đó. Mâu thuẫn → decisions.md thắng; thiếu → hỏi leader, không đoán.**
> Module backend: **Dispatch**. Thực thể: INCIDENT_LOG, CHECK_IN_LOG, JOB_ASSIGNMENT (vòng đời gán/offer/check-in).
> Tổng quan/gate/port/event: [00-overview.md](00-overview.md) · Spec: [../spec.md](../spec.md) §2.1, §2.5, BR-02/03/04/05/10.
> Bạn **dùng** `IWorkerAvailabilityQuery` (M4), `IAgencyCapacityService` + `ISlaPenaltyService` (M5), `IWorkerReputation` (M6), `IRefundService` (M2), `INotificationService`. Bạn **phát** `JobAssigned`, `AssignmentFailed`, `WorkerCheckedIn`, `CustomerAbsentReported`, `IncidentReported`; **nghe** `OrderPaid`, `ExtensionDeclined`.
> **Nền Mobile (Flutter) do bạn dựng — chạy ngay Wave 0, không phụ thuộc backend.** Cả nhóm Mobile chờ MOB-BASE-01..03.

**Allowed mặc định:** `Backend/Application/Features/Dispatch/**`, `Backend/WebAPI/Controllers/Dispatch/**`, `Backend/Infrastructure/Modules/Dispatch/**`, `Backend/Tests/Dispatch/**`, `Backend/Domain/Entities/*.Dispatch.cs`, `Mobile/lib/features/dispatch/**`, `Mobile/test/features/dispatch/**`, `Frontend/src/features/dispatch/**`, `.spec/plan/M3-*.md`, `.spec/contracts/dispatch.md`.
Ticket `MOB-BASE-*`: `Mobile/**`, `.spec/plan/M3-*.md`; riêng `MOB-BASE-05` là ticket `shared` đụng harness (liệt kê nguyên văn `harness/**`, `.husky/**`, `.github/**`, `AGENTS.md`/`Mobile/AGENTS.md`).

## Wave 0 — Nền Mobile (làm ngay, song song với BASE của M1)
- [x] **MOB-BASE-01** Scaffold Flutter trong `Mobile/` (Customer + Worker, vào theo role), `Mobile/ARCHITECTURE.md` + `Mobile/AGENTS.md`; chốt state management / router / DI và ghi tên gói vào ticket (không tự thêm gói ngoài ticket). Khung thiết kế 390×844. *done:* `flutter analyze` + `flutter test` PASS (log). — evidence: `.harness/evidence/20261006-014913-L3.log` (L3 PASSED; `flutter analyze` 0 issues, `flutter test` 7 tests passed; NordicColors, NordicTypography, NordicTheme, NordicButton, NordicCard, TrustBadge, EyebrowBadge, HomeScreen, ToAmApp, Mobile/ARCHITECTURE.md, Mobile/AGENTS.md)
- [ ] **MOB-BASE-02** Khung feature: tạo sẵn thư mục + file route/registry **cho mọi feature của cả 6 người** (identity, customers, booking, payments, dispatch, workers, agencies, ratings, disputes, payouts) để không ai phải sửa `Mobile/lib/app/**` nữa.
- [ ] **MOB-BASE-03** API client + auth: bọc envelope `ApiResponse<T>{success,message,data}`, lưu token an toàn, interceptor refresh, sinh client từ OpenAPI khi G4 (trước đó dùng mock theo contract). Wrapper camera / vị trí / quyền (cho M4, M3).
- [ ] **MOB-BASE-04** UI kit dùng chung: nút/form/ô nhập, đồng hồ đếm ngược, bản đồ nhỏ, trạng thái lỗi/tải.
- [ ] **MOB-BASE-05** (`shared`) Thêm Mobile vào harness: `verify.sh/run.ps1` (L1 `flutter analyze`/format, L2 build, L3 test), Husky `Mobile/.husky`, CI, `protected-paths` · *allowed:* nguyên văn `harness/**`, `.husky/**`, `.github/**`, `Mobile/.husky/**` · chạy một mình, cần người điều phối duyệt.
- [ ] **BE-M3-00** Contract Dispatch → `.spec/contracts/dispatch.md` (offer, nhận/từ chối, check-in, báo vắng mặt, báo sự cố, trạng thái điều phối).

## Wave 1 — Backend (sau G0, Fake; EF sau G1)
- [ ] **BE-M3-01** `MatchingScore` (khoảng cách, rating, lịch sử ca thành công) hàm thuần, trọng số qua options · *needs:* G0.
- [ ] **BE-M3-02** (loại thợ rating_avg < 4.00 sau ≥10 ca, *decisions:* Q14) Quét bán kính bậc thang **5 → 7 → 10 km** (BR-03); Economy **chỉ Freelancer, tuyệt đối không Agency** (§2.1) · *needs:* BE-M3-01, `IWorkerAvailabilityQuery` · *done:* test "Agency không nhận Economy".
- [ ] **BE-M3-03** Engine offer: mỗi thợ **đúng 30 s** rồi chuyển người kế (BR-03), nhận → `JobAssignment ASSIGNED` + khoá Booking Slot, an toàn khi 2 thợ nhận cùng lúc; nghe `OrderPaid` · *needs:* BE-M3-02, `IClock` · *decisions:* Q07 (SignalR + polling dự phòng) · *done:* test timeout bằng clock giả.
- [ ] **BE-M3-04** Quá 10 km không có thợ → huỷ đơn + hoàn 100 % (phát `AssignmentFailed`) · *needs:* BE-M3-03.
- [ ] **BE-M3-05** Premium auto-assign thợ Agency theo Shift Roster + Skill qua `IAgencyCapacityService` · *needs:* BE-M3-03.
- [ ] **BE-M3-06** Khách vắng mặt (BR-05) phía thợ: chờ ≥15 phút + ≥2 cuộc gọi hệ thống → "Khách vắng mặt", phát `CustomerAbsentReported` (Admin duyệt ở M6) · *needs:* BE-M3-10 · *decisions:* Q10, Q17 (đếm `call_attempts`; chỉ báo sau ≥15 phút và ≥2 cuộc; SĐT khách chỉ lộ trong khung giờ ca).
- [ ] **BE-M3-07** Failover theo mốc (Pha 2, §2.5): báo >2 h → cửa sổ 30 phút để Agency tự đổi thợ; ≤2 h hoặc hết 30 phút → "Cứu hộ Khẩn cấp" thu hồi đơn, điều sang Agency khác / Super-Freelancer (*decisions:* Q12), phạt qua `ISlaPenaltyService` · *needs:* BE-M3-05.
- [ ] **BE-M3-08** Nhà >80 m² (BR-02): tách **2 JobAssignment song song** (ảnh + checklist độc lập); thiếu thợ → 1 thợ làm 2 ca liên tiếp sau khi khách xác nhận · *needs:* BE-M3-03, BE-M2-01.
- [ ] **BE-M3-09** Sự cố bất khả kháng (BR-10): báo kèm ảnh + GPS → `IncidentLog`; Auto Re-dispatch **5 phút**; có thợ mới → cập nhật giờ đến cho khách; không có/khách từ chối → huỷ + hoàn 100 %, thợ gặp nạn **miễn phạt** · *needs:* BE-M3-03.
- [ ] **BE-M3-10** Check-in hiện trường (BR-04): GPS lệch ≤100 m so với địa chỉ đơn, ghi `CheckInLog`; lệch → khách xác nhận trên giao diện hoặc thợ chụp biển số nhà · *needs:* G1.
- [ ] **BE-M3-11** Test: không double-assign, timeout 30 s, thang bán kính, Agency bị loại khỏi Economy, miễn phạt sự cố · *needs:* BE-M3-01..10.

## Wave 3 — Mobile (Worker) & Web (Admin)
- [ ] **MOB-M3-01** Màn nhận cuốc: khu vực, thời lượng, **số tiền thực nhận sau hoa hồng**, đếm ngược 30 s, nhận/bỏ qua (§4.2 bước 1) · *needs:* MOB-BASE-01..04, G4.
- [ ] **MOB-M3-02** "Đã đến nơi" (GPS) + xác nhận thay thế khi lệch.
- [ ] **MOB-M3-03** Nút "Khách vắng mặt" (sau 15 phút) + log cuộc gọi.
- [ ] **MOB-M3-04** Báo sự cố (ảnh + GPS) và nhận cập nhật khi đổi thợ.
- [ ] **WEB-M3-01** Admin: giám sát điều phối & danh sách sự cố (chỉ xem) · *needs:* nền Web của M6 (WEB-BASE-01..03).

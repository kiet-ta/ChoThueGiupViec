# M4 — Thợ & Chất lượng ca làm

> **Bắt buộc đọc trước khi làm bất kỳ task nào: [`../decisions.md`](../decisions.md) (tiếng Anh, giá trị & quy tắc đã chốt). Task ghi `decisions: Q##` → đọc mục đó. Mâu thuẫn → decisions.md thắng; thiếu → hỏi leader, không đoán.**
> Module backend: **Workers**. Thực thể: WORKER (STI), JOB_PHOTO, BOOKING_SLOT (Block Slots freelancer), JOB_ASSIGNMENT (vòng đời thi công/nghiệm thu/hoàn tất).
> Tổng quan/gate/port/event: [00-overview.md](00-overview.md) · Spec: [../spec.md](../spec.md) §2.2, §2.6, §2.8, §4.2, BR-06/07/08.
> Bạn **implement** `IWorkerAvailabilityQuery`, `IEkycProvider`, `IImageQualityService`. Bạn **phát** `JobCompleted`, `ExtensionDeclined`; **nghe** `ExtensionPaid`, `CustomerAbsentApproved`.
> Đây là module nặng nhất: nếu trễ, nhờ M2 (ít việc backend hơn) nhận BE-M4-06 hoặc BE-M4-09 qua ticket riêng.

**Allowed mặc định:** `Backend/Application/Features/Workers/**`, `Backend/WebAPI/Controllers/Workers/**`, `Backend/Infrastructure/Modules/Workers/**`, `Backend/Tests/Workers/**`, `Backend/Domain/Entities/*.Workers.cs`, `Mobile/lib/features/workers/**`, `Mobile/test/features/workers/**`, `Frontend/src/features/workers/**`, `.spec/plan/M4-*.md`, `.spec/contracts/workers.md`.

## Wave 0 — làm ngay
- [x] **BE-M4-00** Contract Workers → `.spec/contracts/workers.md` (đăng ký thợ, eKYC, Block Slots, check-in ảnh, upload ảnh, nghiệm thu, hoàn tất, đáp ứng Làm lần 2) — evidence: #172
- [x] **BE-M4-05a** *(làm sớm được vì là hàm thuần)* Thuật toán Variance of Laplacian trên ảnh: hàm thuần + ảnh mẫu rõ/mờ làm test — evidence: #174

## Wave 1 — Backend (sau G0, Fake; EF sau G1)
- [x] **BE-M4-01** Hồ sơ Worker (STI): đăng ký Freelancer, `worker_type`, `agency_id` null, trạng thái (IDLE…), khoá/mở — evidence: #176
- [x] **BE-M4-02** eKYC Freelancer (§2.8): nhận CCCD 2 mặt + selfie → `IEkycProvider` (Fake, mặc định 92.00; *decisions:* Q05) → **Confidence ≥ 85 % → IDLE ngay**, thấp hơn/cờ gian lận → hàng đợi thủ công — evidence: #178
- [ ] **BE-M4-03** ~~Nhà cung cấp eKYC thật~~ — **HOÃN (Q05b), KHÔNG làm** cho tới khi leader chọn nhà cung cấp.
- [x] **BE-M4-04** Hậu kiểm mẫu 10–20 % cho Admin (5 ca đầu của thợ mới: hậu kiểm 100 %; sau đó mẫu ngẫu nhiên 20 % theo `BusinessRules`; duyệt/thu hồi) · *decisions:* Q05 · *needs:* BE-M4-02 — evidence: .harness/evidence/20261008-200330-L3.log
- [ ] **BE-M4-05** Block Slots (§2.6): Ca Sáng 08:00–12:00, Chiều 13:00–17:00, Tối 17:30–20:30; bật/tắt theo tuần; **`UNIQUE(worker_id, slot_date, shift_code)`** + bắt lỗi trùng → thao tác idempotent (double-click/race) · *needs:* BASE-07 · *done:* test 2 request đồng thời không tạo 2 ca.
- [ ] **BE-M4-06** Upload ảnh Before/After (BR-06): 3–5 góc, **After khớp đúng góc Before**, server kiểm VoL qua `IImageQualityService`, từ chối ảnh dưới ngưỡng ngay · *needs:* BASE-11, BE-M4-05a · *decisions:* Q03 (ngưỡng 100.0, thu về rộng 640 px, 3–5 ảnh đạt/giai đoạn).
- [ ] **BE-M4-07** `IWorkerAvailabilityQuery`: thợ Freelancer rảnh theo slot + vị trí + rating, dùng chỉ mục (không quét chuỗi/UNION) · *needs:* BE-M4-05, G1.
- [ ] **BE-M4-08** Nghiệm thu (BR-07): thợ gửi After → khách "Xác nhận nghiệm thu" hoặc yêu cầu dọn lại 15–30 phút (kèm ảnh sửa lỗi); xác nhận → `COMPLETED`, tính `payout_amount` (Freelancer nhận **80 %**, Agency theo hoa hồng gói, *decisions:* Q11; làm tròn VND theo G-2), phát `JobCompleted` · *needs:* BE-M4-06.
- [ ] **BE-M4-09** Làm lần 2 phía thợ (BR-08): nghe `ExtensionPaid`; thợ đồng ý → mở ca nối ngay; từ chối → đóng ca + phát `ExtensionDeclined` · *needs:* BE-M4-08, BE-M2-08.
- [ ] **BE-M4-10** Test: ngưỡng 85 %, UNIQUE Block Slot, ảnh mờ bị chặn, After sai góc bị chặn, payout 80 % · *needs:* BE-M4-01..09.

## Wave 3 — Mobile (Worker + khách nghiệm thu) & Web (Admin) · *needs:* MOB-BASE-01..04 (M3) + G4
- [ ] **MOB-M4-01** eKYC: chụp CCCD 2 mặt + selfie.
- [ ] **MOB-M4-02** Lịch rảnh: lưới tuần bật/tắt Sáng/Chiều/Tối.
- [ ] **MOB-M4-03** Màn thi công: checklist, bấm giờ ca (≤4 h).
- [ ] **MOB-M4-04** Chụp ảnh Before/After: **kiểm VoL ngay trên máy** để chụp lại tại chỗ, khung hướng dẫn góc tương ứng; lấy ngưỡng từ API (Q03).
- [ ] **MOB-M4-05** Khách: xem ảnh, "Xác nhận nghiệm thu" / yêu cầu dọn lại.
- [ ] **MOB-M4-06** Thợ: nhận/từ chối "Làm lần 2".
- [ ] **WEB-M4-01** Admin: hàng đợi hậu kiểm eKYC (xem CCCD/selfie, duyệt/thu hồi) · *needs:* nền Web của M6.

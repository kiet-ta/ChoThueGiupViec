# Plan for Remaining M5 Tasks (excluding BE-M5-06a)

## Wave 1 — Backend (sau G0, Fake; EF sau G1)
- [ ] **BE-M5-01** Đăng ký Agency (ĐKKD, MST, đại diện pháp luật); chỉ thành "đạt chuẩn" khi đủ ký quỹ tối thiểu (§2.1, *decisions:* Q09: tối thiểu 5.000.000đ) · *needs:* BASE-07.
- [ ] **BE-M5-02** Gói thuê bao: Free (3–5 thợ, hoa hồng 20 %, *decisions:* Q08/Q11) vs Pro (thu phí tháng/quý, mở Shift Roster Dashboard, báo cáo, nhãn Verified Partner, tăng quota, ưu tiên điều phối); mua/gia hạn qua `IPaymentGateway` + webhook → `PartnerSubscription` active, phát `SubscriptionActivated`; **chặn vượt Worker Quota** · *needs:* BE-M5-01 · giá/quota mặc định theo decisions Q08; Admin sửa gói phải ghi `ADMIN_AUDIT_LOG`; hết hạn → ân hạn 7 ngày rồi khoá (không xoá) thợ vượt quota.
- [ ] **BE-M5-03** Import thợ hàng loạt (§2.8): kiểm hợp lệ từng dòng, báo lỗi theo dòng, tạo `Worker` `AGENCY_STAFF` (không qua hàng đợi Admin), ghi cam kết bảo lãnh điện tử · *needs:* BE-M4-01 · *decisions:* Q18 (dry-run rồi commit all-or-nothing), Q19 (phải có `guarantee_signed_at` trước khi import; cột mới SC-1 do M1 thêm ở BASE-06).
- [ ] **BE-M5-04** Danh mục Skill (Admin quản trị) + `WorkerSkill` (chọn từ danh mục, số năm kinh nghiệm, trạng thái thẩm định); truy vấn theo ID có chỉ mục, **không LIKE** (§2.7) · *needs:* BE-M5-03.
- [ ] **BE-M5-05** Shift Roster: Agency phân ca tuần cho thợ (BOOKING_SLOT, tôn trọng `UNIQUE(worker_id, slot_date, shift_code)`), chống xếp trùng · *needs:* BE-M5-03.
- [ ] **BE-M5-06** `IAgencyCapacityService` thật (Pha 1): kiểm công suất đối chiếu Roster + ma trận Skill, **khoá vị trí nguyên tử** khi thanh toán, hết ca → báo kín lịch · *needs:* BE-M5-05, BE-M5-04, BE-M5-06a · *done:* test đua 2 khách / 1 vị trí.
- [ ] **BE-M5-07** Quỹ Ký quỹ: nạp (webhook), `escrow_deposit_balance`, sổ khấu trừ, `ISlaPenaltyService` (trừ điểm SLA + khấu trừ bồi hoàn; mức theo decisions Q09; thứ tự khấu trừ: hoàn khách → chi phí cứu hộ → phí nền tảng), không cho âm số dư (thiếu thì khấu tới 0, ghi phần 부족, đặt `SUSPENDED`); mọi thay đổi số dư = 1 dòng `ESCROW_TRANSACTION` append-only (SC-4); khiếu nại trong 48 giờ, Admin đảo bằng dòng `REVERSAL` · *needs:* BE-M5-01 · *decisions:* Q09.
- [ ] **BE-M5-08** Truy vấn Agency (0 JOIN): đơn công ty `WHERE agency_id`, báo cáo hiệu suất nhân viên, nhãn Verified Partner, cờ ưu tiên điều phối (M3 đọc) · *needs:* G1.
- [ ] **BE-M5-09** Cửa sổ 30 phút (Pha 2): Agency tự đổi thợ nội bộ trong hạn, hết hạn/≤2 h → trả quyền cho M3 · *needs:* BE-M3-07 (phối hợp contract).
- [ ] **BE-M5-10** Test: quota, đua công suất, khấu trừ ký quỹ không âm, import lỗi từng dòng, Pro/Free quyền tính năng · *needs:* BE-M5-01..09.

## Wave 3 — Web (Partner Portal desktop 1440×900) & Mobile · *needs:* nền Web của M6 + G4
- [ ] **WEB-M5-01** Đăng nhập Partner + chọn/mua gói thuê bao, hiển thị quota.
- [ ] **WEB-M5-02** Quản lý thợ + Bulk Import (xem lỗi theo dòng).
- [ ] **WEB-M5-03** Bảng Shift Roster theo tuần (phân thợ vào ca, cảnh báo thiếu Skill).
- [ ] **WEB-M5-04** Ký quỹ: số dư, nạp, lịch sử khấu trừ.
- [ ] **WEB-M5-05** Đơn của công ty + báo cáo hiệu suất.
- [ ] **WEB-M5-06** Cửa sổ 30 phút đổi thợ (đếm ngược).
- [ ] **WEB-M5-07** Admin: quản trị danh mục Skill.
- [ ] **WEB-M5-08** Admin: sửa gói thuê bao (giá, quota, hoa hồng) bắt buộc nhập lý do + xem lịch sử `ADMIN_AUDIT_LOG` (G-5).
- [ ] **MOB-M5-01** Thợ Agency: xem lịch ca được phân (Worker app, dùng chung nền M3).
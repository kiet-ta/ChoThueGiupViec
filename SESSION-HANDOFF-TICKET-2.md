# Handoff — Ticket 2: thay thế/ngừng thợ Agency

## Mục tiêu và trạng thái

Tiếp tục quản lý vòng đời thợ Agency: ngừng thợ cũ, thêm người thay thế và tính quota chính xác. Đây là ticket riêng, tách khỏi mẫu cam kết và import. **Ticket 2 chưa có issue, chưa có branch riêng, chưa sửa code và chưa bắt đầu.**

Không bắt đầu code khi chưa có issue hợp lệ gắn nhãn `ticket` + `ready`, được chuyển sang `in-progress`, và đã chạy `node harness/start-ticket.mjs <issue#>` thành công.

## Trạng thái ticket liên quan

- Ticket [#388 — AGENCY-UAT-01](https://github.com/kiet-ta/ChoThueGiupViec/issues/388) làm mẫu cam kết Agency và luồng in/lưu PDF.
- PR [#389](https://github.com/kiet-ta/ChoThueGiupViec/pull/389) đang mở theo thông tin handoff; **chưa xác minh đã merge**. Không giả định issue đã đóng.
- Commit: `7aa9682 feat(agency): add printable guarantee template (#388)`.
- Branch hiện tại lúc handoff: `ticket/388-agency-uat-01-mau-cam-ket-agency-va`.
- Working tree sạch tại thời điểm handoff theo trạng thái repo được cung cấp.
- Các file ticket 1 đã thay đổi không được liệt kê trong bối cảnh handoff; cần tra PR/commit nếu cần biết chính xác.

## Quyết định đã chốt

1. Cam kết ở cấp Agency, áp dụng cho thợ hiện tại và các đợt import sau.
2. Thêm, thay hoặc ngừng thợ không yêu cầu nộp PDF cam kết mới, trừ khi Agency thay đổi nội dung cam kết.
3. PDF không liệt kê từng thợ; không OCR, không đối chiếu từng dòng Excel với PDF.
4. Backend chỉ ghi nhận đã nhận PDF. Không xác minh chữ ký, danh tính người ký, nội dung PDF, hoặc đối chiếu thủ công tên pháp lý/MST với hồ sơ Agency.
5. Ticket 1 dùng các trường nhập thủ công trên mẫu; không tự thêm endpoint để đọc thông tin hồ sơ Agency hoặc đối chiếu dữ liệu.
6. PDF seed chỉ là placeholder sandbox, không phải tài liệu đã ký/hợp lệ pháp lý.
7. Ticket 2 chỉ bao gồm lifecycle thay/ngừng thợ và quota; không mở rộng lại mẫu cam kết hay xác minh chữ ký.

## Khảo sát kỹ thuật đã biết — cần xác minh lại trong repo

- Partner Portal hiện chưa có luồng rõ ràng để sửa, ngừng hoặc thay hồ sơ thợ.
- Luồng import hiện tạo thêm thợ.
- Quota được khảo sát trước đó tại `SubscriptionService.cs:136` là số thợ hiện có cộng số lượng mới trong file import. Đây là tham chiếu cũ, phải kiểm tra lại vị trí, logic và mọi caller liên quan trước khi thiết kế.
- Không mô phỏng thay thế bằng cách import thêm thợ mà không xử lý lifecycle/quota.

## Thay đổi, migration và cấu hình

- Ticket 2: chưa có thay đổi.
- Ticket 1: hoàn tất phần triển khai mẫu cam kết theo commit nêu trên; handoff không có danh sách file hoặc thông tin migration/config cụ thể. Tra PR/commit nếu cần.
- Không chạy seed hoặc ghi dữ liệu lên database khi chưa xác nhận.

## Kiểm thử và bằng chứng ticket 1

- Verify L3: `.harness/evidence/20261020-193944-L3.log` — `VERIFY L3 PASSED`.
- Frontend tests: 217 passed.
- Lint/build thành công; có warning hiện hữu được ghi trong PR.
- Scope check: `scope-check OK (ticket #388, 8 file(s))`.
- Chưa chạy full authenticated UAT upload/import vì Backend container không được khởi động; chưa upload PDF thật. Không báo cáo UAT này là đã hoàn tất.
- Không có kết quả formal security/privacy review trong thông tin handoff; trạng thái là **chưa xác minh**.

## Quy trình tiếp tục

1. Kiểm tra lại trạng thái branch, PR #389 và working tree; không suy diễn PR đã merge.
2. Khảo sát chỉ đọc: đọc `Backend/ARCHITECTURE.md`, `Frontend/ARCHITECTURE.md`, spec/decision/checklist liên quan; truy vết đầy đủ import, hồ sơ thợ, subscription/quota và tất cả caller.
3. Dựa trên spec/checklist hiện có, soạn issue mới theo template Ticket, giới hạn vào lifecycle thợ và quota. Ghi `Allowed paths`, Acceptance và blockers chính xác; nếu cần phạm vi ngoài allowed, mở Scope Exception và dừng, không lách scope.
4. Tạo issue và gắn nhãn `ticket` + `ready`. Chỉ khi issue hợp lệ và được chuyển sang `in-progress`, chạy đúng `node harness/start-ticket.mjs <issue#>`; không tự tạo/chuyển branch.
5. Sau triển khai, chạy `sh harness/verify.sh L3`, lưu evidence, nêu rõ mọi UAT chưa chạy. Tối đa ba lần tự sửa khi verify thất bại; sau đó báo blocker theo quy định.

## Ràng buộc an toàn

Không seed hoặc ghi DB chưa được xác nhận. Không bypass gate, không sửa ngoài `allowed`, không commit trực tiếp lên `main`, không force-push, không giả định PR/issue đã merge/đóng.
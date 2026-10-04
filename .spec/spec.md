# Spec — PRD-MVP5-FINAL (Nền tảng kết nối dịch vụ giúp việc theo yêu cầu)

> Status: DRAFT   (agents chỉ implement khi người phụ trách đổi thành APPROVED)
> Nguồn: `PRD.docx` (v5.0, 27/09/2026) — chuyển máy móc sang Markdown, KHÔNG diễn giải lại. Khi lệch, `PRD.docx` thắng.
> ERD vật lý: `Backend/GiupViec_Physical_DB_MVP5.drawio`. Kế hoạch & checklist: `.spec/plan/00-overview.md`.

## TÀI LIỆU ĐẶC TẢ YÊU CẦU NGHIỆP VỤ & HỆ THỐNG — PHIÊN BẢN CHÍNH THỨC MVP5
NỀN TẢNG KẾT NỐI DỊCH VỤ GIÚP VIỆC THEO YÊU CẦU

On-Demand Home Cleaning Services Platform — MVP 5 Approved Specification


| Mã Tài Liệu | PRD-MVP5-FINAL | Trạng Thái | Phê Duyệt Chính Thức (Approved) |
|---|---|---|---|
| Phiên Bản | v5.0 (MVP5 Milestone) | Ngày Ban Hành | 27/09/2026 |
| Phạm Vi Cốt Lõi | Nguồn cung kép (Dual Supply), Single Table Inheritance Worker, Phân tầng Economy vs Premium, B2B SaaS Agency & Flattened Execution Junction Conceptual ERD | Bản Vẽ Đi Kèm | GiupViec_Conceptual_DB_MVP5 (drawio, svg, png, html) |


## 1. Bối Cảnh, Đóng Khung Bài Toán & Đối Tượng Sử Dụng


### 1.1. Khung bài toán mở rộng không gian nhà ở

Hệ thống mở rộng phục vụ toàn bộ người lao động và cư dân sinh sống tại các mô hình nhà ở đa dạng trong khu vực đô thị:

- Căn hộ chung cư: Cấu trúc mặt bằng 1 tầng sàn phẳng, chuẩn hóa theo số lượng phòng ngủ và nhà vệ sinh, điều kiện ánh sáng và thông gió tương đối đồng đều.
- Nhà riêng lẻ (Nhà phố mặt đất): Cấu trúc phân tầng, có cầu thang bộ và nhiều khu vực vệ sinh riêng biệt. Tổng diện tích sàn sử dụng được tính toán tự động qua công thức chuẩn hóa:
S_total = S_sàn × N_tầng

- Phòng trọ / Căn hộ mini: Diện tích nhỏ (≤ 30 m²), mật độ đồ đạc cao, nhu cầu tập trung vào dọn dẹp mặt sàn và sắp xếp đồ dùng thiết yếu.

### 1.2. Ràng buộc triển khai và vận hành bắt buộc

- Quy chuẩn Khung nhìn Chuyên biệt (Stakeholder Viewport Specialization - ADR-0013): Ứng dụng Web (Web-first) tương thích linh hoạt theo vai trò tác nghiệp thực tế trên Stitch: Ứng dụng Thợ (Worker) và Khách hàng (Customer) tối ưu hóa trên Mobile Web Viewport (390 x 844px) hỗ trợ thao tác một tay, quét định vị GPS và chụp ảnh kiểm định. Bảng điều khiển Quản trị (Admin) và Cổng Doanh nghiệp Đối tác (B2B Partner Portal) thiết kế chuyên biệt trên Desktop Web Viewport (1440 x 900px) đáp ứng quản lý bảng dữ liệu đa cột, đối soát chuỗi ảnh và xuất file ngân hàng hàng loạt.
- Cơ chế Thanh toán Tự động qua Webhook: Tích hợp Webhook (IPN) tự động xác thực biến động số dư chuyển khoản MoMo QR / VietQR theo thời gian thực (Real-time). Khách hàng thanh toán 100% theo từng ca làm đơn lẻ (Pay-per-Job); Đối tác B2B thanh toán phí mua gói thuê bao định kỳ (Partner Subscription). Hệ thống xử lý tự động, Admin không cần duyệt tay giao dịch nạp tiền thông thường.
- Quy chuẩn Ca làm việc (Standard Shift Block): Một ca làm việc tiêu chuẩn của 1 Worker có thời lượng tối đa là 4 giờ (T_max = 4h) áp dụng cho diện tích tiêu chuẩn ≤ 80 m². Đối với diện tích lớn hơn (> 80 m²), hệ thống tự động quy đổi thành nhiều buổi công hoặc điều phối nhiều Worker đồng thời.
- Hiện diện bắt buộc của Khách hàng (Mandatory Customer Presence - ADR-0007): Khách hàng (hoặc người đại diện trưởng thành trong nhà) bắt buộc phải có mặt tại nhà trong suốt toàn bộ thời gian diễn ra ca làm việc từ lúc đón thợ đến khi nghiệm thu hoàn tất. Nghiêm cấm bàn giao chìa khóa để thợ làm việc một mình không có người giám sát nhằm triệt tiêu hoàn toàn nguy cơ mất mát tài sản và tranh chấp vu khống trộm cắp.
- Trách nhiệm Dụng cụ & Hóa chất (Worker Tool Kit - ADR-0010): Người làm (Worker) tự trang bị và mang theo 100% dụng cụ đồ nghề và hóa chất tẩy rửa chuyên dụng an toàn (bao gồm cây lau nhà gấp gọn, xô nước gấp gọn, chổi cọ, khăn vi sợi phân màu chống nhiễm khuẩn chéo, hóa chất đa năng/toilet/bếp). Khách hàng chịu gánh nặng trang bị bằng 0 (Zero Customer Equipment Burden), chỉ có trách nhiệm cung cấp nguồn điện và nguồn nước tại chỗ.

### 1.3. Các bên liên quan (Actors)

- Khách hàng (Customer): Người đặt dịch vụ dọn dẹp; xác thực tài khoản bằng số điện thoại và OTP. Thanh toán 100% theo mô hình Pay-per-Job qua mã MoMo QR / Webhook. Quản lý sổ địa chỉ căn nhà (CustomerAddress), lưu danh sách Thợ quen (FavoriteWorker), bắt buộc hiện diện trực tiếp tại nhà để nghiệm thu ca làm tại chỗ và tham gia đánh giá 2 chiều sau mỗi ca.
- Người làm (Worker): Người trực tiếp thực hiện công việc dọn dẹp tại hiện trường. Bao gồm Lao động tự do (Freelancer) và Nhân sự cơ hữu (Agency Staff) thuộc Doanh nghiệp đối tác. Tất cả đăng nhập và thao tác thực thi ca làm trên ứng dụng di động chuẩn hóa.
- Doanh nghiệp đối tác (Partner Agency): Pháp nhân doanh nghiệp cung ứng nhân sự cơ hữu. Đăng ký mua các Gói thuê bao định kỳ (Partner Subscription) theo mô hình Freemium / B2B SaaS để quản lý hạn mức số lượng thợ tối đa (Worker Quota), lên lịch trực ca (Shift Roster) và nhận phân bổ các hợp đồng dịch vụ cao cấp đòi hỏi cam kết SLA bảo chứng.
- Quản trị viên (Admin): Quản trị viên vận hành nền tảng: thẩm định eKYC Worker theo cơ chế hậu kiểm mẫu, theo dõi vận hành, phê duyệt bồi hoàn vắng mặt, giải quyết tranh chấp (SLA 24–48h) và xuất bảng kê quyết toán lương cuối tháng.

## 2. Kiến Trúc Nguồn Cung Nhân Sự & Phân Tầng Dịch Vụ (MVP5 Core)


### 2.1. Phân tầng Phân khúc Nhu cầu (Demand Segmentation)

Nhằm giải quyết triệt để xung đột quyền lợi giữa Lao động tự do và Doanh nghiệp đối tác, nền tảng phân chia dịch vụ thành 2 phễu phân khúc độc lập ngay từ giao diện khách hàng (Client UI):

- Phân khúc Tiêu chuẩn / Tiết kiệm (Economy Pool): Đặc điểm: Giá thành phổ thông, linh hoạt thời gian, phù hợp nhu cầu dọn dẹp căn hộ cơ bản. Cơ chế phân bổ: Dành riêng cho lực lượng Freelancer Worker theo cơ chế cạnh tranh điểm đánh giá (Rating) và bán kính địa lý. Doanh nghiệp đối tác (Agency) tuyệt đối không được tiếp cận luồng đơn này nhằm bảo vệ sinh kế cho lao động tự do.
- Phân khúc Doanh nghiệp / Cao cấp (Premium / Managed Pool): Đặc điểm: Khách hàng doanh nghiệp, căn hộ cao cấp hoặc gia đình đòi hỏi hợp đồng rõ ràng, thợ có lý lịch tư pháp đầy đủ và cam kết Thỏa thuận Mức Dịch vụ (SLA) nghiêm ngặt. Cơ chế phân bổ: Chỉ chuyển về cho các Partner Agency đạt chuẩn đã nộp Quỹ Ký quỹ Trách nhiệm. Đơn bắt buộc đặt trước tối thiểu 2–4 giờ.

### 2.2. Quyết định kiến trúc: Thực thể thống nhất Worker (Single Table Inheritance)

Để giải quyết dứt điểm nỗi lo "chậm query do JOIN nhiều bảng", kiến trúc cơ sở dữ liệu MVP5 chốt quyết định giữ nguyên một thực thể thống nhất duy nhất là Worker theo mô hình Single Table Inheritance:

- Cấu trúc Bảng Worker lõi: Bảng Worker lưu trữ toàn bộ người trực tiếp thi công, phân định bằng trường worker_type ('FREELANCER' | 'AGENCY_STAFF') và agency_id (NULL nếu là Freelancer; có ID nếu là thợ Agency).
- Triệt tiêu Khóa ngoại Đa hình (Polymorphic FK): Bảng điều phối Job_Assignment chỉ cần duy nhất 1 khóa ngoại trực tiếp worker_id trỏ về bảng Worker. Triệt tiêu hoàn toàn rủi ro Khóa ngoại đa hình (Polymorphic Foreign Key) và loại bỏ hoàn toàn các câu lệnh UNION ALL khi quét tìm thợ gần nhất, đạt hiệu năng truy vấn tối đa.

### 2.3. Mô hình Doanh thu Đa tầng B2B SaaS (Freemium / Hybrid) cho Agency

Nền tảng kiếm tiền từ Doanh nghiệp đối tác thông qua mô hình kết hợp:

- Gói cơ bản (Free Tier): Miễn phí niêm yết số lượng thợ giới hạn (3–5 thợ). Nền tảng trích hoa hồng chiết khấu trên mỗi đơn hoàn thành thành công.
- Gói nâng cấp (B2B SaaS Tier): Thu phí thuê bao định kỳ (hàng tháng/hàng quý) để Agency mở khóa các công cụ quản trị nâng cao: Bảng điều khiển phân ca tự động (Shift Roster Dashboard), báo cáo phân tích hiệu suất nhân viên, gắn nhãn Đối tác Uy tín (Verified Partner), tăng quota số lượng thợ và quyền ưu tiên điều phối.

### 2.4. Quỹ Ký quỹ Trách nhiệm (Performance Bond) & Chế tài SLA

Mỗi Partner Agency khi gia nhập hệ thống bắt buộc phải duy trì một số dư ký quỹ đảm bảo thực hiện nghĩa vụ (escrow_deposit_balance). Mọi vi phạm cam kết mức dịch vụ (bỏ ca, thiếu thợ, khách khiếu nại chất lượng) sẽ bị trừ điểm tín nhiệm SLA và khấu trừ tiền phạt trực tiếp từ Quỹ Ký quỹ này để bồi hoàn cho khách hàng hoặc bù đắp chi phí điều động thợ cứu hộ khẩn cấp.


### 2.5. Cơ chế Bảo vệ 2 Pha cho Đơn Premium (Dual-Phase SLA Protection)

- Pha 1 (Phòng ngừa đầu vào - Atomic Capacity Check): Hệ thống kiểm tra công suất thời gian thực đối chiếu Lịch trực ca (Shift Roster) và Ma trận Kỹ năng của Agency. Khách hàng chỉ có thể tạo đơn Premium nếu tại thời điểm đó còn vị trí rảnh đáp ứng đúng nhãn kỹ năng yêu cầu; khóa vị trí ngay tức thì khi thanh toán. Nếu hết ca, ứng dụng báo kín lịch ngay từ đầu để triệt tiêu nguy cơ nhận đơn ảo không có người làm.
- Pha 2 (Cứu trợ theo mốc thời gian - Time-Adaptive Failover): Khi thợ của Agency báo bận đột xuất hoặc không xác nhận ca làm:
- Báo trước giờ làm > 2 giờ: Agency có cửa sổ 30 phút trên Dashboard để tự đổi thợ nội bộ khác.
- Báo sát giờ ≤ 2 giờ HOẶC Agency hết 30 phút không xử lý được: Hệ thống tự động kích hoạt chế độ "Cứu hộ Khẩn cấp", thu hồi đơn và điều phối sang Agency đối tác khác trong khu vực hoặc Lao động Tự do Cấp cao (Super-Freelancer) đạt chuẩn. Agency vi phạm bị phạt trừ điểm SLA và khấu trừ tiền bồi hoàn từ Quỹ Ký quỹ Trách nhiệm.

### 2.6. Cơ chế Quản lý Lịch rảnh Block Slots chuẩn hóa cho Freelancer

Để tránh phân mảnh thời gian và giúp nền tảng xếp lịch chuẩn xác, Freelancer khai báo lịch rảnh theo các khung ca chuẩn hóa (Block Slots): Ca Sáng (08:00 - 12:00), Ca Chiều (13:00 - 17:00), Ca Tối (17:30 - 20:30) đã tính đệm di chuyển 60 phút. Freelancer chỉ cần bật On/Off trạng thái sẵn sàng theo tuần. Khi có đơn khớp khung giờ, việc đối soát lịch chỉ mất 1 truy vấn đơn giản O(1). Đồng thời, để ngăn ngừa triệt để lỗi duplicate ca trực khi thao tác nhiều lần (double-click/race condition), hệ thống thiết lập ràng buộc duy nhất UNIQUE(worker_id, slot_date, shift_code) trên từng ca mở ra.


### 2.7. Chuẩn hóa Danh mục Kỹ năng (Skill Catalog & Worker Skills Matrix)

Thay vì cho phép nhập chuỗi tự do (skill_tags) dẫn đến thiếu đồng nhất và code phải xử lý chuẩn hóa phức tạp, hệ thống thiết lập bảng danh mục kỹ năng chuẩn hóa (Skill) do Admin quản trị và bảng liên kết kỹ năng thợ (Worker Skill). Thợ và Agency chỉ cần tích chọn kỹ năng có sẵn từ danh mục. Việc khớp ca điều phối theo ma trận kỹ năng (Skill Matching) chuyển thành truy vấn trực tiếp theo ID chỉ mục B-Tree đạt tốc độ O(1), loại bỏ hoàn toàn các câu lệnh quét chuỗi LIKE tốn tài nguyên.


### 2.8. Quy trình eKYC Tự động hóa kết hợp Hậu kiểm mẫu

- Đối với Freelancer Worker: Người làm thực hiện quét eKYC tự động trên mobile app (OCR CCCD gắn chip 2 mặt + Face Matching AI đối chiếu khuôn mặt). Nếu độ tin cậy đạt Confidence Score ≥ 85%, hệ thống tự động kích hoạt tài khoản sang trạng thái IDLE ngay lập tức. Admin chỉ thực hiện hậu kiểm ngẫu nhiên (Sample Audit 10–20%) hoặc kiểm tra khi có cờ cảnh báo gian lận.
- Đối với Thợ Doanh nghiệp (Agency Worker): Agency là pháp nhân chịu trách nhiệm pháp lý toàn diện về nhân sự; Agency import danh sách thợ hàng loạt (Bulk Import) và ký cam kết bảo lãnh trách nhiệm điện tử. Thợ được tự động đồng bộ vào hệ thống ca trực của Agency mà không cần qua hàng đợi duyệt thủ công của Admin.

### Bảng Ma trận So sánh Toàn diện Nguồn cung Nhân sự (Freelancer vs Agency)


| Tiêu chí so sánh | Lao động Tự do (Freelancer Worker) | Doanh nghiệp Đối tác (Agency & Agency Worker) |
|---|---|---|
| Bản chất & Pháp lý | Cá nhân lao động tự do ký hợp đồng CTV số hóa | Pháp nhân doanh nghiệp có ĐKKD, MST, đại diện pháp luật |
| Phân khúc phục vụ | Economy Pool (Dọn dẹp cơ bản, giá tiết kiệm) | Premium Pool (Căn hộ cao cấp, cam kết SLA bảo chứng) |
| Cơ chế Lịch trình | Block Slots cố định (Sáng / Chiều / Tối) bật/tắt linh hoạt | Shift Roster & Skill Matrix theo tuần do Agency quản lý |
| Mô hình Thu phí Sàn | Trích hoa hồng 20% trên mỗi đơn hoàn thành (Thợ nhận 80%) | Freemium / B2B SaaS (Gói Free trích hoa hồng; Gói Pro thu phí thuê bao định kỳ) |
| Bảo lãnh & Chế tài | 2-Way Rating; tụt sao hoặc tự ý bỏ ca sẽ bị khóa app | Quỹ Ký quỹ Trách nhiệm (Performance Bond); phạt SLA trừ thẳng tiền ký quỹ |
| Quy trình Định danh (KYC) | eKYC tự động qua app (OCR CCCD + Face AI); Admin hậu kiểm 10-20% | Agency import danh sách thợ hàng loạt kèm cam kết bảo lãnh pháp lý doanh nghiệp |
| Chi trả Thù lao (Payout) | Chuyển khoản trực tiếp vào tài khoản ngân hàng cá nhân cuối tháng | Quyết toán tổng hợp (Bulk Payout) vào tài khoản doanh nghiệp Agency |


## 3. Quy Tắc Nghiệp Vụ Vận Hành Ca Làm (Operational Business Rules)

- BR-01 (Định mức Ca làm Tiêu chuẩn): Áp dụng cho nhà có diện tích tiêu chuẩn ≤ 80 m² (căn hộ 1-2 phòng ngủ, nhà 1 sàn). Thời lượng tối đa 4 giờ (T_max = 4h). Hệ thống điều phối đúng 1 Worker thực hiện trọn vẹn ca làm.
- BR-02 (Quy chuẩn Nhà Diện tích lớn > 80m²): Đối với nhà có diện tích lớn > 80 m² (nhà nhiều tầng hoặc căn hộ diện tích lớn), hệ thống tự động phân tách thành 2 Job Assignment đồng thời: điều phối 2 Worker làm việc song song (ví dụ: mỗi thợ phụ trách một tầng). Mỗi thợ sở hữu bộ ảnh Before/After riêng biệt và checklist công việc độc lập. Nếu không đủ 2 thợ, hệ thống kích hoạt cơ chế fallback: 1 thợ làm 2 ca liên tiếp sau khi khách xác nhận.
- BR-03 (Điều phối Bán kính Động & Timeout 30 giây): Khi đơn hàng được kích hoạt sau thanh toán, thuật toán quét tìm Worker theo bán kính mở rộng bậc thang: Bán kính 5 km (30 giây đầu) ──(hết thợ)──► 7 km ──► 10 km. Đơn được phát ưu tiên theo MatchingScore (khoảng cách, điểm rating, lịch sử ca thành công). Mỗi thợ có đúng 30 giây để chấp nhận trước khi hệ thống chuyển tiếp cho thợ kế tiếp. Nếu quá 10 km không tìm được thợ, hệ thống tự động hủy đơn và hoàn tiền 100% cho khách hàng.
- BR-04 (Xác thực Hiện trường GPS Check-in): Worker khi đến địa điểm nhà khách bấm "Đã đến nơi" trên ứng dụng. Hệ thống tự động đối soát tọa độ GPS của thiết bị với địa chỉ đơn hàng (sai số cho phép ≤ 100 m). Trường hợp GPS lệch (nhà trong hẻm sâu, chung cư cao tầng cản sóng), khách hàng bấm nút xác nhận trực tiếp trên giao diện web hoặc thợ chụp ảnh biển số nhà đối chiếu.
- BR-05 (Giao thức Khách hàng Vắng mặt & Bồi hoàn 40%): Trường hợp thợ đã đến nơi đúng giờ nhưng không liên hệ được khách hàng: Thợ duy trì chờ tối thiểu 15 phút tại hiện trường và thực hiện ít nhất 2 cuộc gọi qua hệ thống. Sau 15 phút, thợ bấm báo "Khách vắng mặt". Admin kiểm tra lịch sử cuộc gọi và tọa độ GPS, phê duyệt hủy ca: Worker nhận khoản phí bồi hoàn 40% giá trị ca làm; trạng thái Worker chuyển về IDLE để tiếp tục nhận đơn mới; khách hàng bị tính phí dịch vụ tương ứng.
- BR-06 (Kiểm định Ảnh Before/After qua Thuật toán VoL): Bắt buộc người làm chụp từ 3–5 góc ảnh hiện trạng khu vực trước khi bắt đầu dọn (Before) và chụp lại đúng các góc tương ứng sau khi hoàn tất (After). Hệ thống tích hợp thuật toán phân tích phương sai toán tử Laplacian (Variance of Laplacian - VoL) để kiểm tra độ rõ nét của ảnh theo thời gian thực (Real-time Blur Detection). Ảnh có chỉ số nét dưới ngưỡng chuẩn (VoL < Threshold) sẽ bị từ chối ngay lập tức và yêu cầu thợ chụp lại tại chỗ.
- BR-07 (Nghiệm thu Mặt-đối-Mặt & Giao thức Dọn lại 15–30 phút): Khi Worker hoàn thành công việc và chụp đủ ảnh After, khách hàng tiến hành kiểm tra trực tiếp mặt-đối-mặt tại nhà (ADR-0007). Trường hợp khách hàng chưa hài lòng với một số chi tiết nhỏ, thợ có trách nhiệm khắc phục tại chỗ trong 15–30 phút (kèm chụp ảnh sửa lỗi). Nếu chất lượng đạt, khách bấm "Xác nhận nghiệm thu" trên ứng dụng để đóng ca làm việc.
- BR-08 (Nối ca Làm lần 2 - Job Order Extension): Trường hợp khối lượng công việc thực tế quá lớn không thể hoàn thành trong 4 giờ, khách hàng có thể bấm nút "Làm lần 2" (EXTEND-01) trên ứng dụng: Hệ thống tạo một bản ghi Job Order Extension gắn kèm và tạo mã MoMo QR mới. Nếu Worker tại chỗ đồng ý tiếp tục làm, ca nối tiếp được mở ngay lập tức; nếu thợ từ chối (do bận lịch ca sau), hệ thống đóng ca và điều phối thợ mới cho ca tiếp theo.
- BR-09 (Cơ chế Đánh giá 2 Chiều - 2-Way Rating): Cơ chế đánh giá 2 chiều chỉ mở trong vòng 24–48 giờ sau khi ca làm kết thúc thành công: Khách hàng đánh giá Worker (1–5 sao kèm tiêu chí chi tiết); Worker đánh giá thái độ hợp tác và điều kiện làm việc của Khách hàng. Đánh giá được lưu trữ vĩnh viễn và tác động trực tiếp lên MatchingScore điều phối.
- BR-10 (Xử lý Sự cố Bất khả kháng Hiện trường - Incident Handling): Trường hợp Worker gặp sự cố bất khả kháng trên đường di chuyển (hỏng xe, ngã xe, mưa bão ngập lụt): Thợ bấm báo sự cố trên app kèm ảnh chụp hiện trường và định vị GPS. Hệ thống kích hoạt chế độ Auto Re-dispatch trong 5 phút tìm thợ thay thế. Nếu tìm thấy thợ mới, cập nhật lại thời gian đến cho khách; nếu không tìm thấy hoặc khách từ chối đổi thợ, đơn bị hủy và hoàn tiền 100%, Worker gặp nạn được miễn hoàn toàn chế tài phạt.

## 4. Đặc Tả Các Luồng Hoạt Động Chính (Mainflows)


### 4.1. Mainflow 1: Khách hàng đặt dịch vụ (Booking Flow)

- Bước 1: Khởi tạo yêu cầu: Khách hàng đăng nhập qua SĐT + OTP, chọn địa chỉ nhà từ sổ địa chỉ (tự động tính diện tích sàn S_total).
- Bước 2: Chọn phân khúc & Khung giờ: Khách chọn phân khúc Economy (Dọn dẹp cơ bản) hoặc Premium (Dịch vụ cao cấp cam kết SLA). Khách chọn khung ca làm (Block Slot) và chọn dịch vụ kèm ghi chú.
- Bước 3: Thanh toán Pay-per-Job: Hệ thống hiển thị mã MoMo QR động. Webhook IPN nhận tín hiệu thanh toán thành công trong < 1 giây, tự động chuyển đơn sang trạng thái PAID và kích hoạt luồng điều phối.
- Bước 4: Điều phối & Khóa Slot: Đơn Economy quét bán kính phát cho Freelancer; đơn Premium auto-assign cho thợ Agency theo Shift Roster. Khi thợ nhận, trạng thái đơn chuyển sang ASSIGNED và khóa Booking Slot.

### 4.2. Mainflow 2: Người làm tiếp nhận và thực thi ca làm (Execution Flow)

- Bước 1: Nhận tín hiệu đơn: Thợ nhận thông báo cuốc, xem khu vực, thời lượng, số tiền thực nhận sau hoa hồng sàn; bấm nhận trong 30 giây.
- Bước 2: Check-in hiện trường: Thợ di chuyển đến nhà khách, bấm "Đã đến nơi"; hệ thống kiểm tra GPS sai số ≤ 100 m so với địa chỉ đơn hàng.
- Bước 3: Chụp ảnh Before & Thi công: Thợ chụp 3–5 ảnh hiện trạng các phòng (Before) đạt chuẩn kiểm định nét VoL; sau đó tiến hành dọn dẹp theo checklist.
- Bước 4: Chụp ảnh After & Nghiệm thu: Hết 4 giờ làm, thợ chụp bộ ảnh After đối chứng góc chụp, mời khách hàng nghiệm thu trực tiếp mặt-đối-mặt tại chỗ. Khách xác nhận đạt, ca làm hoàn tất chuyển sang trạng thái COMPLETED.

### 4.3. Mainflow 3: Xử lý Khiếu nại, Tranh chấp & Bồi thường (Dispute Flow)

- Bước 1: Khởi tạo khiếu nại: Trong vòng 24 giờ sau ca làm, nếu có tranh chấp (hỏng đồ, mất vệ sinh, thái độ), khách hàng hoặc thợ tạo Dispute Ticket kèm hình ảnh bằng chứng.
- Bước 2: Admin đối soát bằng chứng: Admin mở giao diện xử lý khiếu nại, đối soát chuỗi ảnh Before/After và biên bản nghiệm thu mặt-đối-mặt (SLA xử lý 24–48 giờ).
- Bước 3: Phán quyết & Bồi thường: Nếu lỗi thuộc về Freelancer: trừ tiền thù lao ca làm hoặc khóa tài khoản cảnh cáo. Nếu lỗi thuộc về Agency Worker: trừ điểm SLA và khấu trừ tiền bồi thường từ Quỹ Ký quỹ Trách nhiệm của Agency.

### 4.4. Mainflow 4: Quản trị B2B SaaS Agency & Quyết toán Payout cuối kỳ

- Bước 1: Thuê bao B2B SaaS: Đầu kỳ, Agency thanh toán phí thuê bao định kỳ qua Webhook để mở khóa hạn mức nhân sự và Dashboard phân ca.
- Bước 2: Phân ca trực Shift Roster: Agency khai báo danh sách thợ và phân công ca trực tuần (Shift Roster) theo các khung giờ cố định.
- Bước 3: Quyết toán Payout cuối tháng: Vào ngày cuối tháng, hệ thống tổng hợp bảng kê Payout: (1) Xuất danh sách chuyển khoản ngân hàng trực tiếp cho từng Freelancer Worker (sau khi trừ 20% phí sàn); (2) Xuất bảng kê tổng hợp quyết toán chuyển khoản cho Doanh nghiệp đối tác (Partner Agency). Admin xác nhận giải ngân để đóng kỳ kế toán.

## 5. Thiết Kế Cơ Sở Dữ Liệu Khái Niệm Tối Ưu Truy Vấn (Conceptual Database MVP5)


### 5.1. Triết lý thiết kế: Thực thể JOB là trung tâm & Flattened Execution Junction

Trong một nền tảng dịch vụ theo yêu cầu, toàn bộ vòng đời vận hành đều xoay quanh thực thể JOB. Nhằm triệt tiêu tình trạng chậm query do thực hiện phép JOIN qua 5–6 bảng liên tiếp, bảng Job_Assignment đóng vai trò là Nút giao thực thi phẳng (Flattened Execution Junction) nhúng sẵn các khóa ngoại trực tiếp:

- order_id (FK -> Job_Order): Khóa ngoại trỏ về đơn hàng tổng thể của khách.
- customer_id (FK -> Customer, Denormalized): Nhúng trực tiếp ID khách hàng trên Job_Assignment, giúp truy vấn lịch sử đơn hàng của khách chỉ quét đúng 1 bảng (0 JOIN).
- worker_id (FK -> Worker): Khóa ngoại duy nhất trỏ về bảng Worker (áp dụng mô hình Single Table Inheritance, không phân mảnh bảng).
- agency_id (FK -> Partner_Agency, Denormalized Nullable): NULL nếu là Freelancer; chứa ID Agency nếu là thợ doanh nghiệp. Phục vụ xuất báo cáo doanh nghiệp và chạy quyết toán Payout 0-JOIN.
- slot_id (FK -> Booking_Slot): Khóa khung giờ làm việc của thợ.
- service_tier (ENUM): Phân định luồng ECONOMY vs PREMIUM.
- payout_amount: Số tiền thù lao thực nhận đã tính sẵn sau hoa hồng sàn.

### 5.2. Bảng phân tích so sánh hiệu năng truy vấn


| Tác vụ / Màn hình nghiệp vụ | Mô hình chuẩn hóa 3NF truyền thống | Mô hình Flattened Junction (MVP5) | Cải thiện hiệu năng |
|---|---|---|---|
| Freelancer xem ca làm việc của mình | JOIN 3 bảng: Job_Assignment + Job_Order + Customer | SELECT * FROM Job_Assignment WHERE worker_id = :id | 0 JOIN (Index Scan siêu tốc) |
| Agency xem đơn của công ty mình | JOIN 4 bảng: Job_Assignment + Worker + Partner_Agency + Job_Order | SELECT * FROM Job_Assignment WHERE agency_id = :id | 0 JOIN (Index Scan siêu tốc) |
| Admin chạy quyết toán Payout cuối kỳ | JOIN 5 bảng để bóc tách thợ Freelance và Agency | SELECT * FROM Job_Assignment WHERE agency_id IS [NOT] NULL | 0 JOIN (Indexed Aggregation) |
| Khách hàng xem tiến độ các ca làm | JOIN 3 bảng: Job_Order + Job_Assignment + Worker | SELECT * FROM Job_Assignment WHERE order_id = :id | 0 JOIN (Index Scan siêu tốc) |


### 5.3. Từ điển Dữ liệu Khái niệm (21 Thực thể Cốt lõi MVP5)


| STT | Tên Thực thể Khái niệm | Vai trò & Ý nghĩa Nghiệp vụ trong MVP5 |
|---|---|---|
| 1 | Customer | Hồ sơ người dùng đặt dịch vụ, số điện thoại, điểm tin cậy. |
| 2 | Customer Address | Sổ địa chỉ nhà ở tích hợp diện tích sàn S_total, số tầng, loại hình nhà và tọa độ GPS. |
| 3 | Job Order | Yêu cầu đặt dịch vụ của khách hàng, phân định phân khúc Economy vs Premium. |
| 4 | Job Order Extension | Bản ghi gia hạn nối ca làm việc ("Làm lần 2") kèm giao dịch thanh toán riêng. |
| 5 | Payment Transaction | Giao dịch thanh toán tự động qua Webhook MoMo QR (Pay-per-Job). |
| 6 | Dispute Ticket | Hồ sơ khiếu nại tranh chấp hiện trường giữa Khách hàng và Worker (SLA 24-48h). |
| 7 | Incident Log | Biên bản ghi nhận sự cố bất khả kháng trên đường di chuyển (hỏng xe, ngập lụt). |
| 8 | Job Photo | Tập hợp ảnh chụp Before/After được kiểm định độ nét qua thuật toán Laplacian VoL. |
| 9 | Job Assignment | Nút giao thực thi phẳng (Flattened Junction) kết nối ca làm, Worker, Customer và Agency. |
| 10 | Check In Log | Bản ghi lịch sử check-in hiện trường, tọa độ GPS (≤ 100m) và mốc thời gian. |
| 11 | Booking Slot | Khung giờ ca rảnh chuẩn hóa (Block Slots của Freelancer và Shift Roster của Agency). Ràng buộc duy nhất UNIQUE(worker_id, slot_date, shift_code) để chống duplicate ca trực. |
| 12 | Worker | Thực thể thống nhất (Single Table Inheritance) với cờ worker_type và agency_id; chuẩn hóa kỹ năng thông qua bảng danh mục Skill và bảng liên kết Worker Skill (thay thế nhập skill_tags tự do). |
| 13 | 2-Way Rating | Đánh giá chất lượng 2 chiều giữa Khách hàng và Người làm sau ca dọn. |
| 14 | Subscription Package | Định nghĩa các gói thuê bao đối tác B2B (Gói cơ bản Free vs Gói nâng cấp Pro SaaS). |
| 15 | Partner Subscription | Hợp đồng thuê bao định kỳ đang hiệu lực của Doanh nghiệp đối tác. |
| 16 | Partner Agency | Pháp nhân doanh nghiệp đối tác, số dư Ký quỹ Trách nhiệm (escrow_deposit_balance) và SLA. |
| 17 | Admin | Tài khoản quản trị viên vận hành nền tảng, hậu kiểm eKYC và giải ngân Payout. |
| 18 | Payout Batch | Kỳ chốt sổ quyết toán thù lao định kỳ hàng tháng do Admin phê duyệt. |
| 19 | Payout Item | Chi tiết khoản tiền chi trả cho Freelancer Worker (trực tiếp) hoặc Partner Agency (tổng hợp). |
| 20 | Skill | Bảng danh mục kỹ năng chuẩn hóa toàn hệ thống (Skill Catalog) do Admin quản trị, phục vụ thợ đăng ký và thuật toán ghép ca. |
| 21 | Worker Skill | Bảng liên kết M:N giữa Worker và Skill, lưu trữ hồ sơ kỹ năng thực tế của từng thợ, số năm kinh nghiệm và trạng thái thẩm định. |


### 5.4. Bản vẽ Conceptual ERD MVP5 chuẩn hóa

Mô hình Conceptual ERD MVP5 đã được kết xuất và đồng bộ thành 4 định dạng kỹ thuật tiêu chuẩn trong thư mục dự án:

- GiupViec_Conceptual_DB_MVP5.drawio: Tệp nguồn XML tiêu chuẩn dành cho ứng dụng app.diagrams.net, tuân thủ 100% quy chuẩn 6 style đen-trắng nguyên bản của dự án.
- GiupViec_Conceptual_DB_MVP5.svg: Bản vẽ đồ họa vector sắc nét (chỉ gồm 2 mã màu #000000 và #ffffff), chuẩn quan hệ Crow's Foot RDBMS.
- GiupViec_Conceptual_DB_MVP5.png: Ảnh render độ phân giải cao phục vụ chèn trực tiếp vào báo cáo kỹ thuật và thuyết trình đồ án.
- GiupViec_Conceptual_DB_MVP5.html: Trình xem tương tác trực tiếp trên trình duyệt Web (hỗ trợ zoom, pan, export tiện lợi).

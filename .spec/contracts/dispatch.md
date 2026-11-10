# Contract: Dispatch (module `Dispatch`, owner M3)

> Status: **DRAFT — submitted by M3 (ticket BE-M3-00, issue #90); awaiting leader approval**.
> Sources: `.spec/spec.md` §2.1, §2.5, BR-02, BR-03, BR-04, BR-05, BR-10 · `.spec/decisions.md` Q07 (SignalR), Q10 (Customer absent), Q12 (Super-Freelancer), Q14 (Rating filter), Q17 (Call masking), Q22 (JobAssignment state machine & SC-9 offer model) · `Backend/GiupViec_Physical_DB_MVP5.drawio` tables `JOB_ASSIGNMENT`, `CHECK_IN_LOG`, `INCIDENT_LOG`.
> Implements tickets: BE-M3-01 to BE-M3-11, MOB-M3-01 to MOB-M3-04, WEB-M3-01.
> Conventions (envelope `ApiResponse<T>`, camelCase, UTC timestamps, status codes, roles, policies) are defined in `identity.md` §1 and apply here unchanged.

---

## 1. Conventions & Role Policies

- Base path: `/api/v1/dispatch`.
- Realtime Hub: `/hubs/dispatch` (SignalR WebSocket + Long Polling fallback, Q07).
- Roles & Policies:
  - `WorkerOnly`: thợ thao tác nhận offer, check-in, gọi điện, báo vắng mặt, báo sự cố.
  - `CustomerOnly`: khách theo dõi tiến độ, xác nhận check-in ngoại lệ, phản hồi đổi thợ cứu hộ.
  - `AdminOnly`: duyệt báo khách vắng mặt, giám sát sự cố và điều phối cứu hộ khẩn cấp.
- State Machine của `JOB_ASSIGNMENT` (Q22):
  `OFFERED` -> `ASSIGNED` -> `CHECKED_IN` -> `IN_PROGRESS` -> `AWAITING_ACCEPTANCE` -> `COMPLETED`.
  Nhánh rẽ hủy / sự cố:
  - `CANCELLED` (khách hủy trước giờ / hết thợ >10km)
  - `CANCELLED_BY_WORKER` (thợ hủy ca sau khi nhận)
  - `ABSENT` (khách vắng mặt sau khi Admin duyệt)
  - `INCIDENT` (gặp sự cố bất khả kháng trên đường / hiện trường)
  - `REASSIGNED` (chuyển giao ca cho thợ khác sau sự cố / failover)

---

## 2. Data Shapes

### 2.1 `JobOfferDto`
Mỗi offer gửi tới thợ có hiệu lực trong đúng 30 giây (BR-03, Q07, SC-9):
```json
{
  "assignmentId": 1001,
  "orderId": 501,
  "serviceTier": "ECONOMY",
  "bookingDate": "2026-10-15",
  "shiftTime": "08:00-12:00",
  "district": "Quận Cầu Giấy",
  "approximateDistanceKm": 3.4,
  "estimatedDurationHours": 4.0,
  "grossAmount": 260000.0,
  "commissionRate": 0.20,
  "netEarnings": 208000.0,
  "offeredAt": "2026-10-15T01:00:00Z",
  "expiresAt": "2026-10-15T01:00:30Z",
  "remainingSeconds": 28,
  "isConsecutiveShift": false
}
```
`isConsecutiveShift = true` là ca thứ hai liên tiếp của fallback BR-02 (thợ đã giữ ca còn lại của cùng đơn): cửa sổ trả lời là `Dispatch.FallbackConfirmSeconds` (120 giây), không phải 30 giây; `expiresAt` / `remainingSeconds` đã tính theo cửa sổ đó.

### 2.2 `CheckInResultDto`
Kết quả kiểm tra GPS hiện trường khi thợ bấm "Đã đến nơi" (BR-04):
```json
{
  "checkInId": 2001,
  "assignmentId": 1001,
  "checkedInAt": "2026-10-15T07:55:00Z",
  "distanceMeters": 45.2,
  "isGpsVerified": true,
  "requiresAlternativeVerification": false,
  "verificationMethod": "GPS",
  "platePhotoUrl": null,
  "isCustomerConfirmed": false
}
```

### 2.3 `CustomerAbsentReportDto`
Báo cáo khách vắng mặt sau >= 15 phút và >= 2 cuộc gọi (BR-05, Q10):
```json
{
  "assignmentId": 1001,
  "reportedAt": "2026-10-15T08:16:00Z",
  "checkedInAt": "2026-10-15T07:55:00Z",
  "elapsedMinutes": 21,
  "callAttempts": 3,
  "reviewStatus": "PENDING_APPROVAL",
  "workerFeeRate": 0.40,
  "absenceFeeAmount": 104000.0,
  "customerRefundRate": 0.60,
  "customerRefundAmount": 156000.0
}
```

### 2.4 `IncidentLogDto`
Báo cáo sự cố bất khả kháng trên đường hoặc tại nơi làm (BR-10):
```json
{
  "incidentId": 3001,
  "assignmentId": 1001,
  "workerId": 42,
  "incidentType": "ACCIDENT",
  "description": "Xe bị thủng lốp trên đường đến ca làm",
  "photoEvidenceUrl": "https://storage.local/incidents/inc-3001.jpg",
  "latitude": 21.0285,
  "longitude": 105.8542,
  "reportedAt": "2026-10-15T07:30:00Z",
  "isPenaltyExempt": true,
  "reDispatchStatus": "SEARCHING",
  "reDispatchDeadline": "2026-10-15T07:35:00Z",
  "substituteWorkerId": null
}
```

---

## 3. Endpoints

### 3.1 Dành cho Thợ (`WorkerOnly`)

> Served by `WorkerDispatchController` (DISPATCH-WIRE-02, #311), except `incidents` (DISPATCH-WIRE-03). Ca không tồn tại hoặc không phải của thợ gọi -> `404`; trạng thái không cho phép -> `409` với `data.code` (decisions Q26 D6): `OFFER_EXPIRED`, `OFFER_NOT_OPEN`, `ORDER_NOT_DISPATCHING`, `ASSIGNMENT_STATE_INVALID`, `ABSENT_CONDITIONS_NOT_MET`, `ABSENT_ALREADY_REPORTED`. `decline` trả `"status": "DECLINED"` (nhãn của phản hồi); dòng `JOB_ASSIGNMENT` chuyển `CANCELLED` và chỗ đó được mời cho thợ kế tiếp.

#### `GET /api/v1/dispatch/offers/current`
Lấy job offer đang chờ thợ phản hồi (nếu có). Trả về `data = null` nếu thợ không có offer nào đang active.

- Response 200: `ApiResponse<JobOfferDto | null>`

#### `POST /api/v1/dispatch/offers/{assignmentId}/accept`
Thợ nhận ca làm việc trong cửa sổ 30s.

- Response 200:
```json
{
  "assignmentId": 1001,
  "status": "ASSIGNED",
  "acceptedAt": "2026-10-15T01:00:12Z",
  "bookingSlotLocked": true
}
```
- Mã lỗi:
  - `404`: Không tìm thấy offer hoặc không thuộc về thợ.
  - `409`: Offer đã hết hạn 30s hoặc đơn đã được nhận bởi thợ khác (double-assign guard).

#### `POST /api/v1/dispatch/offers/{assignmentId}/decline`
Thợ chủ động từ chối offer (chuyển ngay sang thợ kế tiếp trong pool matching mà không cần chờ hết 30s).

- Request body:
```json
{
  "reason": "Bận việc đột xuất | Quá xa | null"
}
```
- Response 200:
```json
{
  "assignmentId": 1001,
  "status": "DECLINED"
}
```

#### `POST /api/v1/dispatch/assignments/{assignmentId}/check-in`
Check-in GPS tại hiện trường (BR-04). Lệch <= 100m tính là hợp lệ (`isGpsVerified: true`).

- Request body:
```json
{
  "latitude": 21.0285,
  "longitude": 105.8542
}
```
- Response 200: `ApiResponse<CheckInResultDto>`
- Mã lỗi:
  - `400`: Tọa độ GPS không hợp lệ.
  - `404`: Assignment không tồn tại hoặc không thuộc thợ.
  - `409`: Trạng thái ca làm không phải `ASSIGNED`.

#### `POST /api/v1/dispatch/assignments/{assignmentId}/check-in/plate-photo`
Thợ chụp ảnh biển số nhà / số căn hộ khi GPS bị lệch > 100m.

- Request body: `multipart/form-data` hoặc JSON:
```json
{
  "platePhotoUrl": "string"
}
```
- Response 200: `ApiResponse<CheckInResultDto>`

#### `POST /api/v1/dispatch/assignments/{assignmentId}/calls/log`
Ghi nhận 1 cuộc gọi liên hệ khách hàng qua hệ thống (Q17). Hệ thống tăng `CHECK_IN_LOG.call_attempts`.

- Response 200:
```json
{
  "assignmentId": 1001,
  "callAttempts": 2,
  "loggedAt": "2026-10-15T08:05:00Z"
}
```

#### `POST /api/v1/dispatch/assignments/{assignmentId}/absent`
Báo "Khách vắng mặt" (BR-05, Q10).
Điều kiện bắt buộc:
1. Đã check-in thành công.
2. Đã trôi qua >= 15 phút kể từ `checked_in_at`.
3. Số cuộc gọi `call_attempts >= 2`.

- Response 200: `ApiResponse<CustomerAbsentReportDto>`
- Mã lỗi:
  - `409`: Chưa đủ 15 phút hoặc chưa thực hiện đủ 2 cuộc gọi hệ thống (`CHECK_IN_LOG.call_attempts < 2`).

#### `POST /api/v1/dispatch/assignments/{assignmentId}/incidents`
Báo sự cố bất khả kháng trên đường hoặc hiện trường (BR-10).

- Request body:
```json
{
  "incidentType": "ACCIDENT | HEALTH | SEVERE_WEATHER | OTHER",
  "description": "string",
  "photoEvidenceUrl": "string",
  "latitude": 21.0285,
  "longitude": 105.8542
}
```
- Response 200: `ApiResponse<IncidentLogDto>`
- Quy tắc: Thợ được đánh dấu miễn phạt (`isPenaltyExempt = true`). Hệ thống tự động kích hoạt cửa sổ Auto Re-dispatch 5 phút.

---

### 3.2 Dành cho Khách Hàng (`CustomerOnly`)

#### `GET /api/v1/dispatch/orders/{orderId}/status`
Khách hàng theo dõi realtime trạng thái điều phối đơn đặt.

*(Lưu ý triển khai: Trên Web/Mobile thực tế, tiến độ đơn và danh sách thợ được theo dõi tập trung qua endpoint `GET /api/v1/booking/orders/{orderId}/progress` và SignalR topic `job.tracking`, xem `booking.md` §3.6 và quyết định B9).*

- Response 200:
```json
{
  "orderId": 501,
  "serviceTier": "ECONOMY",
  "requiredWorkers": 1,
  "dispatchStatus": "SEARCHING | ASSIGNED | CHECKED_IN | FAILED",
  "searchRadiusKm": 5.0,
  "assignments": [
    {
      "assignmentId": 1001,
      "workerId": 42,
      "workerName": "Nguyễn Văn Thợ",
      "workerPhone": "0987654321",
      "workerRating": 4.95,
      "status": "ASSIGNED",
      "checkedInAt": null
    }
  ]
}
```
*Lưu ý bảo mật (Q17): `workerPhone` chỉ hiển thị khi trạng thái là `ASSIGNED` hoặc `CHECKED_IN` trong ngày ca làm việc.*

#### `GET /api/v1/dispatch/assignments/{assignmentId}/check-in`
Khách (policy `CustomerOnly`) xem ca làm có đang chờ mình xác nhận thợ đã đến không (CUST-ARRIVAL-01, #657). `awaitingCustomerConfirmation = true` khi ca còn `ASSIGNED` và lượt check-in GPS gần nhất bị lệch > 100m mà chưa ai xác nhận thay. Ca không phải của khách -> `404`.

- Response 200:
```json
{
  "assignmentId": 1001,
  "status": "ASSIGNED",
  "awaitingCustomerConfirmation": true,
  "distanceMeters": 6675.33,
  "attemptedAt": "2026-10-15T07:55:00Z"
}
```

#### `POST /api/v1/dispatch/assignments/{assignmentId}/check-in/confirm`
Khách xác nhận thợ đã có mặt tại nhà khi GPS của thợ bị lệch > 100m (BR-04). Chỉ khi `awaitingCustomerConfirmation = true`, ngược lại `409` `data.code = ARRIVAL_NOT_PENDING`; ca không phải của khách -> `404`. `CHECKIN_LOG.fallback_method` lưu `CUSTOMER_CONFIRM` (cột `varchar(20)`).

- Response 200:
```json
{
  "assignmentId": 1001,
  "isCustomerConfirmed": true,
  "status": "CHECKED_IN"
}
```

#### Phản hồi thay thế / đổi ca (Đã hợp nhất vào Fallback API)
*(Ghi chú: Bản dự thảo trước đây tách thành 2 route `POST .../incidents/accept-substitute` và `POST .../incidents/decline-substitute`. Trong mã nguồn thực tế, thao tác phản hồi của khách được chuẩn hóa vào endpoint duy nhất bên dưới: `POST /api/v1/dispatch/orders/{orderId}/fallback/respond` với body `{ "accept": true | false }`, cài đặt tại `CustomerFallbackController.cs`).*

#### `GET /api/v1/dispatch/orders/{orderId}/fallback`
Đơn 2 thợ chỉ tìm được 1 thợ (BR-02 fallback, #310). Đơn không hủy mà chờ khách chọn: cho cùng một thợ làm 2 ca liên tiếp hay hủy + hoàn 100 %. Đơn của người khác -> `404`.

- Response 200 (`state` = `AWAITING_CUSTOMER` | `AWAITING_WORKER` | `NOT_WAITING`):
```json
{
  "orderId": 501,
  "state": "AWAITING_CUSTOMER",
  "workerId": 101,
  "secondShiftCode": "SHIFT_AFTERNOON",
  "secondShiftDate": "2026-10-15",
  "deadlineUtc": "2026-10-15T01:00:00Z"
}
```
`AWAITING_CUSTOMER`: `deadlineUtc` là giờ bắt đầu ca; khách im lặng đến lúc đó = từ chối. `AWAITING_WORKER`: khách đã đồng ý, `deadlineUtc` là hạn thợ xác nhận (120 giây). `NOT_WAITING`: các trường còn lại là `null`.

#### `POST /api/v1/dispatch/orders/{orderId}/fallback/respond`
Khách trả lời câu hỏi "Chỉ có 1 thợ: thợ làm 2 ca liên tiếp?".

- Request: `{ "accept": true }` (`accept` bắt buộc, thiếu -> `400`).
- Response 200 khi đồng ý: thợ được hỏi xác nhận ca thứ hai (xem `isConsecutiveShift` ở 2.1; thợ dùng lại `offers/current`, `accept`, `decline` ở 3.1):
```json
{ "orderId": 501, "outcome": "WORKER_ASKED", "workerDeadlineUtc": "2026-10-14T03:02:00Z", "message": null }
```
- Response 200 khi từ chối, đã quá giờ ca, hoặc thợ không còn ca liền sau: đơn bị hủy và hoàn 100 % (`AssignmentFailed`):
```json
{ "orderId": 501, "outcome": "CANCELLED", "workerDeadlineUtc": null, "message": "Khách từ chối 1 thợ làm 2 ca liên tiếp (BR-02)." }
```
- `404` đơn không tồn tại / không phải của khách. `409` `data.code` = `FALLBACK_NOT_WAITING` (đơn không ở trạng thái chờ) hoặc `FALLBACK_ALREADY_ANSWERED` (khách đã đồng ý, đang chờ thợ).
- Thợ từ chối hoặc không xác nhận trong 120 giây -> đơn bị hủy và hoàn 100 % như trên (khách thấy `state` = `NOT_WAITING` và đơn `CANCELLED`).


#### `GET /api/v1/dispatch/assignments/{assignmentId}/route`
Khách xem lộ trình từ thợ tới nhà (ROUTE-WEB-01, #765, quyết định D29). Lộ trình do Valhalla tính (costing `motor_scooter`, dữ liệu OpenStreetMap).

- Chỉ trả khi ca còn `ASSIGNED` (thợ đang trên đường). Điểm đi = vị trí thợ đã lưu qua `PUT /workers/me/location`, **làm tròn 3 chữ số thập phân** (khoảng 100 m) và đường đi cũng bắt đầu từ điểm đã làm tròn; điểm đến = toạ độ địa chỉ của đơn.
- Response 200 `data`:
```json
{
  "assignmentId": 500,
  "distanceMeters": 3200.0,
  "durationSeconds": 780.4,
  "polyline": "_p~iF~ps|U",
  "destination": { "latitude": 10.8031, "longitude": 106.7147 },
  "workerPosition": { "latitude": 10.777, "longitude": 106.701 }
}
```
`polyline` là polyline mã hoá 6 chữ số thập phân (định dạng Valhalla).
- `404`: ca không tồn tại hoặc không phải của khách. `409` `data.code` = `ROUTE_NOT_AVAILABLE` (ca không còn `ASSIGNED`) hoặc `WORKER_LOCATION_UNKNOWN` (thợ chưa có vị trí). `502`: Valhalla không trả lời; trang đơn vẫn dùng được.
- Khi `Valhalla:BaseUrl` để trống (Development, CI), backend trả đường thẳng giả `FakeRoutingService` (25 km/h).
---

### 3.3 Dành cho Quản Trị Viên (`AdminOnly`)

#### Quản trị duyệt khách vắng mặt (Thuộc module Admin M6)
> **Chuyển giao:** Các endpoint quản trị duyệt khách vắng mặt thuộc quyền sở hữu của module **Admin (M6)**, quy định chi tiết tại hợp đồng [`admin.md` §2.2](admin.md) và cài đặt thực tế tại [`AdminAbsenceReportsController.cs`](file:///home/tak/openclaw/projects/ChoThueGiupViec/Backend/WebAPI/Controllers/Admin/AdminAbsenceReportsController.cs):
> - `GET /api/v1/admin/absence-reports?status=&page=&pageSize=`
> - `GET /api/v1/admin/absence-reports/{assignmentId}`
> - `POST /api/v1/admin/absence-reports/{assignmentId}/approve`
> - `POST /api/v1/admin/absence-reports/{assignmentId}/reject`
> *(Lưu ý: Không dùng tiền tố `/api/v1/dispatch/admin/absent-reviews` trong mã nguồn thực tế).*

#### `GET /api/v1/dispatch/admin/incidents`
Bảng theo dõi các sự cố hiện trường và tiến độ Re-dispatch.

- Response 200: `ApiResponse<List<IncidentLogDto>>`
- **Implemented by ADMIN-UAT-05** (`AdminDispatchController`, `EfAdminIncidentQuery`): newest first, at most 200 rows, from `INCIDENT_LOG` joined with `JOB_ASSIGNMENT` (worker id). The stored `redispatch_status` is mapped to the contract values: `PENDING_REDISPATCH` -> `SEARCHING`, `REPLACED` -> `REASSIGNED`, `DECLINED_BY_CUSTOMER` and `TIMEOUT_CANCELLED` -> `FAILED`. `reDispatchDeadline` = `reportedAt` + `IncidentService.RedispatchWindowMinutes` (5). `substituteWorkerId` is always null: the table does not store it. **Known gap:** no HTTP endpoint lets a worker report an incident yet (`IncidentService` exists, `WorkerDispatchController` has no incident action), so the list is empty until one exists. The Admin console shows exactly this list; it no longer falls back to invented sample incidents.

#### Cứu hộ khẩn cấp (Emergency Rescue — Event-driven, không có HTTP endpoint)
> **Lưu ý:** Không có HTTP endpoint thủ công (`POST /api/v1/dispatch/admin/emergency-rescue`). Cơ chế "Cứu hộ khẩn cấp" (Pha 2 §2.5, Q09/Q12) chạy hoàn toàn tự động kích hoạt bằng event `AgencySwapWindowExpired` từ Agencies (M5) khi:
> - Cửa sổ 30 phút tự đổi thợ hết hạn (sweep 30s), hoặc
> - Agency báo bận trước giờ ca <= 2 h
> Handler `AgencySwapWindowExpiredHandler` (Dispatch M3) gọi `MilestoneFailoverService.TriggerEmergencyRescueAsync`:
> 1. Thử tìm Agency khác qua `IAgencyCapacityService` → tạo assignment mới cho Agency đó, lock slot, SLA `SHORTAGE` penalty
> 2. Thử Super-Freelancer đủ điều kiện (Rating ≥ 4.80, CompletedJobs ≥ 50) có slot `AVAILABLE` cùng date/shift → gán việc, SLA `SHORTAGE` penalty
> 3. Không tìm thấy ai → phát `AssignmentFailed`, hủy đơn, hoàn 100 % `TotalAmount` cho khách (`AssignmentFailedHandler`)
> Triển khai trong `RESCUE-WIRE-01` (#443).

---

## 4. SignalR Realtime Hub (`/hubs/dispatch`)

### Client -> Server:
- `JoinWorkerQueue()`: Thợ kết nối để lắng nghe các offer 30s.
- `JoinOrderTracking(long orderId)`: Khách kết nối để nhận cập nhật trạng thái đơn realtime.

### Server -> Worker:
- `ReceiveOffer(JobOfferDto offer)`: Gửi offer mới cho thợ (kèm đếm ngược 30s).
- `OfferRevoked(long assignmentId)`: Hết hạn 30s hoặc đơn đã được thợ khác nhận.
- `AssignmentCancelled(long assignmentId)`: Đơn bị khách hủy.

### Server -> Customer:
- `DispatchProgress(double radiusKm, int candidatesFound)`: Cập nhật bậc thang tìm thợ (5km -> 7km -> 10km).
- `WorkerMatched(WorkerSummaryDto worker)`: Đã tìm thấy thợ.
- `WorkerCheckedIn(CheckInResultDto result)`: Thợ đã có mặt tại nhà.
- `IncidentAlert(IncidentSummaryDto alert)`: Thông báo thợ gặp sự cố + thời gian dự kiến đổi thợ.
- `DispatchTimeoutCancelled(double refundAmount)`: Quá 10km không có thợ -> tự động hủy đơn và hoàn tiền 100%.

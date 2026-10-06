# Contract: Dispatch (module `Dispatch`, owner M3)

> Status: **DRAFT — submitted by M3 (ticket BE-M3-00, issue #90); awaiting leader approval**.
> Sources: `.spec/spec.md` §2.1, §2.5, BR-02, BR-03, BR-04, BR-05, BR-10 · `.spec/decisions.md` Q07 (SignalR), Q10 (Customer absent), Q12 (Super-Freelancer), Q14 (Rating filter), Q17 (Call masking), Q22 (JobAssignment state machine & SC-9 offer model) · `Backend/GiupViec_Physical_DB_MVP5.drawio` tables `JOB_ASSIGNMENT`, `CHECK_IN_LOG`, `INCIDENT_LOG`.
> Implements tickets: BE-M3-01 to BE-M3-11, MOB-M3-01 to MOB-M3-04, WEB-M3-01.
> Conventions (envelope `ApiResponse<T>`, camelCase, UTC timestamps, status codes, roles, policies) are defined in `identity.md` §1 and apply here unchanged.

---

## 1. Conventions & Role Policies

- Base path: `/api/dispatch`.
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
  "remainingSeconds": 28
}
```

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

#### `GET /api/dispatch/offers/current`
Lấy job offer đang chờ thợ phản hồi (nếu có). Trả về `data = null` nếu thợ không có offer nào đang active.

- Response 200: `ApiResponse<JobOfferDto | null>`

#### `POST /api/dispatch/offers/{assignmentId}/accept`
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

#### `POST /api/dispatch/offers/{assignmentId}/decline`
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

#### `POST /api/dispatch/assignments/{assignmentId}/check-in`
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

#### `POST /api/dispatch/assignments/{assignmentId}/check-in/plate-photo`
Thợ chụp ảnh biển số nhà / số căn hộ khi GPS bị lệch > 100m.

- Request body: `multipart/form-data` hoặc JSON:
```json
{
  "platePhotoUrl": "string"
}
```
- Response 200: `ApiResponse<CheckInResultDto>`

#### `POST /api/dispatch/assignments/{assignmentId}/calls/log`
Ghi nhận 1 cuộc gọi liên hệ khách hàng qua hệ thống (Q17). Hệ thống tăng `CHECK_IN_LOG.call_attempts`.

- Response 200:
```json
{
  "assignmentId": 1001,
  "callAttempts": 2,
  "loggedAt": "2026-10-15T08:05:00Z"
}
```

#### `POST /api/dispatch/assignments/{assignmentId}/absent`
Báo "Khách vắng mặt" (BR-05, Q10).
Điều kiện bắt buộc:
1. Đã check-in thành công.
2. Đã trôi qua >= 15 phút kể từ `checked_in_at`.
3. Số cuộc gọi `call_attempts >= 2`.

- Response 200: `ApiResponse<CustomerAbsentReportDto>`
- Mã lỗi:
  - `409`: Chưa đủ 15 phút hoặc chưa thực hiện đủ 2 cuộc gọi hệ thống (`CHECK_IN_LOG.call_attempts < 2`).

#### `POST /api/dispatch/assignments/{assignmentId}/incidents`
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

#### `GET /api/dispatch/orders/{orderId}/status`
Khách hàng theo dõi realtime trạng thái điều phối đơn đặt.

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

#### `POST /api/dispatch/assignments/{assignmentId}/check-in/confirm`
Khách xác nhận thợ đã có mặt tại nhà khi GPS của thợ bị lệch > 100m (BR-04).

- Response 200:
```json
{
  "assignmentId": 1001,
  "isCustomerConfirmed": true,
  "status": "CHECKED_IN"
}
```

#### `POST /api/dispatch/orders/{orderId}/incidents/accept-substitute`
Khách đồng ý với thợ thay thế sau sự cố của thợ ban đầu.

- Response 200:
```json
{
  "orderId": 501,
  "substituteAccepted": true,
  "newEstimatedArrival": "2026-10-15T08:30:00Z"
}
```

#### `POST /api/dispatch/orders/{orderId}/incidents/decline-substitute`
Khách từ chối thợ thay thế hoặc quá 5 phút không tìm được thợ -> Hủy đơn và hoàn tiền 100%.

- Response 200:
```json
{
  "orderId": 501,
  "cancelled": true,
  "refundPercentage": 100.0,
  "refundAmount": 260000.0
}
```

---

### 3.3 Dành cho Quản Trị Viên (`AdminOnly`)

#### `GET /api/dispatch/admin/absent-reviews`
Danh sách các ca báo khách vắng mặt đang chờ duyệt.

- Response 200: `ApiResponse<List<CustomerAbsentReportDto>>`

#### `POST /api/dispatch/admin/absent-reviews/{assignmentId}/approve`
Admin phê duyệt khách vắng mặt (Q10).
Hệ thống xác thực nghiêm ngặt: GPS verified, wait >= 15 phút, calls >= 2.
- Thợ nhận 40% `gross_amount` (`absence_fee_amount`) và chuyển trạng thái về `IDLE`.
- Khách được hoàn 60% về phương thức thanh toán gốc.
- Nền tảng giữ 0%.

- Response 200:
```json
{
  "assignmentId": 1001,
  "approved": true,
  "workerFeePaid": 104000.0,
  "customerRefunded": 156000.0,
  "approvedAt": "2026-10-15T08:30:00Z"
}
```

#### `POST /api/dispatch/admin/absent-reviews/{assignmentId}/reject`
Admin từ chối báo cáo khách vắng mặt (nếu phát hiện gian lận).

- Request body:
```json
{
  "reason": "Khách xác nhận vẫn có mặt tại nhà qua camera"
}
```

#### `GET /api/dispatch/admin/incidents`
Bảng theo dõi các sự cố hiện trường và tiến độ Re-dispatch.

- Response 200: `ApiResponse<List<IncidentLogDto>>`

#### `POST /api/dispatch/admin/emergency-rescue`
Admin can thiệp điều phối "Cứu hộ khẩn cấp" (Super-Freelancer) khi Agency không có thợ thay thế (Pha 2, §2.5, Q12).

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

# Contract: Workers (module `Workers`, owner M4)

> Status: **awaiting final approval** (ticket BE-M4-00, issue #172). Nobody codes an endpoint that is not in an approved contract.
> Sources: `.spec/spec.md` §2.2, §2.6, §2.8, §4.2, BR-06/07/08 · `.spec/decisions.md` Q03, Q05, Q05b, Q11, Q20, Q22 · `.spec/plan/00-overview.md` §4 (ports), §6 (entities) · `Backend/GiupViec_Physical_DB_MVP5.drawio` (`WORKER`, `JOB_PHOTO`, `BOOKING_SLOT`, `JOB_ASSIGNMENT`).
> Implements tickets: BE-M4-01, BE-M4-02, BE-M4-04, BE-M4-05, BE-M4-06, BE-M4-07, BE-M4-08, BE-M4-09, BE-M4-10, MOB-M4-01..06, WEB-M4-01.

---

## 1. Conventions

- Base path `/api`. JSON, UTF-8. Property names **camelCase**. Timestamps ISO-8601 **UTC** (`2026-10-08T12:00:00Z`).
- Every response uses the envelope `ApiResponse<T>`: `{ "success": bool, "message": string, "data": T | null }`.
  - 2xx: `success = true`. Non-2xx: `success = false`, `data = null`, `message` is human readable error description.
- Roles: `Worker`, `Admin`, `Customer`, `Partner`.
- Authorization policies: `WorkerOnly`, `AdminOnly`, `CustomerOnly`, `PartnerOnly`.
- Authentication header: `Authorization: Bearer <accessToken>`.
- `ICurrentUser` exposes `Id`, `Role`, `IsAuthenticated`.
- Money values are integer VND rounded using `Vnd` helper (`Vnd.Round`, `Vnd.Net`).

### HTTP Status Code Mapping
| Status | Meaning |
|---|---|
| 200 / 201 | OK / Created |
| 400 | Malformed/invalid input or business validation failure (e.g. photo blur score below threshold) |
| 401 | Not authenticated, missing or expired Bearer/Registration token |
| 403 | Authenticated but role not allowed, or KYC not approved for restricted worker endpoints |
| 404 | Resource not found or not owned by caller |
| 409 | Conflict with current domain state (e.g. double-booking slot, invalid state transition) |
| 422 | Unprocessable Entity (e.g. image VoL score failed validation) |
| 500 | Unexpected error; body contains no sensitive details |

---

### WorkStatus values (AGENCY-UAT-02, ticket #444)

| Status | Meaning |
|---|---|
| `RETIRING` | Worker is being retired; finishes current work, then becomes `RETIRED` |
| `RETIRED` | Worker permanently retired; never restored; phone/national ID reserved |

### WorkStatus transitions (AGENCY-UAT-02)

| From | To | Condition |
|---|---|---|
| `IDLE` | `RETIRING` | Agency retires worker; no active jobs |
| `BUSY` | `RETIRING` | Agency retires worker; worker finishes current assignment then retires |
| `LOCKED` | `RETIRING` | Agency retires worker; no active jobs |
| `RETIRING` | `RETIRED` | No `CheckedIn|InProgress|AwaitingAcceptance` assignments and no future `ASSIGNED` jobs (sweep) |

**No edge leaves `RETIRED`.**
| 500 | Unexpected error; body contains no sensitive details |

---

## 2. Endpoints

### 2.1 Worker Registration & Profile

#### 2.1.1 `POST /api/workers/register` (Anonymous with `registrationToken`)
Registers a new Freelancer worker profile after phone OTP verification (`identity.md` §2.2 O1).

**Headers:**
`X-Registration-Token: <registrationToken>` (or `RegistrationToken` in request body)

**Request Body:**
```json
{
  "registrationToken": "string",
  "fullName": "string",
  "nationalId": "string (12 digits CCCD)",
  "address": "string",
  "bio": "string (optional)"
}
```

**Response 201 `data` (`WorkerProfileResponse`):**
```json
{
  "workerId": 101,
  "phoneNumber": "0912345678",
  "fullName": "Nguyen Van Thoi",
  "nationalId": "012345678901",
  "workerType": "FREELANCER",
  "agencyId": null,
  "workStatus": "IDLE",
  "kycStatus": "PENDING",
  "ratingAvg": 5.00,
  "completedJobs": 0,
  "isSuperFreelancer": false,
  "createdAt": "2026-10-08T12:00:00Z"
}
```

| Status | Condition |
|---|---|
| 400 | `registrationToken` missing/invalid/expired, `nationalId` invalid format, `fullName` empty |
| 409 | `nationalId` or `phoneNumber` already registered by another worker |

---

#### 2.1.2 `GET /api/workers/me` (WorkerOnly)
Returns current authenticated worker's profile.

**Response 200 `data` (`WorkerProfileResponse`):**
```json
{
  "workerId": 101,
  "phoneNumber": "0912345678",
  "fullName": "Nguyen Van Thoi",
  "nationalId": "012345678901",
  "workerType": "FREELANCER",
  "agencyId": null,
  "workStatus": "IDLE",
  "kycStatus": "APPROVED",
  "ratingAvg": 4.85,
  "completedJobs": 24,
  "isSuperFreelancer": false,
  "createdAt": "2026-10-08T12:00:00Z"
}
```

---

#### 2.1.3 `PATCH /api/workers/me` (WorkerOnly)
Updates editable worker profile fields.

**Request Body:**
```json
{
  "fullName": "Nguyen Van Thoi",
  "avatarUrl": "https://storage.giupviec.local/avatars/101.jpg",
  "bio": "5 nam kinh nghiem don dep nha va giat ui."
}
```

**Response 200 `data`:** `WorkerProfileResponse`

---

#### 2.1.4 `GET /api/workers/{id}` (AdminOnly / CustomerOnly)
Public/Admin view of worker profile. Customers can view worker details if the worker is assigned or favorited.

**Response 200 `data` (`WorkerPublicProfileResponse`):**
```json
{
  "workerId": 101,
  "fullName": "Nguyen Van T.",
  "avatarUrl": "https://storage.giupviec.local/avatars/101.jpg",
  "workerType": "FREELANCER",
  "workStatus": "IDLE",
  "kycStatus": "APPROVED",
  "ratingAvg": 4.85,
  "completedJobs": 24,
  "isSuperFreelancer": false
}
```

---

### 2.2 eKYC (Freelancer Identity Verification)

#### 2.2.1 `POST /api/workers/me/ekyc` (WorkerOnly)
Submits CCCD photos and selfie for automated eKYC verification (`IEkycProvider`, Fake in Phase 1 per Q05).

**Request Body:**
```json
{
  "frontCccdUrl": "https://storage.giupviec.local/ekyc/101_front.jpg",
  "backCccdUrl": "https://storage.giupviec.local/ekyc/101_back.jpg",
  "selfieUrl": "https://storage.giupviec.local/ekyc/101_selfie.jpg"
}
```

**Response 200 `data` (`EkycResultResponse`):**
```json
{
  "workerId": 101,
  "confidenceScore": 92.00,
  "kycStatus": "APPROVED",
  "autoApproved": true,
  "reviewedAt": "2026-10-08T12:05:00Z",
  "rejectionReason": null
}
```

| Status | Condition |
|---|---|
| 400 | Photo URLs missing or malformed |
| 409 | eKYC already approved |

**Rules:**
- Fake `IEkycProvider` returns confidence `92.00%` by default (`Ekyc.Fake.Confidence`).
- `confidenceScore >= 85.00` (`Ekyc.AutoApproveConfidence`) -> `kycStatus = APPROVED`, `workStatus = IDLE` immediately.
- `confidenceScore < 85.00` or fraud flag -> `kycStatus = MANUAL_REVIEW`, added to Admin review queue.
- Freelancer cannot receive job assignments until `kycStatus == APPROVED`.

---

#### 2.2.2 `GET /api/admin/workers/ekyc-queue` (AdminOnly)
Lists pending manual eKYC reviews for Admin.

**Query Parameters:** `page=1&pageSize=20&status=PENDING`. `status` (ADMIN-UAT-06) selects the list of the Admin audit page: `PENDING` (default: `MANUAL_REVIEW`, `PENDING`, `AUDIT_PENDING`), `APPROVED` (`kyc_status = APPROVED` and `kyc_reviewed_by` set, that is approved by an Admin; a worker the provider approved is not in it) or `REJECTED`; any other value is a 400 with `errors.status`. The Web page sends the tab it shows.

**Known gap:** `frontCccdUrl`, `backCccdUrl` and `selfieUrl` are always null: the worker's photo addresses are validated and passed to the provider but not stored (no column), so the Admin review dialog has no photo to compare. Needs a schema decision of M1/M4.

**Response 200 `data` (`PagedList<EkycQueueItemResponse>`):**
```json
{
  "items": [
    {
      "workerId": 102,
      "fullName": "Tran Van B",
      "phoneNumber": "0987654321",
      "nationalId": "098765432109",
      "frontCccdUrl": "https://storage.giupviec.local/ekyc/102_front.jpg",
      "backCccdUrl": "https://storage.giupviec.local/ekyc/102_back.jpg",
      "selfieUrl": "https://storage.giupviec.local/ekyc/102_selfie.jpg",
      "confidenceScore": 78.50,
      "submittedAt": "2026-10-08T11:30:00Z"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20
}
```

---

#### 2.2.3 `POST /api/admin/workers/{id}/ekyc-review` (AdminOnly)
Admin manual approval or rejection of a worker's eKYC.

**Request Body:**
```json
{
  "approved": true,
  "rejectionReason": null
}
```

**Response 200 `data` (`EkycResultResponse`)**

---

### 2.3 Block Slots (Freelancer Availability Roster)

#### 2.3.1 `GET /api/workers/me/slots` (WorkerOnly)
Returns active blocked availability slots for the specified date range.

**Query Parameters:** `startDate=2026-10-08&endDate=2026-10-14`

**Response 200 `data` (`List<BookingSlotResponse>`):**
```json
[
  {
    "slotId": 501,
    "workerId": 101,
    "slotDate": "2026-10-09",
    "shiftCode": "SHIFT_MORNING",
    "startTime": "08:00",
    "endTime": "12:00",
    "isActive": true
  },
  {
    "slotId": 502,
    "workerId": 101,
    "slotDate": "2026-10-09",
    "shiftCode": "SHIFT_AFTERNOON",
    "startTime": "13:00",
    "endTime": "17:00",
    "isActive": true
  }
]
```

**Shifts:**
- `SHIFT_MORNING`: 08:00 – 12:00
- `SHIFT_AFTERNOON`: 13:00 – 17:00
- `SHIFT_EVENING`: 17:30 – 20:30

---

#### 2.3.2 `POST /api/workers/me/slots/toggle` (WorkerOnly)
Enables or disables an availability slot for a given date and shift.

**Request Body:**
```json
{
  "slotDate": "2026-10-09",
  "shiftCode": "SHIFT_MORNING",
  "isActive": true
}
```

**Response 200 `data` (`BookingSlotResponse`)**

**Rules:**
- Table `BOOKING_SLOT` has `UNIQUE(worker_id, slot_date, shift_code)`.
- Operation is idempotent (double clicks / concurrent requests return existing slot state safely without duplicate row errors).
- The toggle only moves `AVAILABLE <-> BLOCKED`. A slot owned by Dispatch (`HELD` by an offer or agency hold, `BOOKED` by an assignment) is refused with **409 `SLOT_IN_USE`** and keeps its status (#398).

---

### 2.3A Location and payout account (ECON-FLOW-FIX, decisions D19)

#### `PUT /api/workers/me/location` (WorkerOnly)
Where the worker is now. The dispatch search offers a job only to a worker who has a position (`WORKER.current_lat/current_lng`).

**Request Body:** `{ "latitude": 10.7769, "longitude": 106.7009 }` (latitude -90..90, longitude -180..180).
**Response 200 `data`:** `{ "latitude": 10.7769, "longitude": 106.7009 }` (six decimals kept). 400 in the shared validation envelope when a value is missing or out of range.

#### `GET /api/workers/me/bank` and `PUT /api/workers/me/bank` (WorkerOnly)
The account the platform transfers the worker's monthly payout to (`WORKER.bank_name`, `bank_account_no`; the payout export reads them).
- `GET` 200 `data`: `{ "bankName": "", "bankAccountNo": "", "isSet": false }` (empty strings while never set).
- `PUT` body `{ "bankName": "Vietcombank", "bankAccountNo": "0011001234567" }`; both required; `bankName` <= 100 characters, `bankAccountNo` 6 to 30 digits (spaces dropped). 200 with the stored values and `isSet: true`; 400 otherwise.

---

### 2.4 Image Quality & VoL Before/After Verification

#### 2.4.1 `POST /api/workers/assignments/{assignmentId}/photos` (WorkerOnly)
Uploads a work photo as a file and verifies it using Variance of Laplacian (`IImageQualityService`, BR-06, Q03). Decision D21 (WORKERS-PHOTO-01): the sharpness is measured on the real pixels of the uploaded file.

**Request:** `multipart/form-data` with the parts

| Part | Type | Rule |
|---|---|---|
| `file` | file | Required. JPEG, PNG or WebP (`image/jpeg`, `image/png`, `image/webp`), at most 5 MB (5 242 880 bytes), at most 50 megapixels. The first bytes must be the signature of the declared content type. |
| `photoPhase` | string | `BEFORE` or `AFTER` (case and surrounding spaces ignored). |
| `angleNo` | integer | 1..5. |

**Response 200 `data` (`JobPhotoResponse`):**
```json
{
  "photoId": 801,
  "assignmentId": 2001,
  "photoPhase": "BEFORE",
  "angleNo": 1,
  "photoUrl": "/api/workers/assignments/2001/photos/801/file",
  "volScore": 142.50,
  "isAccepted": true,
  "uploadedAt": "2026-10-08T14:02:00Z"
}
```
`photoUrl` is the address of the authenticated file endpoint (section 2.4.3), built by the server. It is never an address the client chose.

| Status | Condition |
|---|---|
| 400 | Photo blur score `volScore < 100.0` (`Vol.Threshold` default 100.0). Error on `volScore`: `"Ảnh bị mờ (độ nét 45.2, cần tối thiểu 100.0). Vui lòng chụp lại ảnh rõ hơn."` The photo is still stored, with `isAccepted = false`, for audit. |
| 400 | No `file` part, or an empty file (field `file`) |
| 400 | The file is larger than 5 MB, or claims more than 50 megapixels, or has the signature of an image but cannot be decoded (field `file`) |
| 400 | The content type is not `image/jpeg`, `image/png` or `image/webp`, or the bytes are not that type (field `contentType`) |
| 400 | `angleNo` out of range (must be 1..5), or phase invalid |
| 400 | `AFTER` photo `angleNo` does not match an accepted `BEFORE` photo `angleNo` |
| 403 | The caller is a Worker who is not assigned to this assignment |
| 404 | Unknown assignment |
| 409 | The assignment is not `CHECKED_IN` or `IN_PROGRESS` |
| 409 | Phase already reached maximum `Photos.MaxPerPhase` (5 accepted photos) |

**Rules:**
- The file is decoded with SkiaSharp and scaled to 640px width (`Vol.ResizeWidthPx`) for the VoL computation; a photo narrower than that is measured as it is.
- Rejected photo (`volScore < 100.0`) is stored (file and row) with `isAccepted = false` for audit, but returns HTTP 400 prompting retake. A file whose row could not be saved is removed again.
- The first accepted `BEFORE` photo of a `CHECKED_IN` assignment moves it to `IN_PROGRESS`, in the same save as the photo (D18).
- Min 3 (`Photos.MinPerPhase`), Max 5 (`Photos.MaxPerPhase`) accepted photos per phase required before submitting job completion.
- **Development environment only:** the body `application/json` `{ "photoPhase": "BEFORE | AFTER", "angleNo": 1, "photoUrl": "https://..." }` is still accepted, and its sharpness is judged by the stand-in of D18 (a URL containing `blur`, `low_quality` or `vol_45` is blurry, any other is sharp); the response `photoUrl` is the URL that was sent. In every other environment this body is refused with 400 (field `photoUrl`: the photo must be sent as a file). This body is not part of the OpenAPI document.

---

#### 2.4.2 `GET /api/workers/assignments/{assignmentId}/photos` (WorkerOnly / CustomerOnly / AdminOnly)
Returns all uploaded photos for a job assignment.

**Response 200 `data` (`List<JobPhotoResponse>`)**
For a photo that was uploaded as a file, `photoUrl` is the address of the file endpoint (section 2.4.3); for a photo that has only a client URL (the Development JSON body, or a photo from before the upload existed) it is that URL.

| Status | Condition |
|---|---|
| 403 | Not the assignment's worker, not its customer, not an Admin, or any other role |
| 404 | Unknown assignment |

#### 2.4.3 `GET /api/workers/assignments/{assignmentId}/photos/{photoId}/file` (WorkerOnly / CustomerOnly / AdminOnly)
Returns the stored file of one photo (decision D21). **Not anonymous:** the photos show the inside of a customer's home, so the rule of 2.4.2 applies (the assignment's worker, its customer, an Admin) and the request needs the bearer token. The response is the file itself (`image/jpeg`, `image/png` or `image/webp`, from the stored name) with `X-Content-Type-Options: nosniff` and `Cache-Control: private, max-age=3600`.

| Status | Condition |
|---|---|
| 403 | Not the assignment's worker, not its customer, not an Admin, or any other role |
| 404 | Unknown assignment, or a photo that is not of this assignment, or a photo without a stored file, or a stored file that is gone |

---

### 2.5 Acceptance & Completion (BR-07, Q11)

#### 2.5.1 `POST /api/workers/assignments/{assignmentId}/submit-completion` (WorkerOnly)
Worker submits completed job for customer acceptance after uploading all required Before/After photos.

**Request Body:** empty or `{ "notes": "string" }`

**Response 200 `data` (`AssignmentStatusResponse`):**
```json
{
  "assignmentId": 2001,
  "status": "AWAITING_ACCEPTANCE",
  "submittedAt": "2026-10-08T16:00:00Z"
}
```

| Status | Condition |
|---|---|
| 400 | Less than 3 accepted `BEFORE` or `AFTER` photos, or `AFTER` angles do not match `BEFORE` angles |
| 409 | Assignment status is not `IN_PROGRESS` |

---

#### 2.5.2 `POST /api/customers/assignments/{assignmentId}/accept` (CustomerOnly)
Customer confirms job completion. Calculates payout and completes assignment.

**Request Body:** empty or `{ "feedback": "string" }`

**Response 200 `data` (`AssignmentCompletionResponse`):**
```json
{
  "assignmentId": 2001,
  "status": "COMPLETED",
  "grossAmount": 260000,
  "commissionRate": 0.20,
  "payoutAmount": 208000,
  "completedAt": "2026-10-08T16:15:00Z"
}
```

**Rules:**
- Updates `JOB_ASSIGNMENT` status to `COMPLETED`.
- Computes `payoutAmount = grossAmount - Vnd.Round(grossAmount * commissionRate)` (for Freelancer commission 20%, payout = 80% = 208,000 VND for 260,000 VND shift).
- Sets worker `workStatus = IDLE`.
- Emits `JobCompleted` domain event (M6 rating window opens, payout batch accumulator receives item).

---

#### 2.5.3 `POST /api/customers/assignments/{assignmentId}/request-redo` (CustomerOnly)
Customer requests a 15–30 minute touch-up/redo if work is unsatisfied.

**Request Body:**
```json
{
  "reason": "Chua lau sach bui duoi gam ban va cua kinh tuong.",
  "redoPhotoUrl": "https://storage.giupviec.local/jobs/2001_redo_note.jpg"
}
```

**Response 200 `data` (`AssignmentStatusResponse`):**
```json
{
  "assignmentId": 2001,
  "status": "IN_PROGRESS",
  "notes": "Yeu cau don lai: Chua lau sach bui duoi gam ban..."
}
```

---

### 2.6 Extension Handling ("Làm lần 2", BR-08)

#### 2.6.1 `POST /api/workers/assignments/{assignmentId}/extension/respond` (WorkerOnly)
Worker accepts or declines an extension request paid by the customer (`ExtensionPaid` event listened).

**Request Body:**
```json
{
  "accept": true
}
```

**Response 200 `data` (`ExtensionResponse`):**
```json
{
  "assignmentId": 2001,
  "extensionId": 301,
  "extraHours": 2,
  "extraAmount": 130000,
  "status": "ACCEPTED",
  "newEndTime": "19:00"
}
```

**Rules:**
- If `accept == true`: extension is activated, shift extended, worker remains `BUSY`.
- If `accept == false`: extension status set to `DECLINED`, emits `ExtensionDeclined` domain event (M2 triggers 100% refund of extension fee to customer).

**Implemented by WORKERS-EXT-01 (BE-M4-09):**
- `newEndTime` is the local shift end (Asia/Ho_Chi_Minh, `HH:mm`) plus `extraHours` when accepted, the unchanged shift end when declined.
- Accept is one conditional update of `JOB_ORDER_EXTENSION` that only applies while `ext_status = PAID`: `ext_status` and `worker_decision` become `ACCEPTED`, `decided_at` = now (`IClock`); two simultaneous answers decide once.
- Decline only publishes `ExtensionDeclined`; Payments (`ExtensionDeclinedHandler`) sets `DECLINED` / `decided_at` and refunds 100 %. The Workers service does not write `DECLINED` itself: the handler treats an already `DECLINED` extension as done and would skip the refund. The `status` of the response is `DECLINED`.
- There is no `ExtensionPaid` listener in Workers: Payments already writes `PAID`, and the worker reads the extension through this endpoint.

| Status | Condition |
|---|---|
| 401 | no token |
| 403 | the caller is not a Worker |
| 404 | the assignment does not exist, is not the caller's, or its order has no extension asked of this worker |
| 409 | `ext_status` is not `PAID` (`PENDING_PAYMENT`, `ACCEPTED`, `DECLINED`, `EXPIRED`), or another answer came first |

---

## 3. Ports & Interfaces (Module `Workers`)

### 3.1 `IWorkerAvailabilityQuery` (Implemented by M4)
Used by Dispatch (M3) during candidate scanning.
```csharp
public interface IWorkerAvailabilityQuery
{
    Task<List<WorkerCandidateDto>> GetAvailableFreelancersAsync(
        DateTime slotDate,
        string shiftCode,
        double latitude,
        double longitude,
        double maxRadiusKm,
        CancellationToken ct = default);
}
```

### 3.2 `IEkycProvider` (Implemented by M4)
Fake provider in Phase 1 (Q05).
```csharp
public interface IEkycProvider
{
    Task<EkycResultDto> VerifyAsync(
        string frontCccdUrl,
        string backCccdUrl,
        string selfieUrl,
        CancellationToken ct = default);
}
```

### 3.3 `IImageQualityService` (Implemented by M4)
Computes Variance of Laplacian blur score for job photos (Q03).
```csharp
public interface IImageQualityService
{
    Task<ImageQualityResultDto> AnalyzeBlurAsync(
        string photoUrl,
        CancellationToken ct = default);
}
```

### 3.4 `IWorkerProfileQuery` (Implemented by M4)
Query port for Customer module (decisions Q21 C3 favorite workers list).
```csharp
public interface IWorkerProfileQuery
{
    Task<WorkerProfileSummaryDto?> GetWorkerSummaryAsync(int workerId, CancellationToken ct = default);
    Task<bool> ExistsAsync(int workerId, CancellationToken ct = default);
}
```

---

## 4. Domain Events

### Published by Workers (M4)
- `JobCompleted`: Published when customer accepts assignment completion (`assignmentId`, `workerId`, `customerId`, `grossAmount`, `payoutAmount`, `completedAt`).
- `ExtensionDeclined`: Published when worker declines extra hours (`assignmentId`, `extensionId`, `extraAmount`, `declinedAt`).

### Listened by Workers (M4)
- `ExtensionPaid` (from M2): Triggers extension prompt for worker.
- `CustomerAbsentApproved` (from M6/M3): Updates worker `workStatus` to `IDLE` and sets `absence_fee_amount` (40% compensation).

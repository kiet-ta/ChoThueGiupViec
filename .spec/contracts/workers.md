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

**Query Parameters:** `page=1&pageSize=20`

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

---

### 2.4 Image Quality & VoL Before/After Verification

#### 2.4.1 `POST /api/workers/assignments/{assignmentId}/photos` (WorkerOnly)
Uploads and verifies a work photo using Variance of Laplacian (`IImageQualityService`, BR-06, Q03).

**Request Body:**
```json
{
  "photoPhase": "BEFORE | AFTER",
  "angleNo": 1,
  "photoUrl": "https://storage.giupviec.local/jobs/2001_before_angle1.jpg"
}
```

**Response 200 `data` (`JobPhotoResponse`):**
```json
{
  "photoId": 801,
  "assignmentId": 2001,
  "photoPhase": "BEFORE",
  "angleNo": 1,
  "photoUrl": "https://storage.giupviec.local/jobs/2001_before_angle1.jpg",
  "volScore": 142.50,
  "isAccepted": true,
  "uploadedAt": "2026-10-08T14:02:00Z"
}
```

| Status | Condition |
|---|---|
| 400 | Photo blur score `volScore < 100.0` (`Vol.Threshold` default 100.0). Message: `"Photo blur score (45.2) is below threshold (100.0). Please retake a sharper photo."` |
| 400 | `angleNo` out of range (must be 1..5), or phase invalid |
| 400 | `AFTER` photo `angleNo` does not match an accepted `BEFORE` photo `angleNo` |
| 409 | Phase already reached maximum `Photos.MaxPerPhase` (5 accepted photos) |

**Rules:**
- Photos are resized to 640px width (`Vol.ResizeWidthPx`) for VoL computation.
- Rejected photo (`volScore < 100.0`) is stored with `isAccepted = false` for audit, but returns HTTP 400 prompting retake.
- Min 3 (`Photos.MinPerPhase`), Max 5 (`Photos.MaxPerPhase`) accepted photos per phase required before submitting job completion.

---

#### 2.4.2 `GET /api/workers/assignments/{assignmentId}/photos` (WorkerOnly / CustomerOnly / AdminOnly)
Returns all uploaded photos for a job assignment.

**Response 200 `data` (`List<JobPhotoResponse>`)**

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

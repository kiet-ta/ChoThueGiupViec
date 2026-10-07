# Contract: Admin (module `Admin`, owner M6)

> Status: **merged by the leader in PR #64** (ticket BE-M6-00, issue #62). The open questions of section 4 (A1-A8) are still unanswered: an endpoint whose rules depend on one of them waits for the leader's answer; section 2.4 (audit log read, no open question) is implemented by BE-M6-09b.
> Sources: `.spec/spec.md` §1.3 (Admin role), BR-05 · `.spec/decisions.md` G-1, G-2, G-5, Q10, Q12, Q16, Q22 D4/D6, G-7 · `.spec/plan/00-overview.md` §4 (`IAuditLog`, `IRefundService`), §5 (`CustomerAbsentReported`, `CustomerAbsentApproved`), §10 (dev Admin seed) · `Backend/GiupViec_Physical_DB_MVP5.drawio` tables `ADMIN`, `ADMIN_AUDIT_LOG`, `CHECK_IN_LOG`, `JOB_ASSIGNMENT`, `WORKER` · entities `AdminAccount.cs`, `AdminAuditLog.cs`, `CheckInLog.cs`.
> Implements tickets: BE-M6-03 (absence approval), BE-M6-06 (admin account + dashboard metrics), BE-M6-09 (real `IAuditLog`, Super-Freelancer). UI: WEB-M6-02 (absence approval, Figma `19:3`), WEB-M6-04 (operations dashboard, Figma `182:2`..`182:131`).
> Conventions (envelope, camelCase, UTC, status codes, roles, policies, 404-for-not-owned) are defined in `identity.md` §1 and apply here unchanged. Admin login (email + password, lockout) is `identity.md` §2.3 and is **not** repeated. Every endpoint here requires policy **`AdminOnly`**. Items marked **Ax** are not decided by the PRD or `decisions.md`; they are listed in section 4 with a recommended default.

Only the Admin created by the dev seed (overview §10) exists in the MVP. This contract does **not** add create/list/deactivate Admin endpoints (**A1**).

## 1. Data shapes

`AdminProfile`
```json
{ "adminId": 0, "email": "string", "fullName": "string", "adminRole": "string", "isActive": true, "createdAt": "ISO-8601 UTC" }
```
(`ADMIN` table, C# entity `AdminAccount` per Q22 D4; `password_hash`, `failed_login_count`, `locked_until` are never exposed.)

`AuditLogEntry` (read-only; table `ADMIN_AUDIT_LOG`, SC-3)
```json
{ "logId": 0, "actorType": "ADMIN | SYSTEM", "adminId": 0, "entityType": "string", "entityId": "string", "fieldName": "string", "oldValue": "string | null", "newValue": "string | null", "reason": "string | null", "changedAt": "ISO-8601 UTC" }
```

`AbsenceReport` (an assignment where the worker pressed "Khách vắng mặt")
```json
{
  "assignmentId": 0, "orderId": 0, "orderCode": "string",
  "workerId": 0, "workerName": "string", "workerType": "FREELANCER | AGENCY_STAFF", "agencyName": "string | null",
  "customerName": "string",
  "status": "PENDING | APPROVED | REJECTED",
  "checkedInAt": "ISO-8601 UTC", "customerAbsentAt": "ISO-8601 UTC",
  "gpsVerified": true, "distanceM": 0.0, "deviceLat": 0.0, "deviceLng": 0.0,
  "callAttempts": 0, "waitedMinutes": 0,
  "grossAmount": 0, "absenceFeeAmount": 0, "customerRefundAmount": 0,
  "photoUrl": "string | null",
  "canApprove": true, "blockReasons": ["string"]
}
```
(Fields from `CHECK_IN_LOG` and `JOB_ASSIGNMENT`. `absenceFeeAmount` is the preview `Vnd.Round(grossAmount x 0.40)` until approved, then the stored `absence_fee_amount`; `customerRefundAmount = grossAmount - absenceFeeAmount`.)

## 2. Endpoints

### 2.1 Profile (BE-M6-06)
**`GET /api/admin/me`** -> 200 `data: AdminProfile`. 404 only if the row was removed.

### 2.2 Absence approval (BE-M6-03, WEB-M6-02)

**`GET /api/admin/absence-reports?status=&page=&pageSize=`** -> 200 `data: { items: AbsenceReport[], page, pageSize, total }`, default `status = PENDING`, oldest `customerAbsentAt` first. A report exists when `CHECK_IN_LOG.customer_absent_at` is set (M3 published `CustomerAbsentReported`); there is no separate table (**A3**).
**`GET /api/admin/absence-reports/{assignmentId}`** -> 200 `data: AbsenceReport`. 404 if the assignment has no absence report.

**`POST /api/admin/absence-reports/{assignmentId}/approve`** -> 200 `data: AbsenceReport`. No body.
The system **refuses** unless all hold (Q10): (a) `gps_verified`, (b) `call_attempts >= 2`, (c) at least 15 minutes since `checked_in_at`, (d) `customer_absent_at` is set. A refusal is **409** with `blockReasons` listing which of `GPS_NOT_VERIFIED`, `CALLS_BELOW_MINIMUM`, `WAIT_BELOW_MINIMUM`, `ABSENCE_NOT_REPORTED` failed. 409 also if already decided.
On success, in one transaction (per affected assignment, Q10):
- `absence_fee_amount = Vnd.Round(gross_amount x Absence.FeeRate)` (0.40); the assignment moves to `ABSENT` through the state machine.
- The customer is charged exactly that 40% and the other 60% is refunded through `IRefundService` to the original payment method; the platform keeps nothing.
- The worker returns to `IDLE` (Workers module reacts to the event, `WORKER.work_status` is not written here, Q22 D6).
- `CustomerAbsentApproved { assignmentId, orderId, workerId, agencyId, feeAmount, refundAmount }` is published; the customer is notified and has 24 h to dispute (`disputes.md`).

**`POST /api/admin/absence-reports/{assignmentId}/reject`** -> 200 `data: AbsenceReport`
```json
{ "reason": "string" }
```
400 if `reason` is empty or over 255. 409 if already decided. Effect of a rejection on the assignment is **A4**.

### 2.3 Operations dashboard (BE-M6-06, WEB-M6-04)

**`GET /api/admin/dashboard`** -> 200
```json
{
  "generatedAt": "ISO-8601 UTC",
  "orders": { "today": 0, "thisWeek": 0 },
  "shifts": { "inProgress": 0, "completedToday": 0 },
  "disputes": { "open": 0, "nearSla": 0 }
}
```
Exactly the three groups the plan names (orders, shifts, open disputes); no other metric is added without the leader (G-7). Definitions are **A2**. The "Tranh chấp sắp quá hạn" list on the dashboard reuses `GET /api/admin/disputes?nearSla=true&pageSize=5` (`disputes.md` §2.2), so the dashboard does not duplicate it. Errors and loading states are the standard envelope (the UI has loading/empty/error states).

**(BE-M6-06b, ticket #142) As built:** the counts follow the recommended defaults of **A2** (not yet confirmed by the leader). Days and the ISO week are those of `Asia/Ho_Chi_Minh` (Sunday 23:59:59 local still belongs to the ending week, Monday 00:00 local starts the new one even when the UTC date is still Sunday); `shifts.inProgress` counts `CHECKED_IN`, `IN_PROGRESS` and `AWAITING_ACCEPTANCE`; `disputes.open` counts `OPEN` and `IN_REVIEW`; `disputes.nearSla` is open tickets with `sla_due_at <= now + Admin:DisputeNearSlaHours` (default 6, configurable in one options class, overdue included). The module reads `JOB_ORDER`, `JOB_ASSIGNMENT` and `DISPUTE_TICKET` read-only because no read port exists (**A6**).

### 2.4 Audit log (BE-M6-09)

`IAuditLog` (port) gets its real implementation here: **append-only**; there is no update or delete endpoint or method. `Append` runs in the **same DB transaction** as the change it records (G-5) and rejects an empty `reason` for Admin actors.

**`GET /api/admin/audit-logs?entityType=&entityId=&actorType=&from=&to=&page=&pageSize=`** -> 200 `data: { items: AuditLogEntry[], page, pageSize, total }`, newest first; `from`/`to` are ISO-8601 UTC dates; `pageSize` max 100. No write endpoints.

### 2.5 Super-Freelancer (BE-M6-09)

**`POST /api/admin/workers/{workerId}/super-freelancer`** -> 200 `data: { workerId, isSuperFreelancer: true }`
```json
{ "reason": "string" }
```
Q12: allowed only if `rating_avg >= 4.80`, `completed_jobs >= 50`, KYC approved, and no upheld dispute with `fault_party = FREELANCER` in the last 180 days. Otherwise **409** with `data.failedCriteria` listing `RATING_BELOW_MINIMUM`, `COMPLETED_JOBS_BELOW_MINIMUM`, `KYC_NOT_APPROVED`, `UPHELD_DISPUTE_RECENT`. 404 if the worker does not exist; 409 `NOT_FREELANCER` for `AGENCY_STAFF`. Writes one audit row (`entity_type = WORKER`, `field_name = is_super_freelancer`, `reason`).
**`DELETE /api/admin/workers/{workerId}/super-freelancer`** -> 200, body `{ "reason": "string" }`, revokes manually and audits the same way.

**Automatic revoke** (Q12): when a `RatingSubmitted` event leaves `rating_avg < 4.70`, the Admin module sets `is_super_freelancer = false` and writes an audit row with `actor_type = SYSTEM`, `admin_id = null`, `reason = "rating below 4.70"`.

## 3. Events
- **Handles** `CustomerAbsentReported` (M3): notifies Admins through `INotificationService`; the queue itself is derived from data (2.2). **Handles** `RatingSubmitted` (Ratings) for the auto-revoke above.
- **Publishes** `CustomerAbsentApproved` (consumers M2 refund bookkeeping, M3, M4, M6 Disputes notification).

## 4. Open questions (not covered by PRD or decisions; recommended default in bold)

| # | Question | Recommended default |
|---|---|---|
| **A1** | "Tài khoản Admin" (plan BE-M6-06): create/list/deactivate Admin accounts? AGENTS.md forbids creating other admins by hand. | **Only `GET /api/admin/me`; no admin management endpoints in the MVP.** |
| **A2** | Dashboard definitions. Orders: by `created_at` or `scheduled_date`? "This week" start day? Shifts: which statuses are "in progress"? "Near SLA" threshold? | **Orders = `JOB_ORDER.created_at` falling today / this ISO week (Monday start) in `Asia/Ho_Chi_Minh` (G-3). Shifts in progress = assignments in `CHECKED_IN`, `IN_PROGRESS`, `AWAITING_ACCEPTANCE`; completed today = `COMPLETED` with `completed_at` today. Disputes open = `OPEN` + `IN_REVIEW`. Near SLA = open and `sla_due_at - now <= Admin.DisputeNearSlaHours`, config default 6 h** (the 6 h and the "Hoàn tất hôm nay" period were chosen by the designer in the Figma frame, **not decided by the leader**: needs confirmation). |
| **A3** | Is a separate absence-report table needed (approved/rejected state)? | **No schema change: PENDING = `customer_absent_at` set and assignment not yet `ABSENT`; APPROVED = assignment `ABSENT` with `absence_fee_amount`; REJECTED derived from the audit log (`entity_type = JOB_ASSIGNMENT`, `field_name = absence_report`).** |
| **A4** | What does "Bác yêu cầu" do to the assignment (PRD and Q10 only define approval)? | **Records the rejection and reason in the audit log and notifies the worker; the assignment is left as it is (the worker can continue if the customer shows up, M3 owns the next transition).** If a state transition is needed, the leader must define it. |
| **A5** | Figma `19:3` shows the call log per call (time, duration) and a door photo; `CHECK_IN_LOG` stores only `call_attempts` and `fallback_photo_url`. | **Contract returns `callAttempts` and `photoUrl` only; the UI must drop the per-call table.** A per-call log would be an M1 schema change. |
| **A6** | The queue needs data from M3 (`CHECK_IN_LOG`) and M2/M4 (`JOB_ORDER`, `JOB_ASSIGNMENT`, `WORKER`); dashboard counts need `JOB_ORDER`. Modules may not read each other's tables. | **`JOB_ASSIGNMENT` is the flat shared node and may be read; for `JOB_ORDER`, `CHECK_IN_LOG` and `WORKER` the Admin module gets a read-only port `IAdminReadModel` (Fake first, implementer M1 over the shared `DbContext`) via a Scope exception on BASE-03.** Leader decides. |
| **A7** | Writing `WORKER.is_super_freelancer` (M4's table) and reading `kyc_status`. | **M6 owns this single flag per the plan (BE-M6-09); the write goes through a small command port `IWorkerFlagCommands` (implementer M4) and the KYC check through `IWorkerProfileQuery` extended with `kycStatus` and `lastUpheldFaultAt`.** Leader decides. |
| **A8** | Figma `65:283` (Khách hàng & Uy tín) and `19:3` blocks "Vi phạm đồ nghề (BR-39)" and "Hậu kiểm eKYC" have no ticket in the M6 plan. | **Not in this contract.** eKYC re-check belongs to M4; equipment violation and customer reputation need their own tasks. |

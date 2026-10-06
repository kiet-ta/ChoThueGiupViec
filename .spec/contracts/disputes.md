# Contract: Disputes (module `Disputes`, owner M6)

> Status: **DRAFT, awaiting leader approval** (ticket BE-M6-00, issue #62). Nobody codes an endpoint that is not in an approved contract.
> Sources: `.spec/spec.md` §4.3, BR-05 · `.spec/decisions.md` Q09, Q10, Q11, Q22 D3, G-2, G-3, G-7 · `.spec/plan/00-overview.md` §4 (`IRefundService`, `ISlaPenaltyService`, `IFileStorage`), §5 (`DisputeResolved`) · `Backend/GiupViec_Physical_DB_MVP5.drawio` table `DISPUTE_TICKET` · entity `Backend/Domain/Entities/DisputeTicket.cs`, enum `FaultParty`.
> Implements tickets: BE-M6-02 (dispute filing and verdict). UI: WEB-M6-01 (dispute console, Figma `66:2`), MOB-M6-03 (file a dispute).
> Conventions (envelope, camelCase, UTC, status codes, roles, policies, 404-for-not-owned) are defined in `identity.md` §1 and apply here unchanged. Items marked **Dx** are not decided by the PRD or `decisions.md`; they are listed in section 4 with a recommended default.

## 1. Data shapes

`Dispute`
```json
{
  "disputeId": 0,
  "orderId": 0,
  "raisedBy": "CUSTOMER | WORKER",
  "category": "string",
  "description": "string",
  "evidenceUrls": ["string"],
  "disputeStatus": "OPEN | IN_REVIEW | RESOLVED | DISMISSED",
  "faultParty": "FREELANCER | AGENCY | CUSTOMER | null",
  "compensationAmount": 0,
  "slaDueAt": "ISO-8601 UTC",
  "resolvedAt": "ISO-8601 UTC | null",
  "createdAt": "ISO-8601 UTC"
}
```
(`DISPUTE_TICKET`: `dispute_id, order_id, resolved_by, raised_by VARCHAR(10), category VARCHAR(15), description NVARCHAR(1000), evidence_urls NVARCHAR(MAX), dispute_status VARCHAR(12), fault_party, compensation_amount DECIMAL(18,2) NULL, sla_due_at, resolved_at, created_at`.) `resolved_by` is exposed only to Admin. `evidenceUrls` is stored as a JSON array in `evidence_urls`. `faultParty` is null while open and after a dismissal (Q22 D3). The table has no `assignment_id`: a dispute is about an order; the worker(s) involved are the assignments of that order (see **D2**).

`DisputeSummary` (Admin queue row; Figma `66:2`)
```json
{
  "disputeId": 0, "orderId": 0, "orderCode": "string",
  "customerName": "string",
  "workers": [ { "workerId": 0, "fullName": "string", "workerType": "FREELANCER | AGENCY_STAFF", "agencyName": "string | null" } ],
  "raisedBy": "CUSTOMER | WORKER", "category": "string",
  "priority": "HIGH | MEDIUM | LOW",
  "slaDueAt": "ISO-8601 UTC", "slaSecondsRemaining": 0,
  "disputeStatus": "OPEN | IN_REVIEW | RESOLVED | DISMISSED",
  "autoCancelled": false
}
```
`priority` is derived, never stored (see **D3**). `autoCancelled = true` marks the dispute created by the system after a customer-absent fee (Q10), shown as "Auto-Cancelled Dispute" in the console.

## 2. Endpoints

### 2.1 Customer and Worker file a dispute (BE-M6-02, MOB-M6-03)

**`POST /api/customers/me/disputes`** (policy `CustomerOnly`) and **`POST /api/workers/me/disputes`** (policy `WorkerOnly`) -> 201 `data: Dispute`
```json
{ "orderId": 0, "category": "string", "description": "string", "evidenceUrls": ["string"] }
```
`raisedBy` comes from the caller's role, never from the body. The caller must be on the order (`JOB_ORDER.customer_id` for a customer, an assignment of the order for a worker); otherwise 404.

| Status | Condition |
|---|---|
| 400 | `description` empty or longer than 1000; `category` not in the allowed set (**D1**); `evidenceUrls` empty (PRD §4.3 step 1: "kèm hình ảnh bằng chứng") or more than 10 entries, or an entry that is not a URL issued by `IFileStorage` |
| 404 | order missing or caller not on it |
| 409 | more than 24 h after the end of the shift (**D4**); the order has no assignment that reached `COMPLETED`/`AWAITING_ACCEPTANCE`/`ABSENT` (nothing to dispute); an open dispute by the same role already exists for this order (**D5**) |

Effects: `dispute_status = OPEN`, `sla_due_at = created_at + 48 h` (drawio note: `sla_due_at = +48 h`; PRD says SLA 24-48 h, see **D3**). The customer-absent fee dispute window (Q10: 24 h after the fee, `Absence.CustomerDisputeHours`) is the same endpoint with `category = ABSENT_FEE`.

**`GET /api/customers/me/disputes`** and **`GET /api/workers/me/disputes`** -> 200 `data: Dispute[]` (own disputes only), newest first. **`GET .../disputes/{disputeId}`** -> 200 `data: Dispute`, 404 if not the caller's. The party sees the verdict (`faultParty`, `compensationAmount`) once `RESOLVED`.

### 2.2 Admin queue and case file (WEB-M6-01) — policy `AdminOnly`

**`GET /api/admin/disputes?status=&priority=&nearSla=&page=&pageSize=`** -> 200 `data: { items: DisputeSummary[], page, pageSize, total }`. Default `status = OPEN,IN_REVIEW`, sorted by `slaDueAt` ascending. `nearSla = true` keeps only unresolved disputes with `slaDueAt - now <= Admin.DisputeNearSlaHours` (see `admin.md` **A2**; the dashboard uses this filter). `pageSize` max 100, default 20. Unresolved total is what the dashboard shows as "Tranh chấp tồn".

**`GET /api/admin/disputes/{disputeId}`** -> 200
```json
{
  "dispute": { "...Dispute" },
  "summary": { "...DisputeSummary" },
  "shiftTimeline": [ { "at": "ISO-8601 UTC", "type": "CHECK_IN | PHOTO_AFTER | CUSTOMER_DISPUTED | CHECK_OUT", "detail": "string", "volScore": 0.0, "gpsVerified": true, "distanceM": 0.0 } ],
  "checklist": { "done": 0, "total": 0 },
  "photos": [ { "phase": "BEFORE | AFTER", "angleNo": 0, "url": "string", "volScore": 0.0, "isAccepted": true } ]
}
```
`shiftTimeline`, `checklist` and `photos` are **read from other modules** (check-in log: M3, job photos: M4) and need a read port that does not exist yet: see **D6**. The admin sees the customer's description and `evidenceUrls` from this module directly.

### 2.3 Admin handles a case (BE-M6-02)

**`POST /api/admin/disputes/{disputeId}/take`** -> 200 `data: Dispute` (`OPEN -> IN_REVIEW`, records `resolved_by` = the admin as the handler). 409 if not `OPEN`.

**`POST /api/admin/disputes/{disputeId}/resolve`** -> 200 `data: Dispute`
```json
{ "faultParty": "FREELANCER | AGENCY | CUSTOMER | null", "compensationAmount": 0, "lockWorker": false, "note": "string" }
```

| Status | Condition |
|---|---|
| 400 | `note` empty or longer than 255; `compensationAmount` < 0 or > the `gross_amount` sum of the order's assignments; `lockWorker = true` with `faultParty` other than `FREELANCER`; `faultParty = null` with `compensationAmount` > 0 |
| 404 | missing |
| 409 | already `RESOLVED`/`DISMISSED` (idempotent guard: a second call changes nothing) |

`faultParty = null` dismisses the dispute (`DISMISSED`); otherwise `RESOLVED`. All effects run in one DB transaction, then `DisputeResolved` is published:

| `faultParty` | Effect |
|---|---|
| `FREELANCER` (Q22 D3, PRD §4.3) | Customer is compensated first (Principle 0, Q09): `IRefundService` refunds `compensationAmount` to the customer. The same amount becomes a **pending deduction** on the worker's next payout (`PAYOUT_ITEM.penalty_amount`, applied by `payouts.md` §2.1). `lockWorker = true` locks the worker account through the Workers module event handler (PRD: "khóa tài khoản cảnh cáo"). |
| `AGENCY` (Q09, PRD §4.3) | Order of deduction: (1) refund the customer 100% of the affected amount, (2) actual rescue-worker cost, (3) platform fee. `ISlaPenaltyService` subtracts `QUALITY_COMPLAINT` points (Q09: **only because the dispute is upheld**, `-5`) and the escrow deduction (customer refund + rescue cost). Shortfall and suspension follow Q09. The Disputes module never writes escrow or SLA tables itself. |
| `CUSTOMER` (dispute raised by a worker, or a false customer claim) | No money moves to the customer. If the dispute was about an absence fee (`ABSENT_FEE`) and the customer lost, nothing is refunded. A **false absence claim** is recorded as an upheld dispute with the worker's side as `faultParty` (Q10). Platform fee charge to the customer: see **D7**. |
| `null` | Dismissed, no effect. |

`compensationAmount` and every refund use `Vnd` (G-2). The `resolved_by` admin, `resolved_at` (UTC, `IClock`) and `note` are recorded; the note is stored in `ADMIN_AUDIT_LOG` as `entity_type = DISPUTE_TICKET`, `field_name = dispute_status`, `reason = note` (**D8**).

Absence-fee reversal (Q10): if the customer disputes an absence fee within 24 h and wins (`faultParty` = the worker's side, `category = ABSENT_FEE`), the 40% fee is refunded to the customer and deducted from the worker's next payout (`PAYOUT_ITEM.penalty_amount`) the same way as `FREELANCER`.

## 3. Events
- **Handles** `CustomerAbsentApproved` (published by this team's Admin module): tells the customer, through `INotificationService`, that they can dispute the fee within `Absence.CustomerDisputeHours` (24, Q10). No row is created.
- **Publishes** `DisputeResolved { disputeId, orderId, faultParty, compensationAmount, lockWorker }` (consumers: M5 `ISlaPenaltyService` bookkeeping, M2 refund bookkeeping, M4 worker lock, M6 Payouts). Idempotent: consumers key on `disputeId`.

## 4. Open questions (not covered by PRD or decisions; recommended default in bold)

| # | Question | Recommended default |
|---|---|---|
| **D1** | `category VARCHAR(15)` allowed values. PRD lists "hỏng đồ, mất vệ sinh, thái độ"; Figma shows "Chất lượng kém / Thái độ / Hư hỏng tài sản / Phí vắng mặt tự động". | **`QUALITY`, `ATTITUDE`, `PROPERTY_DAMAGE`, `ABSENT_FEE`, `OTHER`.** |
| **D2** | `DISPUTE_TICKET` has `order_id` only, but fault is per worker (a two-worker order could mix a Freelancer and an Agency worker). | **One verdict per dispute; it applies to every assignment of the order. A mixed-fault order is out of the MVP.** If the leader wants per-assignment disputes, M1 adds an `assignment_id` column (schema change). |
| **D3** | PRD says SLA 24-48 h by priority; drawio says `sla_due_at = +48 h`; Figma shows High/Medium/Low priority. No rule for priority. | **`sla_due_at = created_at + 48 h` for all; `priority` derived from remaining time: HIGH < 6 h, MEDIUM < 24 h, else LOW** (thresholds are config keys `Disputes.PriorityHighHours`, `Disputes.PriorityMediumHours`, proposed, not decided). |
| **D4** | "Within 24 h after the shift" (PRD §4.3): measured from `completed_at` or the shift end? A customer can dispute before completion ("Từ chối & Khiếu nại" during acceptance, Figma timeline). | **Allowed from check-in until `max(completed_at, shift end) + Dispute.FileWindowHours` (24).** |
| **D5** | Duplicate disputes; auto-created dispute for the absence fee ("Auto-Cancelled Dispute" in Figma). | **At most one unresolved dispute per `(order_id, raised_by)`; the system does not auto-create disputes, the Figma tag is shown only when `category = ABSENT_FEE`.** |
| **D6** | Case file needs check-in log (M3), photos/VoL (M4) and checklist: modules may not read each other's tables. | **New read port `IDisputeEvidenceQuery` returning timeline, checklist and photos by `orderId` (implementer M3/M4 or M1 via the shared flat `JOB_ASSIGNMENT` data), added through a Scope exception on BASE-03.** Until then Fake. |
| **D7** | If the customer is at fault, may the platform charge a fee? PRD only defines the 40% absence rule. | **No extra charge (nothing is invented); `CUSTOMER` fault is only recorded.** |
| **D8** | G-5 audits Admin-editable money parameters only; a verdict moves money. | **Also write one `ADMIN_AUDIT_LOG` row per verdict (cheap, append-only).** |

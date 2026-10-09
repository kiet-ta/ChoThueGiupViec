# Contract: Booking (module `Booking`, owner M2)

> Status: **DRAFT, awaiting leader approval** (ticket BE-M2-00, issue #181). Nobody codes an endpoint that is not in an approved contract.
> Sources: `.spec/spec.md` §1.1, §1.2, §2.1, §2.5, §2.6, §4.1, BR-01/02/03/08/10, §5.2 · `.spec/decisions.md` G-2, G-3, G-4, G-5, G-7, Q01, Q04, Q07, Q13, Q15, Q21 C1, Q22 D2/D7 · `.spec/plan/00-overview.md` §4 (ports), §5 (events), §6 (entities) · `Backend/GiupViec_Physical_DB_MVP5.drawio` tables `JOB_ORDER`, `JOB_ORDER_EXTENSION`, `PRICE_RULE` (SC-2) · entities `Backend/Domain/Entities/JobOrder*.cs`, `JobOrderExtension.cs`, `PriceRule.cs`, enums `JobOrderStatus`, `ServiceTier`, state machine `Backend/Domain/StateMachines/JobOrderStateMachine.cs`.
> Implements tickets: BE-M2-01, BE-M2-02, BE-M2-02a, BE-M2-03, BE-M2-08, BE-M2-09, BE-M2-10. UI: MOB-M2-01, MOB-M2-02, MOB-M2-04, MOB-M2-05, MOB-M2-06, WEB-M2-01. Payment, IPN, reconciliation and refunds: `payments.md`.
> Conventions (envelope `ApiResponse<T>`, camelCase, UTC ISO-8601, status codes, roles, policies `CustomerOnly` / `AdminOnly`, 404-for-not-owned, validation shape O5) are defined in `identity.md` §1 and apply here unchanged.
> Items marked **Bx** are not decided by the PRD or `decisions.md`; they are listed in section 6 with a recommended default. An endpoint whose rule depends on an open **Bx** waits for the leader's answer.

## 1. Shared values

### 1.1 Order status (`JOB_ORDER.order_status`, shared with M1/M3/M6)
Exactly the members of `JobOrderStatus` (no new status, BASE-06). Wire value = DB value:

| Value | Meaning | Set by |
|---|---|---|
| `PENDING_PAYMENT` | created, QR not paid yet (initial) | Booking, on create |
| `PAID` | IPN or reconciliation confirmed the payment | Payments (`payments.md` §3.2) |
| `DISPATCHING` | dispatch is looking for worker(s), or a seat was lost and is re-dispatched (Q22 D7) | Booking, see **B4** |
| `ASSIGNED` | `required_workers` assignments accepted (Q22 D7) | Booking, from assignment statuses (`JobOrder.SyncWithAssignments`) |
| `COMPLETED` | `required_workers` assignments `COMPLETED` (Q22 D7) | idem |
| `CANCELLED` | unpaid QR expired (Q04), customer cancel (Q15), no worker within 10 km (BR-03), incident without substitute (BR-10) | Booking / Payments |

Allowed transitions are exactly those of `JobOrderStateMachine` (PENDING_PAYMENT -> PAID | CANCELLED; PAID -> DISPATCHING | CANCELLED; DISPATCHING -> ASSIGNED | CANCELLED; ASSIGNED -> DISPATCHING | COMPLETED | CANCELLED). Any other transition is **409**. Only the Booking/Payments modules write `JOB_ORDER` (overview §3.5); other modules react through events (§5).

### 1.2 Shift codes (Block Slots, PRD §2.6)
Same codes as `workers.md` §2.3 (`BOOKING_SLOT.shift_code`), local time `Asia/Ho_Chi_Minh` (G-3):

| `shiftCode` | Local time |
|---|---|
| `SHIFT_MORNING` | 08:00-12:00 |
| `SHIFT_AFTERNOON` | 13:00-17:00 |
| `SHIFT_EVENING` | 17:30-20:30 |

"Shift start" in this contract = `scheduledDate` + start time above, converted to UTC by `IClock` only (G-3). See **B1** (the Dispatch code currently uses other codes).

### 1.3 Service tier, area bracket, workers (Q01, BR-01/02)
- `serviceTier`: `ECONOMY` | `PREMIUM` (enum `ServiceTier`).
- `areaBracket` from the address `totalAreaM2`: `UP_TO_30` (<= 30), `FROM_31_TO_80` (> 30 and <= `Area.StandardMaxM2`), `OVER_80` (> 80).
- `requiredWorkers`: `totalAreaM2 <= Area.StandardMaxM2` (80) -> 1; `> 80` -> **exactly 2** (BR-02, Q01; never 3+).
- `unitPrice` = active `PRICE_RULE` row for `(serviceTier, areaBracket)`, per worker per shift (`Shift.MaxHours` = 4).
- `totalAmount` = `unitPrice x requiredWorkers`, whole VND (`Vnd.Round`, G-2). Frozen in `JOB_ORDER.total_amount` at creation (Q01 price snapshot). Each future assignment's `gross_amount` = `unitPrice`.
- No add-on/service surcharge (Q01). The chosen services + note are stored as text only (`customer_note`).
- Boundaries tested by BE-M2-01/02: 30.00 -> `UP_TO_30`, 30.01 -> `FROM_31_TO_80`, 80.00 -> 1 worker / `FROM_31_TO_80`, 80.01 -> 2 workers / `OVER_80`.

## 2. Data shapes

`PriceQuote`
```json
{
  "addressId": 0, "serviceTier": "ECONOMY | PREMIUM", "totalAreaM2": 0.0,
  "areaBracket": "UP_TO_30 | FROM_31_TO_80 | OVER_80", "requiredWorkers": 1,
  "unitPrice": 0, "totalAmount": 0, "currency": "VND"
}
```

`Order` (table `JOB_ORDER`)
```json
{
  "orderId": 0, "orderCode": "string", "serviceTier": "ECONOMY | PREMIUM",
  "addressId": 0, "scheduledDate": "2026-10-15", "shiftCode": "SHIFT_MORNING",
  "shiftStartAt": "ISO-8601 UTC", "shiftEndAt": "ISO-8601 UTC",
  "areaSnapshotM2": 0.0, "requiredWorkers": 1, "requiredSkill": "string | null",
  "totalAmount": 0, "orderStatus": "PENDING_PAYMENT", "customerNote": "string | null",
  "cancelReason": "string | null", "paymentDeadlineAt": "ISO-8601 UTC | null",
  "createdAt": "ISO-8601 UTC", "updatedAt": "ISO-8601 UTC"
}
```
- `paymentDeadlineAt` = `createdAt + Payments.QrExpiryMinutes` (15) while `PENDING_PAYMENT`, otherwise `null`.
- `orderCode`: see **B7**. The customer address itself is **not** copied (the app already has it from `customers.md`); only `addressId` and `areaSnapshotM2`.

`OrderSummary` (history list): `orderId, orderCode, serviceTier, scheduledDate, shiftCode, requiredWorkers, totalAmount, orderStatus, createdAt`.

`OrderProgress` (BE-M2-10, read from `JOB_ASSIGNMENT WHERE order_id = :id`, 0 JOIN, PRD §5.2)
```json
{
  "orderId": 0, "orderStatus": "ASSIGNED", "requiredWorkers": 2,
  "assignments": [
    {
      "assignmentId": 0, "assignmentSeq": 1, "workerId": 0,
      "workerName": "string | null", "workerRatingAvg": 0.0,
      "assignmentStatus": "OFFERED | ASSIGNED | CHECKED_IN | IN_PROGRESS | AWAITING_ACCEPTANCE | COMPLETED | CANCELLED | CANCELLED_BY_WORKER | ABSENT | INCIDENT | REASSIGNED",
      "acceptedAt": "ISO-8601 UTC | null", "completedAt": "ISO-8601 UTC | null"
    }
  ],
  "extension": "Extension | null"
}
```
- Only assignments **not** in `OFFERED`, `CANCELLED`, `REASSIGNED` are listed (a customer never sees who was merely offered the job, Q22 D1).
- `workerName` / `workerRatingAvg` come from the `IWorkerProfileQuery` port (M4, Q21 C3), never from the `WORKER` table. No worker phone here: phone visibility is Dispatch's rule (Q17, `dispatch.md`).

`Extension` (table `JOB_ORDER_EXTENSION`)
```json
{
  "extensionId": 0, "orderId": 0, "workerId": 0, "extraHours": 0.0, "extraAmount": 0,
  "extStatus": "PENDING_PAYMENT | PAID | ACCEPTED | DECLINED | EXPIRED",
  "workerDecision": "PENDING | ACCEPTED | DECLINED",
  "requestedAt": "ISO-8601 UTC", "decidedAt": "ISO-8601 UTC | null"
}
```
Value lists for `ext_status` / `worker_decision`: see **B5** (the columns are strings without a defined list).

`PriceRule` (table `PRICE_RULE`, SC-2)
```json
{ "ruleId": 0, "serviceTier": "ECONOMY", "areaBracket": "UP_TO_30", "unitPrice": 0, "isActive": true, "updatedAt": "ISO-8601 UTC", "updatedBy": 0 }
```

## 3. Customer endpoints (`CustomerOnly`, base `/api/booking`)

### 3.1 `GET /api/booking/options`
Static booking choices so the app hard-codes nothing (G-4). 200 `data`:
```json
{
  "serviceTiers": ["ECONOMY", "PREMIUM"],
  "shifts": [ { "shiftCode": "SHIFT_MORNING", "startLocal": "08:00", "endLocal": "12:00" } ],
  "premiumMinLeadHours": 4, "shiftMaxHours": 4, "standardMaxAreaM2": 80,
  "paymentQrExpiryMinutes": 15, "sandbox": true
}
```
(values from `BusinessRules`: `Premium.MinLeadHours`, `Shift.MaxHours`, `Area.StandardMaxM2`, `Payments.QrExpiryMinutes`; `sandbox` is always `true` in the MVP, G-1.)

### 3.2 `GET /api/booking/price-quote?addressId=&serviceTier=` (BE-M2-02, MOB-M2-02)
Shows the fixed price **before** paying (PRD §4.1, Q01). 200 `data`: `PriceQuote`. Read only, creates nothing; the price is only frozen by 3.3.

| Status | Condition |
|---|---|
| 400 | missing/invalid `addressId` or `serviceTier` |
| 404 | address does not exist or is not the caller's |
| 500 | no active `PRICE_RULE` row for the pair (seed data broken; never guessed) |

### 3.3 `POST /api/booking/orders` (BE-M2-01, BE-M2-02, BE-M2-03)
Request
```json
{
  "addressId": 0, "serviceTier": "ECONOMY | PREMIUM",
  "scheduledDate": "2026-10-15", "shiftCode": "SHIFT_MORNING",
  "customerNote": "string | null", "requiredSkill": "string | null"
}
```
Response **201** `data`: `Order` (`orderStatus = PENDING_PAYMENT`). The client then asks for the QR (`payments.md` §2.1).

Rules (in this order; nothing is written when one fails):
1. Validation (400, O5): `addressId` > 0; `serviceTier` and `shiftCode` from §1.2/§1.3; `scheduledDate` a date; `customerNote` <= 500 chars (column `NVARCHAR(500)`); `requiredSkill` <= 100 chars and only allowed for `PREMIUM` (see **B6**).
2. The address must belong to the caller (404 otherwise), read through a port, never from `CUSTOMER_ADDRESS` (`customers.md` §3; see **B2**). `areaSnapshotM2` = its `totalAreaM2` at this moment.
3. Shift start must be in the future (409 `SHIFT_IN_PAST`). See **B3** for an Economy lead time / booking horizon.
4. `PREMIUM`: shift start >= now + `Premium.MinLeadHours` (4) (Q13) else **409** `PREMIUM_LEAD_TIME`.
5. `requiredWorkers` and `totalAmount` from §1.3 (BE-M2-01 pure function + BE-M2-02 price lookup).
6. `PREMIUM` phase 1 (PRD §2.5, Q13): `IAgencyCapacityService.TryReserveAsync(date, shiftCode, requiredWorkers)` holds the capacity atomically. `null` -> **409** `FULLY_BOOKED`, no order. The hold is released if the order is cancelled or its QR expires; it becomes final when the order is paid. Where the reservation id is kept: **B8**.
7. Insert `JOB_ORDER` with `order_status = PENDING_PAYMENT`, `created_at = updated_at = now` (UTC, `IClock`).

| Status | Condition |
|---|---|
| 400 | validation (O5) |
| 404 | address not found / not owned |
| 409 | `data.code` = `SHIFT_IN_PAST` \| `PREMIUM_LEAD_TIME` \| `FULLY_BOOKED` |

Business 409s carry `data: { "code": "<CODE>" }` so clients branch on the code, never on `message` (same pattern as `admin.md` `failedCriteria`).

### 3.4 `GET /api/booking/orders?status=&page=&pageSize=` (BE-M2-10, MOB-M2-06)
Caller's history from `JOB_ORDER WHERE customer_id = :me` (0 JOIN), newest `createdAt` first. `status` optional, one value of §1.1. `pageSize` default 20, max 100. 200 `data: { items: OrderSummary[], page, pageSize, total }`. 400 on an unknown `status`.

### 3.5 `GET /api/booking/orders/{orderId}` (MOB-M2-04)
200 `data`: `Order`. 404 if not the caller's.

### 3.6 `GET /api/booking/orders/{orderId}/progress` (BE-M2-10, MOB-M2-04)
200 `data`: `OrderProgress`. 404 if not the caller's. Realtime: the app subscribes to SignalR topic `job.tracking` on `/hubs/notifications` (Q07); every order status change sends `NotificationMessage { RecipientRole = Customer, Topic = "job.tracking", Data = { orderId, orderStatus } }` and the app re-reads this endpoint. Polling this endpoint is the fallback. Overlap with `GET /api/dispatch/orders/{orderId}/status`: **B9**.

### 3.7 `POST /api/booking/orders/{orderId}/cancel` (BE-M2-09, Q15)
Request `{ "reason": "string (1-255)" }`. Response 200 `data`: `Order` (`CANCELLED`, `cancelReason` = reason).

| Order status at cancel | Money (Q15) |
|---|---|
| `PENDING_PAYMENT` | nothing was paid; the open QR transaction becomes `EXPIRED` (`payments.md`); Premium hold released |
| `PAID`, `DISPATCHING` (no assignment accepted yet) | **100 %** refund of the order payment |
| `ASSIGNED`, shift start more than `Cancel.FullRefundHoursBefore` (2) h away | **100 %** refund |
| `ASSIGNED`, shift start within 2 h | per accepted assignment, `Cancel.LateFeeRate` (0.40) x `gross_amount` is kept as worker compensation, the rest refunded: **waits for B10** |
| `COMPLETED`, `CANCELLED` | **409** `INVALID_STATE` |

- Refunds go through the Payments module (`payments.md` §3.4); `OrderCancelled` is published after the commit (M3 cancels the open offers/assignments, M5 releases capacity), then `OrderRefunded` when money moved.
- Plan scope of BE-M2-09 is "before a worker is assigned"; the `ASSIGNED` rows are listed so the rule is complete, but the late-fee row is not implemented before **B10** is answered.

| Status | Condition |
|---|---|
| 400 | `reason` empty or > 255 |
| 404 | order not found / not owned |
| 409 | `INVALID_STATE` (see table) |

### 3.8 `POST /api/booking/orders/{orderId}/extensions` (BE-M2-08, BR-08, "Làm lần 2")
Request `{ "assignmentId": 0, "extraHours": 0.0 }`. Response **201** `data`: `Extension` (`extStatus = PENDING_PAYMENT`, `workerDecision = PENDING`). The client then asks for its QR (`payments.md` §2.2).

Rules:
- `assignmentId` must be an assignment of this order in `IN_PROGRESS` or `AWAITING_ACCEPTANCE` (the worker is on site); its `worker_id` becomes `JOB_ORDER_EXTENSION.worker_id`.
- `extraAmount = Vnd.Round(unitPrice / Shift.MaxHours x extraHours)` using the **frozen** unit price of the order (`totalAmount / requiredWorkers`), Q01. Allowed `extraHours`: **B11**.
- At most **one** extension per order: `JOB_ORDER_EXTENSION.order_id` is UNIQUE in the schema -> a second request is **409** `EXTENSION_EXISTS`.
- After payment the Payments module publishes `ExtensionPaid`; M4 asks the worker (`workers.md` §2.6.1). Decline -> `ExtensionDeclined` -> 100 % refund of the extension (`payments.md` §3.4) and, per BR-08, a new worker for the next shift (M3).

| Status | Condition |
|---|---|
| 400 | validation (O5) |
| 404 | order or assignment not found / not owned |
| 409 | `INVALID_STATE` (order not `ASSIGNED`, assignment not on site) \| `EXTENSION_EXISTS` |

## 4. Admin endpoints (`AdminOnly`, BE-M2-02a, WEB-M2-01)

### 4.1 `GET /api/admin/price-rules`
200 `data: PriceRule[]` (all 6 rows of Q01, ordered by `serviceTier`, `areaBracket`).

### 4.2 `PUT /api/admin/price-rules/{ruleId}`
Request `{ "unitPrice": 0, "reason": "string (1-255)" }`. Response 200 `data`: `PriceRule`.
- `unitPrice` > 0, whole VND (no decimals), <= 9,999,999,999,999,999 (`DECIMAL(18,2)`). `reason` required, non-empty (Q01, G-5).
- In **one DB transaction** (G-5): update `unit_price`, `updated_at = now`, `updated_by = <admin id>` and write one `ADMIN_AUDIT_LOG` row through `IAuditLog`: `actorType = ADMIN`, `adminId`, `entityType = PRICE_RULE`, `entityId = <ruleId>`, `fieldName = unit_price`, `oldValue`, `newValue`, `reason`.
- Takes effect for **new** orders only; existing `JOB_ORDER.total_amount` never changes (Q01).
- Same price as the current one -> 200, nothing written (no empty audit row).

| Status | Condition |
|---|---|
| 400 | validation (O5) |
| 404 | `ruleId` unknown |

### 4.3 Price change history (read-only)
No new endpoint: the Admin UI reads `GET /api/admin/audit-logs?entityType=PRICE_RULE&entityId=&page=&pageSize=` (`admin.md` §2.4, owner M6), which already returns who, when, old/new value and reason. Booking does not read `ADMIN_AUDIT_LOG` (M6's table). If the leader wants a Booking-owned endpoint instead: **B12**.

## 5. Events
Records are those of `Backend/Domain/Events/*.cs` (BASE-04); this contract does not change them.

- **Publishes** `OrderCancelled { OrderId, CustomerId, Reason, CancelledAtUtc }` after every cancel commit (customer cancel, QR expiry, `AssignmentFailed`, incident without substitute).
- `PAID -> DISPATCHING`: who and when is **B4** (recommended: in the same transaction that marks the order `PAID`, before `OrderPaid` reaches Dispatch).
- **Handles** `JobAssigned`, `JobCompleted` (M3/M4) and the seat-lost cases: re-reads the order's assignment statuses and applies `JobOrder.SyncWithAssignments` (Q22 D7), then notifies `job.tracking`. Seat-lost signal: **B4**.
- **Handles** `AssignmentFailed { OrderId, Reason, FailedAtUtc }` (M3, BR-03 no worker within 10 km, BR-10 no substitute): order -> `CANCELLED` (`cancel_reason = Reason`) and a **100 %** refund (`payments.md` §3.4). Idempotent: an order already `CANCELLED` changes nothing and is not refunded twice.
- **Handles** `ExtensionDeclined` (M4): extension `extStatus = DECLINED`, `decidedAt`; refund in `payments.md` §3.4.
- **Does not refund** on `CustomerAbsentApproved` or `DisputeResolved`: the Admin and Disputes modules already call `IRefundService` themselves (`admin.md` §2.2 "double-refund risk", `disputes.md` §2.3). Booking only notifies `job.tracking`.

## 6. Open questions (B1-B12) - recommended default, leader decides

| # | Question | Recommended default |
|---|---|---|
| **B1** | Shift codes differ: `workers.md` §2.3 uses `SHIFT_MORNING / SHIFT_AFTERNOON / SHIFT_EVENING`, the Dispatch code and tests use `SANG / CHIEU / TOI` (e.g. `Backend/Application/Features/Dispatch/MilestoneFailoverService.cs:317`). `JOB_ORDER.shift_code` and `BOOKING_SLOT.shift_code` must match or no slot is ever found. | Use the `workers.md` codes everywhere; M3 aligns its code in a fix ticket. One constant list owned by M1 (Domain) would avoid a third spelling. |
| **B2** | Booking may not read `CUSTOMER_ADDRESS` (`customers.md` §3) but needs ownership, `totalAreaM2` and coordinates. No such port exists in `Application/Interfaces/Ports`. | New read port `ICustomerAddressQuery.GetOwnedAsync(customerId, addressId)` -> `{ addressId, totalAreaM2, latitude, longitude }` or null; implementer M1, Fake first; via Scope exception on BASE-03. |
| **B3** | Economy has no lead time or horizon in the PRD/decisions. | Economy: shift start must be in the future only; booking horizon 14 days for both tiers as a new key `Booking.MaxDaysAhead` (14) - **suggestion**, needs a `decisions.md` entry. |
| **B4** | Who moves `PAID -> DISPATCHING`, and how does Booking learn a seat was lost (worker cancel, incident, reassignment, Q22 D7)? No event carries those. | Booking sets `DISPATCHING` in the same transaction that publishes `OrderPaid` (`PAID` is then only visible in history). For seat loss M3 publishes a new event `AssignmentSeatLost { AssignmentId, OrderId, Reason }` (BASE-04 Scope exception) or Booking re-syncs on `IncidentReported`. |
| **B5** | `ext_status` / `worker_decision` value lists are not defined anywhere (columns are free strings). | `ext_status`: `PENDING_PAYMENT`, `PAID`, `ACCEPTED`, `DECLINED`, `EXPIRED`; `worker_decision`: `PENDING`, `ACCEPTED`, `DECLINED` (matches `workers.md` §2.6.1 `DECLINED`). M1 adds enums. |
| **B6** | `requiredSkill` (`JOB_ORDER.required_skill`, PRD §2.5 skill label) - free text or a Skill catalog id (M5)? | Premium only, a skill **name** from M5's catalog, validated through M5 when its contract exists; until then optional and not validated. |
| **B7** | `order_code` format (`VARCHAR(20)`, UNIQUE) is not defined. | `GV` + `yyMMdd` (local date) + 6 random uppercase alphanumerics, e.g. `GV261015K3P9QZ` (14 chars); retry on UNIQUE clash. |
| **B8** | `IAgencyCapacityService` returns a `ReservationId` that must survive until payment/expiry, but `JOB_ORDER` has no column for it. | M5 keys the reservation by `orderId` (new port parameter) so Booking stores nothing; otherwise a new nullable column `JOB_ORDER.capacity_reservation_id` (M1 schema change). |
| **B9** | `GET /api/dispatch/orders/{orderId}/status` (`dispatch.md` §3.2, draft) overlaps with §3.6. | Keep both: §3.6 is the order view (status + assignments); Dispatch's endpoint covers only the live search (radius, offers). The app's tracking screen reads §3.6. |
| **B10** | Q15 late cancel (<= 2 h, `ASSIGNED`): the 40 % goes to the worker, but `JOB_ASSIGNMENT` only has `absence_fee_amount` (BR-05), and refund of 60 % needs a partial refund record (`payments.md` P2). | Reuse no column for a different meaning; M1 adds `JOB_ASSIGNMENT.cancel_fee_amount DECIMAL(18,2) NULL`, M3 sets the assignment to `CANCELLED`. Not implemented until answered. |
| **B11** | Allowed `extraHours` for "Làm lần 2" (column `DECIMAL(3,1)`). Also `ExtensionPaid.ExtraHours` is `int` and `ExtensionId` is `long` while the entity has `decimal` / `int`. | `extraHours` from 0.5 to `Shift.MaxHours` (4) in 0.5 steps; M1 changes `ExtensionPaid.ExtraHours` to `decimal` (BASE-04 Scope exception). |
| **B12** | Q01 says "Admin can list the change log"; plan BE-M2-02a says "endpoint xem lịch sử". | Reuse M6's `GET /api/admin/audit-logs?entityType=PRICE_RULE` (§4.3); no duplicate endpoint. |

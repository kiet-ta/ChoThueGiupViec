# Contract: Agencies (module `Agencies`, owner M5)

> Status: **DRAFT — proposed for leader approval** (ticket BE-M5-00, issue #231). No endpoint in this document is approved for implementation until the leader approves this contract.
> Sources: `.spec/spec.md` §1.3, §2.1, §2.3–§2.5, §2.8, §4.3–§4.4, §5.1–§5.4; `.spec/decisions.md` G-1–G-7, Q08, Q09, Q11, Q16, Q18, Q19, SC-1, SC-3, SC-4; `.spec/plan/00-overview.md` §4–§6; `.spec/contracts/identity.md` §1.
> Implements: BE-M5-01..09 and relevant Partner Portal work, after contract approval and required gates.

## 1. Conventions and proposal status

- Shared conventions are inherited from `identity.md` §1: base path `/api`; JSON UTF-8; camelCase; UTC ISO-8601 timestamps; `ApiResponse<T>`; Bearer access token; roles `Partner` and `Admin`; policies `PartnerOnly` and `AdminOnly`; common status meanings and validation envelope. A resource outside the caller's ownership returns 404.
- Every route below is a **candidate for leader review**, not a source-backed or approved path. Request/response fields are specified only when their names and meaning are present in an allowed source; otherwise they are called out in §8 rather than guessed.
- Money is VND. Payment gateway behavior is MoMo sandbox only (G-1, Q04); use the `IPaymentGateway` port. Real-money payment and VietQR are out of scope.
- Agency workers are `WORKER` rows with `worker_type = AGENCY_STAFF` and an `agency_id`; Agency workers do not enter the Freelancer eKYC/Admin-review flow (PRD §2.2/§2.8, Q05).

## 2. Candidate capability / endpoint map

The path and method are proposals solely to make the review concrete. The leader must approve or replace them before implementation. Any unresolved request/response shape is listed in §8.

| Capability | Candidate endpoint | Access | Source-backed behavior |
|---|---|---|---|
| Agency registration | `POST /api/agencies/registration` | Access policy and whether registration is self-service: **leader decision required** | Agency is a legal business with business registration, tax ID, and legal representative (PRD §2.1). Partner identity uses `contact_email` + password (Q16). Agency becomes `ACTIVE` only when escrow reaches the minimum (Q09). |
| Current Agency / profile | `GET /api/agencies/me` | `PartnerOnly` candidate | Agency-scoped access; return only the caller's Agency data. Exact profile DTO is open. |
| Subscription packages | `GET /api/agencies/packages` | `PartnerOnly` candidate | Lists the package defaults in §3. Admin edits are audited (G-5, Q08). |
| Buy / renew subscription | `POST /api/agencies/subscriptions` | `PartnerOnly` candidate | Payment via `IPaymentGateway`; successful activation emits `SubscriptionActivated` (overview §4–§5). Payment request/callback shapes are not defined here. |
| Subscription state | `GET /api/agencies/subscriptions/me` | `PartnerOnly` candidate | Shows the caller's subscription and quota state; exact response shape is open. |
| Guarantee PDF | `POST /api/agencies/guarantee` | `PartnerOnly` candidate; must enforce that the authenticated account is the Agency's legal representative or is authorized by them. How that identity/authorization is established is open (Q19). | Upload signed guarantee PDF via `IFileStorage`; retain version in file name and set `guarantee_signed_at` and `guarantee_file_url` (Q19, SC-1). |
| Import workers (dry-run) | `POST /api/agencies/workers/import/dry-run` | `PartnerOnly` candidate | Accept `.xlsx`, validate rows, return per-row errors, write nothing (Q18). Reject when guarantee is absent (Q19). |
| Import workers (commit) | `POST /api/agencies/workers/import/commit` | `PartnerOnly` candidate | Commit all rows or none; reject the whole file for any invalid row or quota overflow (Q18). Creates `AGENCY_STAFF` workers without the Admin review queue (PRD §2.8). |
| Weekly shift roster | `GET /api/agencies/roster` and `PUT /api/agencies/roster` | `PartnerOnly` candidate | Agency assigns its workers to weekly shifts; enforce `UNIQUE(worker_id, slot_date, shift_code)` (PRD §2.6 and §4.4). The exact write method/body is open. |
| Escrow deposit | `POST /api/agencies/escrow/deposits` | `PartnerOnly` candidate | Deposit through MoMo sandbox (Q09). On successful payment, balance changes are append-only `ESCROW_TRANSACTION` records (SC-4). |
| Escrow history / appeal | `GET /api/agencies/escrow/transactions`, `POST /api/agencies/escrow/appeals` | `PartnerOnly` candidate | Agency may appeal within 48 hours; Admin decides manually; reversal restores points/money using a `REVERSAL` row (Q09). Exact appeal payload is open. |
| Agency dashboard | `GET /api/agencies/dashboard` | `PartnerOnly` candidate | Agency-scoped information only. Premium includes roster dashboard, analytics, and priority dispatch under Pro (Q08); exact metrics and filtering are open. |
| Edit packages / decide appeals | Candidate Admin endpoints **not selected** | `AdminOnly` candidate | Package edits require an append-only `ADMIN_AUDIT_LOG` entry in the same transaction (G-5/Q08). Admin decides appeals manually (Q09). Paths and DTOs are open. |

### Candidate request/response shapes

These shapes map existing PRD concepts and schema fields; they remain proposals until approval. No password hash, internal login-lock fields, or another Agency's data is returned.

`POST /api/agencies/registration` request:
```json
{
  "taxCode": "string",
  "legalName": "string",
  "legalRepresentative": "string",
  "contactPhone": "string",
  "contactEmail": "partner@example.com",
  "password": "string"
}
```
Known validation: required business identity/contact fields; `taxCode` is unique (schema, max 14 chars); password follows Q16 (minimum 10 characters with uppercase, lowercase, and a digit). Exact tax-code/phone/email format rules and whether this public registration route is permitted remain open.

`GET /api/agencies/packages` candidate item: `packageId, packageCode, packageName, tier, billingCycle, price, workerQuota, commissionRate, hasRosterDashboard, hasAnalytics, priorityDispatch, isActive` (camelCase mappings of the existing package schema). Only active package visibility and public-vs-Partner access are unresolved.

`POST /api/agencies/subscriptions` candidate request:
```json
{ "packageId": 0 }
```
Activation is asynchronous through the existing payment flow; payment-initiation response/callback details are not defined by this contract. Candidate subscription response fields: `subscriptionId, agencyId, packageId, startDate, endDate, subStatus, autoRenew`.

`POST /api/agencies/escrow/deposits` candidate request:
```json
{ "amount": 0 }
```
The amount is VND; propose rejecting non-positive or fractional-VND amounts with **400** and the shared validation envelope (G-2 whole-VND rule; positivity is a candidate validation for leader approval). Payment result and callback fields must follow the existing payment contract, not a new provider shape. Candidate transaction response fields are the SC-4 ledger columns, scoped to the authenticated Agency.

`PUT /api/agencies/roster` candidate request:
```json
{
  "entries": [
    { "workerId": 0, "slotDate": "2026-10-15", "shiftCode": "TBD" }
  ]
}
```
Each entry represents a worker's date/shift roster assignment and must respect `UNIQUE(worker_id, slot_date, shift_code)`. Accepted `shiftCode` wire values and week boundaries are open.

`POST /api/agencies/workers/import/dry-run` and `/commit` accept the approved `.xlsx` template. Candidate dry-run response contains per-row validation results; exact template columns and error DTO must be approved before implementation (Q18).

`GET /api/agencies/dashboard` candidate data may expose the authenticated Agency's existing `agencyStatus`, `escrowDepositBalance`, `slaScore`, `workerQuota`, and `isVerifiedPartner`, plus subscription state. The dashboard's response envelope is shared; analytics metrics and fields are not specified and require leader approval.

Candidate Admin package edit (route/method TBD) may change `price`, `workerQuota`, and/or `commissionRate`. It must include a non-empty `reason` (M5 plan; `IAuditLog` rejects empty Admin reasons), and the audit append occurs in the same transaction (G-5). Empty/missing reason is a candidate **400** with the shared validation envelope. Exact route, payload semantics, and other field validation remain open.

## 3. Subscription package rules (Q08, Q11)

The following are **DEFAULT seed values**, not permanent business truths. Packages are rows in `SUBSCRIPTION_PACKAGE`; Admin may edit them, with an audit record (G-5).

| `package_code` | `tier` | `billing_cycle` | `price` (VND) | `worker_quota` | `commission_rate` | Roster dashboard / analytics / priority dispatch |
|---|---|---|---:|---:|---:|---|
| `FREE` | `FREE` | none | 0 | 3 | 0.200 | no / no / no |
| `PRO_MONTHLY` | `PRO` | `MONTHLY` | 2,000,000 | 50 | 0.000 | yes / yes / yes |
| `PRO_QUARTERLY` | `PRO` | `QUARTERLY` | 5,400,000 | 50 | 0.000 | yes / yes / yes |

- Subscription expiry starts a 7-day grace period; the Agency retains Pro during grace. Afterwards it falls back to Free limits. Workers above quota are locked, most recently created first, and are never deleted. Already `ASSIGNED` jobs continue (Q08).
- The PRD names a Verified Partner label as a Pro-tier capability, and `PARTNER_AGENCY.is_verified_partner` stores the label state. Q08 and `SUBSCRIPTION_PACKAGE` define no package flag or rule that automatically changes this Agency field. Whether Pro activation grants/revokes the label and any verification criteria require leader approval; it is not treated as a package entitlement in the table above.
- An Agency's assignment commission is the active package's `commission_rate`, copied to the assignment at creation and unchanged later (Q11).
- A successful subscription activation emits the existing `SubscriptionActivated` event with `SubId`, `AgencyId`, `PackageId`, `PackageCode`, `StartDateUtc`, and `EndDateUtc` (`Backend/Domain/Events/SubscriptionActivated.cs`). Do not change the event shape under this ticket.
- Editing price, commission, or quota requires an audit entry in the same database transaction; `ADMIN_AUDIT_LOG` is append-only (G-5). Use the existing `IAuditLog` port (overview §4); exact package-edit request fields and Admin endpoint are open questions.

## 4. Guarantee and worker import (Q18, Q19)

- The legal representative uploads the signed guarantee PDF. It is stored via `IFileStorage`, with the document version in the filename; persist `guarantee_signed_at` and `guarantee_file_url` (Q19, SC-1).
- Import is from a downloadable `.xlsx` template. The contract must define its columns only after leader approval; each column maps to an existing `WORKER` column (Q18).
- **Dry-run** validates each row, returns per-row errors, and writes nothing.
- **Commit** is all-or-nothing. Any invalid row or quota overflow rejects the entire import; no worker is created on rejection.
- Import is rejected unless `guarantee_signed_at` is set (Q19). Exact HTTP status and error code are an approval question; the shared 4xx envelope follows `identity.md`.
- Successful import creates `WORKER` rows as `AGENCY_STAFF`, linked to the importing Agency, without an Admin review queue (PRD §2.8). Agency workers do not use Freelancer eKYC (Q05).
- The import DTO must not collect a free-text skill tag: skills are selected from the normalized catalog by ID (PRD §2.7). Exact import columns, duplicate detection, row-error DTO and worker profile fields are open questions.

## 5. Roster and capacity boundaries

- Roster is planned by week; workers are assigned to fixed shift slots (PRD §2.3, §2.6, §4.4).
- Slot uniqueness is `UNIQUE(worker_id, slot_date, shift_code)`; a duplicate assignment must not create a second slot (PRD §2.6). Conflict response status/body for this API needs leader approval.
- Shift times are local `Asia/Ho_Chi_Minh`: 08:00–12:00, 13:00–17:00, and 17:30–20:30 (G-3). Persisted timestamps are UTC. Exact wire values for `shift_code` are not specified by the approved source set.
- Premium capacity checks must consider roster and Skill matrix; a matching free slot is locked atomically, and unavailable capacity is reported as fully booked (PRD §2.5). The PRD says lock at payment, while `booking.md` §3.3 proposes a temporary hold at order creation before payment; reconcile the timing before implementation (leader question 12). Detailed capacity/query endpoints belong to the separately scoped M5 capacity work and are not defined by this contract.
- Existing `IAgencyCapacityService` accepts `CapacityRequest(Date, ShiftCode, RequiredWorkers)` and returns a `CapacityReservation(ReservationId, AgencyId, SlotIds)`; it has no required-skill field. The PRD requires matching the requested Skill, so see leader question 10 before implementing capacity matching. Do not change this shared port in this ticket.
- Booking currently reserves before payment, releases the hold on order cancellation or QR expiry, and keeps it on successful payment (`booking.md` §3.3, B8). The port's `ReleaseAsync` requires `ReservationId`, but the shared contracts do not decide whether Booking persists that ID or M5 keys reservations by order ID. Define that handoff, including repeated-release behavior, before implementation; do not infer idempotency or add a new port parameter here.
- M5 consumes the existing `JobCompleted(AssignmentId, OrderId, WorkerId, AgencyId, PayoutAmount, CompletedAtUtc)` and `DisputeResolved(DisputeId, AssignmentId, FaultParty, CustomerRefundAmount, ResolutionNotes, ResolvedAtUtc)` events. Only Agency assignments (`AgencyId` set) and upheld Agency-fault disputes should trigger Agency-specific handling, consistent with Q09/Q22. Do not change these event shapes.

## 6. Escrow and SLA (Q09, SC-4)

Implement the existing `ISlaPenaltyService.ApplyAsync(SlaPenaltyRequest)` port without changing its shape. Its request carries Agency/violation/order/dispute IDs, customer refund, rescue cost, and reason; its result carries points delta, new score, actual escrow deduction, shortfall, and suspension state. The detailed business rules below come from Q09.

- Agency status becomes `ACTIVE` only when `escrow_deposit_balance >= 5,000,000` VND (DEFAULT `Agency.MinEscrowBalance`). The minimum is configurable, not hard-coded.
- SLA starts at 100. Penalties: `NO_SHOW` −20; `SHORTAGE` −10; `QUALITY_COMPLAINT` −5 only when a dispute is upheld. Score `<= 70` triggers a warning; score `< 50` excludes the Agency from Premium dispatch. No automatic score recovery; only an Admin adjustment, audited (Q09).
- Deduction priority: customer refund (100% of affected amount), actual rescue-worker cost, then platform fee. Q09 separately defines the deduction amount as customer refund + rescue cost; whether/how the third item is charged is a leader question.
- Balance cannot go below zero. Deduct available balance, record any shortfall, and set `agency_status = SUSPENDED` until topped up (Q09).
- Appeals are allowed within 48 hours and decided manually by Admin. Reversal restores points/money through a `REVERSAL` transaction. Every balance change is an append-only `ESCROW_TRANSACTION` row (Q09, SC-4).
- `ESCROW_TRANSACTION` source fields: `escrow_txn_id`, `agency_id`, `txn_type` (`DEPOSIT` | `PENALTY` | `REVERSAL`), `amount`, `balance_after`, optional `sla_points_delta`, optional `order_id`, `dispute_id`, `payment_id`, `reason`, optional `created_by_admin_id`, and `created_at` (SC-4). Contract response inclusion and visibility are open questions; never expose another Agency's rows.
- `DisputeResolved` is the overview event consumed by M5; only upheld Agency-fault disputes incur the Q09 quality penalty. Automatic recovery is explicitly deferred (decisions.md §2 deferred).

## 7. Error behavior

Use the shared envelope and general status meanings in `identity.md` §1. Proposed domain conditions that require an error response include:

| Condition | Contract behavior |
|---|---|
| Unauthenticated / non-Partner request to Partner capability | 401 / 403 per identity contract |
| Resource belongs to another Agency | 404, avoiding an ownership leak |
| Invalid request / invalid import row | 400 candidate; error field shape per identity O5. Exact row error shape is open. |
| Duplicate roster slot or conflicting current state | 409 candidate; exact response is open. |
| Worker quota exceeded | Reject whole import; exact status and error DTO are open. |
| Missing signed guarantee | Reject import with a clear error (Q19); exact status and error DTO are open. |
| Unexpected error | 500 with no internal details, per identity contract |

These endpoint-specific status assignments are proposals where not explicitly settled by a source; the leader must approve them.

## 8. Leader questions (must resolve before implementation)

1. Is Agency registration self-service, Admin-created, or invitation-based; which role/policy applies? What exact business-registration, tax-ID, legal-representative, contact and password fields/validation are required, and what is the registration state flow before escrow qualifies the Agency?
2. Approve or replace every candidate method/path in §2. Which registration and package listing capabilities are actually exposed?
3. What exact subscription request/response fields, renewal semantics, payment callback ownership, and `SubscriptionActivated` payload apply? Which route/payload semantics should Admin package edits use beyond the required non-empty `reason` and same-transaction audit?
4. Approve the `.xlsx` template columns and their exact mapping/validation to existing `WORKER` fields. What defines duplicate rows, and what is the per-row error DTO? Which HTTP status/error code covers missing guarantee and quota overflow?
5. What exact roster request/response DTO, `shift_code` wire values, date/week boundaries, and conflict response are required? Can an Agency edit a slot after a job has reserved it?
6. What is the escrow deposit request/callback contract and transaction visibility? Does the platform-fee item in Q09's deduction priority ever debit escrow, given Q09's stated formula excludes it?
7. What does the Agency dashboard include (metrics, date range, filtering, pagination, worker/job detail)? Which fields are returned to Partner users?
8. What exact Agency status values and transition rules are intended beyond `ACTIVE` and `SUSPENDED` stated in Q09?
9. The PRD requires capacity matching by Skill, but the existing `IAgencyCapacityService.CapacityRequest` has no Skill identifier. Does M1 need a scoped port change before capacity implementation, and which approved contract supplies the requested Skill ID?
10. Q19 requires the legal representative to upload the guarantee, but Partner authentication is by Agency contact email. How is the authenticated account linked to or authorized by the legal representative, and how is that requirement enforced?
11. Who persists/associates the capacity `ReservationId` with the order, and what is the required behavior for repeated `ReleaseAsync` calls? Align with booking contract B8 before implementation.
12. Should M5 lock a slot at order creation as a temporary hold (booking contract proposal) or only when payment succeeds (PRD §2.5)?
13. PRD §2.3 describes the Verified Partner label as a Pro capability, but Q08 has no package entitlement flag for it and the schema stores it on `PARTNER_AGENCY`. Does subscription activation set `is_verified_partner`, and what verification criteria/actor govern the flag?

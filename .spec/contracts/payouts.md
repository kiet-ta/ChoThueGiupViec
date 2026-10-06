# Contract: Payouts (module `Payouts`, owner M6)

> Status: **DRAFT, awaiting leader approval** (ticket BE-M6-00, issue #62). Nobody codes an endpoint that is not in an approved contract.
> Sources: `.spec/spec.md` §4.4 step 3, §5.2 (0-JOIN payout query), BR-05 · `.spec/decisions.md` G-1, G-2, G-3, Q04, Q10, Q11, Q15, Q18 · `.spec/plan/00-overview.md` §5 (`PayoutBatchClosed`) · `Backend/GiupViec_Physical_DB_MVP5.drawio` tables `PAYOUT_BATCH`, `PAYOUT_ITEM`, `JOB_ASSIGNMENT` · entities `PayoutBatch.cs`, `PayoutItem.cs`, `JobAssignment.cs`, `Worker.cs`, value object `Vnd`.
> Implements tickets: BE-M6-04 (monthly batch), BE-M6-05 (bank export), BE-M6-07 (worker income). UI: WEB-M6-03 (payout batch), MOB-M6-04 (worker income).
> Conventions (envelope, camelCase, UTC, status codes, roles, policies, 404-for-not-owned) are defined in `identity.md` §1 and apply here unchanged. Items marked **Px** are not decided by the PRD or `decisions.md`; they are listed in section 4 with a recommended default.

**No real money (G-1).** The system never calls a bank. "Disbursement" means the Admin records that the transfers were made outside the system from the exported file.

## 1. Data shapes

`PayoutBatch`
```json
{
  "batchId": 0,
  "periodMonth": "YYYY-MM",
  "batchStatus": "DRAFT | CLOSED",
  "totalAmount": 0,
  "itemCount": 0,
  "confirmedBy": 0,
  "confirmedAt": "ISO-8601 UTC | null",
  "createdAt": "ISO-8601 UTC"
}
```
(`PAYOUT_BATCH`: `batch_id, period_month CHAR(7) UNIQUE, confirmed_by, batch_status VARCHAR(10), total_amount, export_file_url, confirmed_at, created_at`.) One batch per month (UNIQUE `period_month`). `exportFileUrl` is internal.

`PayoutItem`
```json
{
  "itemId": 0,
  "payeeType": "FREELANCER | AGENCY",
  "workerId": 0,
  "agencyId": 0,
  "payeeName": "string",
  "jobCount": 0,
  "grossAmount": 0,
  "commissionAmount": 0,
  "penaltyAmount": 0,
  "netAmount": 0,
  "bankName": "string | null",
  "bankAccountNo": "string",
  "itemStatus": "PENDING | TRANSFERRED"
}
```
(`PAYOUT_ITEM`; `FREELANCER` has `workerId` and null `agencyId`, `AGENCY` the reverse, enforced by a DB CHECK.) Amounts are whole VND (G-2).

## 2. Endpoints (policy `AdminOnly` unless stated)

### 2.1 Build the monthly batch (BE-M6-04)

**`POST /api/admin/payout-batches`** -> 201 (new) or 200 (existing) `data: PayoutBatch`
```json
{ "periodMonth": "2026-10" }
```
400 if `periodMonth` is not `YYYY-MM` or is in the future/current unfinished month (**P2**). **Idempotent**: calling again for a month whose batch is `DRAFT` rebuilds the items from the current data and returns the same `batchId`; a `CLOSED` batch returns 409 and is never changed.

Aggregation (PRD §5.2, one table, 0 JOIN): select `JOB_ASSIGNMENT` rows with `assignment_status = COMPLETED`, `completed_at` inside the month in `Asia/Ho_Chi_Minh` (G-3, **P2**) and `payout_item_id IS NULL` (not already paid):
- `agency_id IS NULL` -> one `FREELANCER` item per `worker_id`.
- `agency_id IS NOT NULL` -> one `AGENCY` item per `agency_id` (summary).
- `grossAmount` = sum of `gross_amount`; `commissionAmount` = sum of `Vnd.Commission(gross_amount, commission_rate)` per assignment (rate frozen at assignment creation, Q11); `netAmount = gross - commission - penalty` (`Vnd.Net`). Freelancer commission is 20%; Agency commission is its package rate (Q11).
- **Absence fee** (Q10): assignments with `absence_fee_amount` set and the absence approved are counted in `grossAmount` with **no commission** ("the platform keeps nothing from this fee").
- `penaltyAmount`: sum of pending deductions from resolved disputes against the worker (`FREELANCER`, `disputes.md` §2.3) and absence-fee reversals not yet applied. A deduction larger than gross is carried to the next batch (`netAmount` never below 0, **P3**).
- Each included assignment gets `payout_item_id` set; the rows are locked by the batch so a re-run cannot count them twice.
- `bankName`, `bankAccountNo` come from the worker (`WORKER.bank_name/bank_account_no`) or the agency; a payee without bank data is still listed with `bankAccountNo = ""` and flagged in the response of 2.3 (**P4**).

### 2.2 Read batches

**`GET /api/admin/payout-batches?page=&pageSize=`** -> 200 `data: { items: PayoutBatch[], page, pageSize, total }`, newest month first.
**`GET /api/admin/payout-batches/{batchId}?payeeType=&page=&pageSize=`** -> 200 `data: { batch: PayoutBatch, items: PayoutItem[], page, pageSize, total, warnings: ["string"] }`. `warnings` lists items without bank data (**P4**). 404 if missing.

### 2.3 Bank export (BE-M6-05, WEB-M6-03)

**`GET /api/admin/payout-batches/{batchId}/export?type=freelancer|agency-summary|agency-detail`** -> 200 **file** (`Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `Content-Disposition: attachment`). This is the only response that is not the JSON envelope (Q18). Errors still use the envelope. 400 for an unknown `type`; 404 if missing.

| `type` | Columns (Q18) |
|---|---|
| `freelancer` | `full_name`, `bank_name`, `bank_account_no`, `net_amount`, `transfer_note` (contains `period_month`) |
| `agency-summary` | one row per agency: columns **P5** |
| `agency-detail` | one row per assignment of that agency (Q18 "per-assignment detail"): columns **P5** |

Bank-specific transfer formats are deferred (Q18). The file is also stored through `IFileStorage` and its url written to `export_file_url`.

### 2.4 Confirm disbursement (BE-M6-04)

**`POST /api/admin/payout-batches/{batchId}/confirm`** -> 200 `data: PayoutBatch`. Sets `batch_status = CLOSED`, `confirmed_by` = caller, `confirmed_at` = `IClock.UtcNow`, every item `TRANSFERRED`, then publishes `PayoutBatchClosed { batchId, periodMonth, totalAmount }`. 409 if already `CLOSED` or the batch has no items. Idempotent guard: a second call never changes anything. Confirming a month that is not over is rejected (**P2**).

### 2.5 Worker income (BE-M6-07, MOB-M6-04) — policy `WorkerOnly`

**`GET /api/workers/me/earnings?month=YYYY-MM`** -> 200
```json
{
  "periodMonth": "YYYY-MM",
  "jobCount": 0,
  "grossAmount": 0,
  "commissionAmount": 0,
  "penaltyAmount": 0,
  "netAmount": 0,
  "payoutStatus": "NOT_BUILT | PENDING | TRANSFERRED",
  "jobs": [ { "assignmentId": 0, "orderId": 0, "completedAt": "ISO-8601 UTC", "grossAmount": 0, "commissionAmount": 0, "netAmount": 0, "absenceFee": false } ]
}
```
Computed from the caller's own `COMPLETED` assignments (same formula as 2.1, not stored), so it is available before the batch exists; `payoutStatus` shows the batch state. Only for `worker_type = FREELANCER` (80% net, Q11); an `AGENCY_STAFF` worker gets 403 because the agency, not the worker, is paid (PRD §4.4 step 3). `month` defaults to the current month; a future month is 400.

**`GET /api/workers/me/payouts?page=&pageSize=`** -> 200 `data: { items: [ { batchId, periodMonth, netAmount, itemStatus, transferredAt } ], page, pageSize, total }` — the worker's own closed items (the "Lịch sử giải ngân" screen).

## 3. Events
- **Publishes** `PayoutBatchClosed { batchId, periodMonth, totalAmount }`. Consumers: Notifications to workers/agencies (`INotificationService`).
- **Handles** nothing: it reads `JOB_ASSIGNMENT` and the resolved disputes in the Disputes module through the Disputes read model (**P1**).

## 4. Open questions (not covered by PRD or decisions; recommended default in bold)

| # | Question | Recommended default |
|---|---|---|
| **P1** | The batch must read pending deductions (Disputes) and absence fees (check-in/assignment) and `JOB_ASSIGNMENT` (flat shared node). Modules must not read each other's tables. | **Payouts reads `JOB_ASSIGNMENT` directly (the plan itself requires this "0 JOIN" read); pending deductions come from a new read port `IPendingDeductionQuery` implemented by Disputes (M6), so no cross-module table access.** Port added via Scope exception on BASE-03. |
| **P2** | Which month an assignment belongs to; when a month may be built and closed. | **By `completed_at` converted to `Asia/Ho_Chi_Minh` (G-3). A batch can be built for a finished month only (PRD: "ngày cuối tháng"); `CLOSED` batches are immutable.** |
| **P3** | Deduction larger than the month's gross. | **`netAmount` floors at 0; the remainder carries to the next batch.** |
| **P4** | Payee without bank data. | **Listed with a warning; the export still lists the row with an empty account so the Admin sees the gap; closing is not blocked.** |
| **P5** | Agency summary/detail columns are not defined (Q18 only fixes the Freelancer file). | **Summary: `agency_name`, `bank_name`, `bank_account_no`, `job_count`, `gross_amount`, `commission_amount`, `penalty_amount`, `net_amount`, `transfer_note`. Detail: `assignment_id`, `order_id`, `completed_at`, `worker_name`, `gross_amount`, `commission_amount`, `net_amount`.** |
| **P6** | Figma Worker Wallet shows an "available balance" (`106:263`); the PRD and decisions have no wallet: pay is monthly, held until a batch (Q04). | **No wallet is built. `earnings` shows the current month as pending; "Số dư khả dụng" in the UI must be re-labelled "Thu nhập tháng này".** |
| **P7** | Whether `PayoutBatch` has `DRAFT` and `CLOSED` only (`VARCHAR(10)`) and `PayoutItem` `PENDING`/`TRANSFERRED` (`VARCHAR(12)`). | **As above.** |

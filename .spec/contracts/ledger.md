# Contract: system ledger

> Approved target behavior under D28; implementation is delivered by the Ledger tickets.

## 1. Scope and decisions

D28 defines an append-only ledger of virtual money movements from deployment onwards; no historical movements are backfilled. G-1 still applies: neither settlement nor a withdrawal transfers real money. Amounts are whole VND represented by decimals, with rounding through `Vnd`. Timestamps are UTC; calendar boundaries and display use Asia/Ho_Chi_Minh through `IClock`.

## 2. Parties

| Type | ID | Meaning |
|---|---|---|
| EXTERNAL | customer or agency ID, or null on outgoing payments | Outside the platform |
| PLATFORM_CLEARING | null | Customer money held pending distribution |
| PLATFORM_REVENUE | null | Platform revenue |
| WORKER | worker ID | Freelancer earnings |
| AGENCY | agency ID | Agency staff earnings paid to the agency |
| AGENCY_ESCROW | agency ID | Agency escrow, mirrored from escrow transactions |

## 3. Movements

| Entry | From | To | Amount |
|---|---|---|---|
| ORDER_PAID | EXTERNAL customer | PLATFORM_CLEARING | order payment |
| EXTENSION_PAID | EXTERNAL customer | PLATFORM_CLEARING | extension payment |
| JOB_EARNING | PLATFORM_CLEARING | WORKER or AGENCY | `Vnd.Net(gross, rate)` |
| EXTENSION_EARNING | PLATFORM_CLEARING | WORKER or AGENCY | `Vnd.Net(extension, rate)` |
| COMMISSION | PLATFORM_CLEARING | PLATFORM_REVENUE | `Vnd.Commission(gross or extension, rate)` |
| REFUND | PLATFORM_CLEARING and, for any shortfall, PLATFORM_REVENUE | EXTERNAL customer | refunded amount |
| ABSENCE_FEE | PLATFORM_CLEARING | WORKER or AGENCY | approved compensation |
| PENALTY | WORKER or AGENCY | PLATFORM_REVENUE | qualifying verdict, allocated per responsible assignment |
| SUBSCRIPTION | EXTERNAL agency | PLATFORM_REVENUE | subscription payment |
| ESCROW_DEPOSIT | EXTERNAL agency | AGENCY_ESCROW | deposit |
| ESCROW_PENALTY | AGENCY_ESCROW | PLATFORM_CLEARING | penalty |
| ESCROW_REVERSAL | PLATFORM_CLEARING | AGENCY_ESCROW | reversal |
| WITHDRAWAL | WORKER | EXTERNAL | approved advance |
| SETTLEMENT | WORKER or AGENCY | EXTERNAL | monthly net |
| REVERSAL | original recipient | original sender | original amount |

Agency assignments pay the agency, not the worker. Extensions are credited only to the worker assigned to the extension. Each original amount is split into a rounded commission and its remainder, never the unrounded assignment payout. The absence and verdict handlers do not write refunds. `OrderRefunded` is published after success and writes the event amount using reference ORDER/orderId and key `REFUND:ORDER:{orderId}:{refundedAtUtc.Ticks}`, without reading the source's potentially provisional refunded amount.

The sweep considers an order only when the latest `refunded_at` of its ORDER and EXTENSION payments is at least `Ledger:RefundQuietMinutes` old (default 10). Under `ledger-append`, compare the sum of those payments' refunded amounts (target) with the sum of the order's REFUND entries (booked). Append the positive difference with key `REFUND:ORDER:{orderId}:TO:{target}`; report a negative difference without posting it. The writer supplies a build callback executing under the same lock. A refund consumes the order's clearing balance first and any shortfall from revenue. If both sources are needed, append both entries atomically: the clearing entry uses the event or sweep key and the revenue entry adds `:REVENUE`; both count toward booked. Test gateway refusal while reserved and after reversal, successful refund, recovery after ten minutes, event replay, two successive refunds and extension refunds.

For a resolved verdict with positive compensation, record penalties for FREELANCER fault, or AGENCY fault only for ABSENT_FEE. Other Agency faults use escrow. Split compensation by `gross_amount` across responsible assignments using `PayoutCalculator.SplitByWeight`: freelancer assignments have no agency ID, agency assignments have one. Write one PENALTY per positive share. The event handler uses the assignment share carried by `DisputeResolved`; the sweep uses the same split and keys. A two-worker test must prove each share, their sum and replay idempotency.

## 4. Tables

`LEDGER_ENTRY`: `entry_id` BIGINT IDENTITY primary key; unique persisted computed `entry_code` (`LG` plus the last eight zero-padded digits of `entry_id`); `from_party_type`, `to_party_type` VARCHAR(20) NOT NULL and respective nullable INT IDs; `amount` DECIMAL(18,2) NOT NULL CHECK > 0; `entry_type` and `ref_type` VARCHAR(20) NOT NULL; `ref_id` BIGINT NOT NULL; nullable `order_id` BIGINT and `available_at` DATETIME2; unique `idempotency_key` VARCHAR(80) NOT NULL; nullable `note` NVARCHAR(255) and `created_by_admin_id` INT; `prev_hash` and `entry_hash` CHAR(64) NOT NULL; `created_at` DATETIME2 NOT NULL. Reference types are ORDER, EXTENSION, ASSIGNMENT, PAYMENT, DISPUTE, ESCROW_TXN, WITHDRAWAL, PAYOUT_ITEM and ENTRY. A CHECK rejects identical from and to parties. Indexes: `(to_party_type,to_party_id,created_at)` including amount, available_at, order_id; `(from_party_type,from_party_id,created_at)` including amount; `(entry_type,created_at)`; and `(order_id)`.

`WITHDRAWAL_REQUEST`: INT IDENTITY primary key `withdrawal_id`; INT `worker_id` NOT NULL; DECIMAL(18,2) `amount` NOT NULL CHECK > 0; VARCHAR(10) `request_status` NOT NULL; nullable `bank_account_no` VARCHAR(30), `bank_name` NVARCHAR(100), `reject_reason` NVARCHAR(255), `entry_id` BIGINT, `reviewed_by` INT and `reviewed_at` DATETIME2; `requested_at` DATETIME2 NOT NULL. A filtered unique index on `worker_id` where status is PENDING prevents concurrent pending requests. `PAYOUT_ITEM` gains non-null DECIMAL(18,2) `advance_amount` and `carry_in_amount`, both default zero. `PAYOUT_BATCH.confirmed_by` is nullable.

```sql
CREATE TABLE LEDGER_ENTRY (
  entry_id BIGINT IDENTITY PRIMARY KEY,
  entry_code AS ('LG' + RIGHT('00000000' + CAST(entry_id AS VARCHAR(20)), 8)) PERSISTED,
  from_party_type VARCHAR(20) NOT NULL, from_party_id INT NULL,
  to_party_type VARCHAR(20) NOT NULL, to_party_id INT NULL,
  amount DECIMAL(18,2) NOT NULL CHECK (amount > 0),
  entry_type VARCHAR(20) NOT NULL, ref_type VARCHAR(20) NOT NULL,
  ref_id BIGINT NOT NULL, order_id BIGINT NULL, available_at DATETIME2 NULL,
  idempotency_key VARCHAR(80) NOT NULL UNIQUE, note NVARCHAR(255) NULL,
  created_by_admin_id INT NULL, prev_hash CHAR(64) NOT NULL,
  entry_hash CHAR(64) NOT NULL, created_at DATETIME2 NOT NULL,
  CHECK (NOT (from_party_type = to_party_type AND ISNULL(from_party_id,-1) = ISNULL(to_party_id,-1)))
);
CREATE UNIQUE INDEX UX_LEDGER_ENTRY_code ON LEDGER_ENTRY(entry_code);
CREATE INDEX IX_LEDGER_ENTRY_to ON LEDGER_ENTRY(to_party_type,to_party_id,created_at) INCLUDE(amount,available_at,order_id);
CREATE INDEX IX_LEDGER_ENTRY_from ON LEDGER_ENTRY(from_party_type,from_party_id,created_at) INCLUDE(amount);
CREATE INDEX IX_LEDGER_ENTRY_type ON LEDGER_ENTRY(entry_type,created_at);
CREATE INDEX IX_LEDGER_ENTRY_order ON LEDGER_ENTRY(order_id);
CREATE TABLE WITHDRAWAL_REQUEST (
  withdrawal_id INT IDENTITY PRIMARY KEY, worker_id INT NOT NULL,
  amount DECIMAL(18,2) NOT NULL CHECK (amount > 0), request_status VARCHAR(10) NOT NULL,
  bank_account_no VARCHAR(30) NULL, bank_name NVARCHAR(100) NULL,
  reject_reason NVARCHAR(255) NULL, entry_id BIGINT NULL,
  reviewed_by INT NULL, reviewed_at DATETIME2 NULL, requested_at DATETIME2 NOT NULL
);
CREATE UNIQUE INDEX UX_WITHDRAWAL_REQUEST_worker_pending ON WITHDRAWAL_REQUEST(worker_id)
  WHERE request_status = 'PENDING';
ALTER TABLE PAYOUT_ITEM ADD advance_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
  carry_in_amount DECIMAL(18,2) NOT NULL DEFAULT 0;
```

## 5. Balances

Balance is the sum of entries to a party less entries from it, optionally before an exclusive UTC cutoff. Held is the amount of a worker's PENDING request. Available is eligible worker credits (available_at absent or no later than now, excluding orders with OPEN or IN_REVIEW disputes), minus all worker debits and held, floored at zero. JOB_EARNING and EXTENSION_EARNING become available 24 hours after completion; ABSENCE_FEE and REVERSAL are immediately available. Only freelancers may withdraw; query open disputes through `IOpenDisputeQuery`.

## 6. Withdrawals

A freelancer may create a PENDING request for a positive whole amount no greater than available. Bank details are optional and copied if present. One pending request is permitted, enforced both in the service and by the filtered unique index; violation returns 409 `WITHDRAWAL_PENDING`. Invalid amounts return 400 with field errors; agency staff receive 403. A worker may cancel their own pending request. Admin may reject with a 1–255 character reason or approve. Approval locks the worker, recomputes available including the request's own held amount, and writes WITHDRAWAL and PAID in the same transaction. Insufficient funds return 409 `WITHDRAWAL_INSUFFICIENT`; transitions from a non-pending request return 409 `WITHDRAWAL_NOT_PENDING`. Approval and rejection are audited and notify the worker.

## 7. Monthly settlement

For month M, calculate `[fromUtc,toUtc)` from local month boundaries. Include each worker or agency with a nonzero balance before toUtc or any entry in the period. Under its party lock, calculate carry-in as balance before fromUtc; advance as withdrawals in the period; penalty as penalties in the period; earnings and commission from the corresponding entries; and net as `max(0, balance(before toUtc) - held)`. Absence fees are payable without commission. Record a SETTLEMENT for positive net, mark the item TRANSFERRED, close the batch with a null confirmer and publish `PayoutBatchClosed`. A pending withdrawal remains held. A closed month is immutable and repeated settlement is a no-op. The scheduler checks hourly after 00:05 on the first local day for the preceding month.

Before settlement the cutoff balance must equal carry-in plus job and extension earnings plus absence fees minus penalties and advances, adjusted by net reversals. Afterwards the remaining balance is the cutoff balance less settlement, subject to other subsequent movements. For example, a 260,000 job and 100,000 extension at 20% commission yield 288,000 earnings; after a 100,000 advance and 50,000 penalty, net settlement is 138,000. If a 100,000 request is still held instead of paid, it remains in the balance after settlement. Negative balances are carried forward; they do not create a negative settlement.

## 8. Integrity

All writes go through `ILedgerWriter.AppendAsync`, atomically and with a transaction-owned exclusive `ledger-append` application lock. A party lock, if needed, precedes the append lock. Validate positive whole amounts, distinct parties and nonempty keys of at most 80 characters. Existing idempotency keys are skipped. The first previous hash is 64 zeroes. Each hash is SHA-256 over the previous hash, entry ID, from type and ID, to type and ID, amount in invariant `F2`, entry type, reference type and ID, and UTC creation time in invariant `O`, joined by `|`; enum strings are database values. Verify in entry-ID order and report the first broken entry. No repository update/delete API is exposed after commit; use a reversal.

A reconciliation worker runs every five minutes, read-only against source records from the configured deployment start, and retries missing payments, refunds, completions, absence fees, verdicts, subscriptions and escrow transactions. It uses the event keys except for cumulative refund recovery, which uses the order target key described above. Reconciliation reports counts and amounts missing by type.

Event keys include `ORDER_PAID:ORDER:{orderId}`, `EXTENSION_PAID:EXTENSION:{extensionId}`, `JOB_EARNING:ASSIGNMENT:{assignmentId}`, `COMMISSION:ASSIGNMENT:{assignmentId}`, `EXTENSION_EARNING:EXTENSION:{extensionId}`, `COMMISSION:EXTENSION:{extensionId}`, `REFUND:ORDER:{orderId}:{refundedAtUtc.Ticks}` (event), `REFUND:ORDER:{orderId}:TO:{target}` (sweep), `ABSENCE_FEE:ASSIGNMENT:{assignmentId}`, `PENALTY:DISPUTE:{disputeId}:ASSIGNMENT:{assignmentId}`, `SUBSCRIPTION:PAYMENT:{paymentId}`, `ESCROW:{escrowTransactionId}`, `WITHDRAWAL:{withdrawalId}` and `SETTLEMENT:{yyyy-MM}:{partyType}:{partyId}`. A split refund uses the additional revenue key described above.

## 9. Endpoints

JSON responses use `ApiResponse<T>` and existing authorization policies. Paged endpoints default to 20 items and accept page sizes 1–100. Date filters are Vietnam calendar dates; opening balance is before the first local day, closing balance before the day after the last local day.

| Method and path | Policy | Purpose |
|---|---|---|
| GET `/api/v1/workers/me/wallet` | WorkerOnly, freelancer | Balance, available, held, pending request and next available time |
| POST `/api/v1/workers/me/withdrawals` | WorkerOnly, freelancer | Create with `{ "amount": 100000 }` |
| GET `/api/v1/workers/me/withdrawals` | WorkerOnly, freelancer | Paginated own requests |
| POST `/api/v1/workers/me/withdrawals/{id}/cancel` | WorkerOnly, freelancer | Cancel own pending request |
| GET `/api/v1/workers/me/ledger` | WorkerOnly, freelancer | Own history, period balances and filters |
| GET `/api/v1/workers/me/statements/{yyyy-MM}` | WorkerOnly, freelancer | Monthly JSON statement |
| GET `/api/v1/agencies/me/ledger` | PartnerOnly | Own agency and escrow history |
| GET `/api/v1/agencies/me/ledger/export` | PartnerOnly | XLSX of own history |
| GET `/api/v1/admin/ledger` | AdminOnly | History with date, type and party filters |
| GET `/api/v1/admin/ledger/summary` | AdminOnly | Revenue, clearing, worker and agency debts and held total |
| GET `/api/v1/admin/ledger/verify` | AdminOnly | `{ ok, checkedCount, firstBrokenEntryCode }` |
| GET `/api/v1/admin/ledger/reconciliation` | AdminOnly | Source-to-ledger discrepancies |
| GET `/api/v1/admin/ledger/export` | AdminOnly | XLSX history |
| GET `/api/v1/admin/withdrawals` | AdminOnly | Filtered paginated requests |
| POST `/api/v1/admin/withdrawals/{id}/approve` | AdminOnly | Approve atomically |
| POST `/api/v1/admin/withdrawals/{id}/reject` | AdminOnly | Reject with `{ "reason": "..." }` |
| POST `/api/v1/dev/settlement/run?period=yyyy-MM` | Development only | Demo settlement; 404 outside Development |

Ledger history accepts `from`, `to`, `type`, `page`, `pageSize`; Admin also accepts `partyType` and `partyId`. XLSX includes transaction code, Vietnam time, type, sender, recipient, amount, reference, hash and opening/closing rows. Worker statements are viewed in-app; no mobile spreadsheet package is added. The Admin payout batch GET and file routes remain; build and confirm POST routes are removed.

For example, a successful wallet response can contain `data: { "balance": 288000, "available": 188000, "held": 100000, "pendingWithdrawal": { "amount": 100000, "status": "PENDING" }, "nextAvailableAt": null }`. Business conflicts use the shared 409 envelope with `data.code`; validation errors use 400 and field errors. Read endpoints return 401 for unauthenticated callers, 403 for an inappropriate policy, and 404 for an unavailable resource according to the shared identity contract.

Representative `data` values (omitting optional fields) for the remaining JSON routes:

| Route | Example successful `data` |
|---|---|
| Create, cancel, approve, reject withdrawal | `{ "withdrawalId": 7, "amount": 100000, "status": "PENDING" }` (transition responses use the resulting status) |
| Worker or Admin withdrawal list | `{ "items": [], "page": 1, "pageSize": 20, "total": 0 }` |
| Worker or Agency ledger history | `{ "items": [], "openingBalance": 0, "closingBalance": 0, "page": 1, "pageSize": 20, "total": 0 }` |
| Admin ledger history | `{ "items": [], "page": 1, "pageSize": 20, "total": 0 }` |
| Worker monthly statement | `{ "periodMonth": "2026-11", "openingBalance": 0, "items": [], "advanceAmount": 100000, "penaltyAmount": 50000, "settlementAmount": 138000, "closingBalance": 0, "lastHash": "..." }` |
| Admin summary | `{ "revenue": 72000, "clearing": 0, "workerDebt": 0, "agencyDebt": 0, "held": 0 }` |
| Chain verify | `{ "ok": true, "checkedCount": 9, "firstBrokenEntryCode": null }` |
| Reconciliation | `{ "items": [] }` when no entries are missing |
| Development settlement | `{ "periodMonth": "2026-11", "batchStatus": "CLOSED" }` |

An XLSX response is a file rather than an envelope; errors retain the envelope. All requests and responses are restricted to the authenticated party unless the Admin policy applies. A missing or foreign owned withdrawal returns 404; invalid periods, filters, request bodies and page sizes return 400. The development route returns 404 outside Development.

## 10. Known gaps

The globally serialized append lock is acceptable at demo scale. Historical data predating deployment does not acquire ledger entries. Refund sweep recovery depends on the configured quiet interval exceeding the time a gateway reservation can remain provisional; this bound has not been verified in this environment. Since the event and sweep keys differ, a successful event arriving after sweep recovery also needs an explicit duplicate-prevention test; the quiet interval alone does not prove this ordering impossible.

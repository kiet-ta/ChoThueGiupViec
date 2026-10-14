# Contract: Payments (module `Payments`, owner M2)

> Status: **DRAFT, awaiting leader approval** (ticket BE-M2-00, issue #181). Nobody codes an endpoint that is not in an approved contract.
> Sources: `.spec/spec.md` §1.2 (Webhook IPN, Pay-per-Job 100 %), §4.1 step 3, BR-03, BR-08, BR-10 · `.spec/decisions.md` G-1, G-2, G-3, G-6, G-7, Q04, Q04b, Q07, Q10, Q15 · `.spec/plan/00-overview.md` §4 (`IPaymentGateway`, `IRefundService`), §5 (events) · `Backend/GiupViec_Physical_DB_MVP5.drawio` table `PAYMENT_TRANSACTION` · entity `Backend/Domain/Entities/PaymentTransaction.cs`, enums `PaymentStatus`, `PaymentPurpose` · ports `Backend/Application/Interfaces/Ports/IPaymentGateway.cs`, `IRefundService.cs` · Fake `Backend/Infrastructure/Fakes/FakePaymentGateway.cs`.
> Implements tickets: BE-M2-04, BE-M2-05, BE-M2-05a, BE-M2-06, BE-M2-07. UI: MOB-M2-03, MOB-M2-05 (QR part). Orders, cancel and extension requests: `booking.md`.
> Conventions (envelope, camelCase, UTC, status codes, roles, `CustomerOnly`, 404-for-not-owned, O5) are those of `identity.md` §1, except the gateway-facing IPN endpoint (§2.4).
> **Sandbox only, no real money (G-1, Q04).** Gateway = MoMo sandbox behind `IPaymentGateway`; the Fake gateway is used until the leader provides sandbox keys through user-secrets (G-6). VietQR / real money is **deferred (Q04b): not implemented**.
> Items marked **Px** are open; see section 5.

## 1. Values and data shapes

### 1.1 Transaction status (`PAYMENT_TRANSACTION.txn_status`, enum `PaymentStatus`)

| Value | Meaning |
|---|---|
| `PENDING` | QR created, not paid (initial) |
| `SUCCESS` | paid (IPN or reconciliation) |
| `EXPIRED` | not paid within `Payments.QrExpiryMinutes` (15), or the order was cancelled before payment |
| `REFUNDED` | money given back (whole or partial, see **P2**) |

Allowed: `PENDING -> SUCCESS | EXPIRED`, `SUCCESS -> REFUNDED`. A late `SUCCESS` IPN for an `EXPIRED` transaction: **P4**.

### 1.2 Purpose (`PAYMENT_TRANSACTION.purpose`)
`ORDER` (sets `order_id`) | `EXTENSION` (sets `extension_id`). `SUBSCRIPTION` belongs to M5 (Agency, `agencies` contract); this module's endpoints never create it. The DB CHECK already enforces exactly one of the three ids.

### 1.3 `Payment`
```json
{
  "paymentId": 0, "purpose": "ORDER | EXTENSION", "orderId": 0, "extensionId": null,
  "gateway": "MOMO", "amount": 0, "txnStatus": "PENDING",
  "qrPayload": "string | null", "payUrl": "string | null",
  "expiresAt": "ISO-8601 UTC", "paidAt": "ISO-8601 UTC | null",
  "createdAt": "ISO-8601 UTC", "sandbox": true
}
```
- `qrPayload` is the text the app renders as a QR; `payUrl` (optional) opens the MoMo sandbox page. Both only while `PENDING`.
- `gateway_txn_ref` and `ipn_payload` are **never** returned.
- `expiresAt` for `ORDER` = order `createdAt + Payments.QrExpiryMinutes` (the deadline of `booking.md` `paymentDeadlineAt`); for `EXTENSION` = transaction `createdAt + Payments.QrExpiryMinutes`. Not stored (no column): computed. See **P3**.
- `gateway`: `MOMO` for the sandbox adapter, `FAKE` for the Fake (column `VARCHAR(10)`).
- `sandbox` is always `true` (the app shows the "SANDBOX - no real money" banner, Q04).

## 2. Endpoints

### 2.1 `POST /api/payments/orders/{orderId}/qr` (`CustomerOnly`, BE-M2-04, MOB-M2-03)
No body. Response **201** `data`: `Payment` when a transaction is created, **200** with the existing one when a `PENDING`, not expired transaction already exists for the order (idempotent: tapping twice never creates two QRs).
- Amount = `JOB_ORDER.total_amount` (100 %, Pay-per-Job, PRD §1.2).
- Calls `IPaymentGateway.CreateQrAsync(purpose ORDER, paymentRef = orderId, amount, description = orderCode, expiresAtUtc)` and inserts `PAYMENT_TRANSACTION` (`txn_status = PENDING`, `gateway_txn_ref` from the gateway, `qr_payload`, `created_at`).

| Status | Condition |
|---|---|
| 404 | order not found / not the caller's |
| 409 | `data.code` = `INVALID_STATE` (order not `PENDING_PAYMENT`) \| `PAYMENT_EXPIRED` (deadline passed; the order is or will be cancelled by §3.3) |
| 502 | the gateway refused or did not answer; nothing is stored, the client may retry |

### 2.2 `POST /api/payments/extensions/{extensionId}/qr` (`CustomerOnly`, BE-M2-08, MOB-M2-05)
Same as 2.1 for an extension in `PENDING_PAYMENT` (`booking.md` §3.8); amount = `JOB_ORDER_EXTENSION.extra_amount`; `purpose = EXTENSION`. 404 if the extension's order is not the caller's.

### 2.3 `GET /api/payments/{paymentId}` (`CustomerOnly`, polling fallback, MOB-M2-03)
200 `data`: `Payment`. 404 if not the caller's. `GET /api/payments?orderId=` returns every transaction of one of the caller's orders (order and extension), newest first: 200 `data: Payment[]`.

Realtime (Q07): on every status change the module sends `NotificationMessage { RecipientRole = Customer, RecipientId = customerId, Topic = "payment.status", Data = { paymentId, orderId, extensionId, txnStatus } }` through `INotificationService` (SignalR hub `/hubs/notifications`, already merged). The app shows `PAID` from the push and polls 2.3 only when the socket is down.

### 2.4 `POST /api/payments/ipn/momo` (anonymous, gateway-facing, BE-M2-05, BE-M2-06)
Called by the MoMo sandbox (or a test with the Fake). This endpoint is the only one **outside** the `identity.md` envelope rules: the request body and the exact response the gateway expects are those of MoMo's official documentation and are written into this section by BE-M2-06 (**G-7: field names and the signature are not guessed here**).

Processing (independent of the gateway format):
1. The raw key/value pairs go to `IPaymentGateway.VerifyIpnAsync`. `IsSignatureValid = false` -> reject (HTTP 400, nothing changed, no event, the attempt is logged without secrets).
2. Find the transaction by `gateway_txn_ref` (UNIQUE, Q04). Unknown ref -> reject, nothing changed.
3. Amount must equal the stored `amount`; otherwise reject and log (**never** mark paid on a mismatch).
4. **Idempotent (Q04):** if the transaction is already `SUCCESS` (or `REFUNDED`), return success and change nothing: no second status change, no second event.
5. `PENDING` + gateway status success -> in **one DB transaction**: `txn_status = SUCCESS`, `paid_at = now`, `ipn_payload` = raw body; for `ORDER` the order `PENDING_PAYMENT -> PAID` (`booking.md` §1.1); for `EXTENSION` `ext_status = PAID`. After commit publish `OrderPaid` or `ExtensionPaid` (§4) and push `payment.status`.
6. Gateway status failed/expired -> `txn_status = EXPIRED` (the order follows §3.3).
7. A concurrent duplicate IPN must not double-apply: the update is conditional (`WHERE txn_status = 'PENDING'`); the loser behaves like step 4.

**HTTP answer (BE-M2-06, MoMo's "Payment Notification" page):** MoMo calls `ipnUrl` with `POST`, `Content-Type: application/json`, and requires **HTTP 204 (No Content) within 15 seconds**. So this endpoint answers **204** with no body when the IPN is accepted or is an idempotent repeat, and **400** (envelope, no reason given) when it is rejected. The same answers apply to the Fake.

Dev/test with the Fake gateway: payload keys `gatewayTxnRef`, `amount`, `status` (`success` | `pending` | `expired`), `signature` (`valid` = valid), as documented in `FakePaymentGateway.cs`.

**MoMo format (BE-M2-06; read from MoMo's official documentation, G-7 — one-time payment, Payment Notification, query, refund and Result Code pages of `developers.momo.vn/v3/docs/payment/api/`).** The adapter `MoMoPaymentGateway` is registered instead of the Fake only when the configuration section `MoMo` holds real sandbox values (user-secrets / environment, G-6); it refuses any endpoint that is not `https://test-payment.momo.vn` (G-1). **It was written without sandbox keys and has not been run against MoMo.**
- Every signature = HMAC-SHA256 (hex) with the secret key over `key=value` pairs joined by `&`, keys sorted a to z. `accessKey` is part of the signed string and is never sent as a field.
- Create: `POST /v2/gateway/api/create`, `requestType = captureWallet`, fields `partnerCode, requestId, amount (Long, 1,000 to 50,000,000 VND), orderId, orderInfo, redirectUrl, ipnUrl, requestType, extraData, lang, signature`; signed string `accessKey, amount, extraData, ipnUrl, orderId, orderInfo, partnerCode, redirectUrl, requestId, requestType`. The MoMo `orderId` generated by the adapter is what is stored as `gateway_txn_ref`; `qr_payload` = `qrCodeUrl` (or `payUrl` when MoMo returns none).
- IPN body: `partnerCode, orderId, requestId, amount, orderInfo, orderType, transId, resultCode, message, payType, responseTime, extraData, signature` (and optional extras); signed string `accessKey, amount, extraData, message, orderId, orderInfo, orderType, partnerCode, payType, requestId, responseTime, resultCode, transId`. The IPN is rejected when the signature differs, a signed field is missing or `partnerCode` is not ours; `orderId` is the lookup key of step 2 and `amount` the value of step 3.
- Status: `resultCode` 0 = paid (`SUCCESS`); a code the Result Code table marks final and not 0 (98, 99, 1001-1007, 1017, 1026, 1080, 1081, 1088, 2019, 4001, 4002, 4100) = `EXPIRED` (step 6); any other code (1000 initiated, 7000 / 7002 processing, 9000 authorized, non-final errors, unknown codes) = still `PENDING`, nothing changes.
- Query (§3.1): `POST /v2/gateway/api/query`, fields `partnerCode, requestId, orderId, lang, signature`; signed string `accessKey, orderId, partnerCode, requestId`.
- Refund (§3.4): `POST /v2/gateway/api/refund`, fields `partnerCode, orderId (a NEW id), requestId, amount, transId (MoMo's id of the original payment), lang, description, signature`; signed string `accessKey, amount, description, orderId, partnerCode, requestId, transId`. The port does not carry `transId`, so the adapter asks the query API for it first. Partial refunds are supported by MoMo.

Required tests (BE-M2-05 done criteria): replay of the same IPN changes nothing the second time; a wrong signature changes nothing; wrong amount changes nothing; two concurrent identical IPNs give one `SUCCESS` and one `OrderPaid`.

## 3. Background jobs and internal services

### 3.1 Reconciliation (BE-M2-05a, Q04)
Hosted job every `Payments.ReconcileIntervalSeconds` (60):
- For each `PENDING` transaction older than `Payments.ReconcileAfterMinutes` (3): `IPaymentGateway.QueryStatusAsync(gatewayTxnRef)`. `SUCCESS` -> exactly the §2.4 step 5 path (same idempotent code, so an IPN arriving later is a no-op). A customer who paid is never left with an unpaid order.
- Test (done criterion): a payment whose IPN is lost becomes `SUCCESS` and the order `PAID` through this job alone.

### 3.2 Paid order
The only place an order becomes `PAID` is §2.4 step 5 (shared by IPN and reconciliation). It publishes `OrderPaid { OrderId, CustomerId, Amount, ShiftCode, ScheduledDate, RequiredWorkers }`, consumed by Dispatch (M3). Funds stay with the platform until the assignments are `COMPLETED`; nothing is paid out here (Q04: money leaves only through `PAYOUT_BATCH`, M6).

### 3.3 QR expiry (Q04)
In the same job: a `PENDING` transaction past `expiresAt` (§1.3) whose gateway status is still not paid -> `EXPIRED`. For `ORDER`: the order `PENDING_PAYMENT -> CANCELLED` (`cancel_reason = "PAYMENT_EXPIRED"`), no charge, Premium hold released, `OrderCancelled` published. An order still `PENDING_PAYMENT` past its deadline with **no** transaction (QR never requested) is cancelled the same way. For `EXTENSION`: `ext_status = EXPIRED`; the order is unchanged.

### 3.4 Refunds (`IRefundService`, BE-M2-07)
Real implementation of `IRefundService.RefundAsync(RefundRequest { OrderId, Amount, Reason })`, replacing `FakeRefundService`:
- Refunds against the order's `SUCCESS` (`purpose = ORDER`) transaction through `IPaymentGateway.RefundAsync`. If the gateway has no refund API (`GatewaySupported = false`) the refund is recorded in the DB only and the PR says which path was used (Q04).
- `Amount` is whole VND (`Vnd.Round`), > 0 and <= the amount not refunded yet (needs **P2**); otherwise `RefundResult.Succeeded = false`, nothing changed.
- Records `txn_status = REFUNDED` with reason (**P2**), then publishes `OrderRefunded { OrderId, CustomerId, Amount, Reason, RefundedAtUtc }` and pushes `payment.status`.
- Gateway failure -> `Succeeded = false`, nothing recorded; the caller decides (Disputes rolls back with 502, `disputes.md` §2.3).

Callers and amounts:

| Trigger | Amount | Caller |
|---|---|---|
| No worker within 10 km (BR-03) / incident without substitute (BR-10) | 100 % of `total_amount` | Booking handler of `AssignmentFailed` (`booking.md` §5) |
| Customer cancel (Q15) | 100 % (or 60 % late, **B10**) | Booking §3.7 |
| Customer absent approved (Q10) | 60 % of the assignment's `gross_amount` | Admin module, directly (`admin.md` §2.2) - Payments does **not** refund again on `CustomerAbsentApproved` |
| Dispute verdict | `compensationAmount` | Disputes module, directly (`disputes.md` §2.3) - Payments does **not** refund again on `DisputeResolved` |

Extension refund (worker declines, BR-08): handler of `ExtensionDeclined` refunds 100 % of the extension's `SUCCESS` transaction (same rules; `IRefundService` only takes an `OrderId`, so this path is internal to the module) and sets `ext_status = DECLINED` (`booking.md` §5).

## 4. Events
- **Publishes** `OrderPaid` (§3.2), `ExtensionPaid { ExtensionId, OrderId, WorkerId, ExtraHours, ExtraAmount, PaidAtUtc }` (§2.4; type mismatch in **B11** of `booking.md`), `OrderRefunded` (§3.4), `OrderCancelled` (§3.3, QR expiry).
- **Handles** `ExtensionDeclined` (M4) for the refund (§3.4). `AssignmentFailed` is handled by Booking, which calls this module's `IRefundService`.

## 5. Open questions (P1-P5) - recommended default, leader decides

| # | Question | Recommended default |
|---|---|---|
| **P1** | MoMo sandbox request/IPN/refund formats and the HTTP answer MoMo expects from the IPN endpoint. | Not decided here (G-7). BE-M2-06 reads MoMo's official docs and adds them to §2.4 in its PR; until then the Fake format applies. |
| **P2** | `PAYMENT_TRANSACTION` has no refund amount, refund reason or refunded time, yet Q04 says "record REFUNDED with a reason" and Q10/Q15/disputes need **partial** refunds (60 %, compensation) possibly more than once per order. | M1 adds `refunded_amount DECIMAL(18,2) NOT NULL DEFAULT 0`, `refund_reason NVARCHAR(255) NULL`, `refunded_at DATETIME2 NULL`; `txn_status = REFUNDED` once `refunded_amount > 0`; a refund is refused when it would exceed `amount`. Alternative: one new row per refund (new purpose `REFUND`) - more audit-friendly but changes the CHECK constraint. |
| **P3** | No `expires_at` column; expiry is computed from `created_at`. | Keep it computed (no schema change); the value is deterministic because `Payments.QrExpiryMinutes` is config. |
| **P4** | A `SUCCESS` IPN arrives for a transaction already `EXPIRED` (customer paid at minute 14:59, IPN at 15:01, order already cancelled). Principle 0: customer money is protected first. | Record `SUCCESS` (money was received) and immediately refund 100 % through §3.4 with reason `PAID_AFTER_EXPIRY`; the order stays `CANCELLED`. Needs the leader's OK because it adds `EXPIRED -> SUCCESS`. |
| **P5** | Gateway call fails at QR creation (§2.1, 502) - retry policy. | No automatic retry server-side; the client retries; the order deadline does not move. |

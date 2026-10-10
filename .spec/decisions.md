# Product Decisions (APPROVED)

> Status: **APPROVED** by the leader (Kiệt) on 2026-10-04.
> Purpose: close every gap that `.spec/spec.md` (the PRD) leaves open. This file is **English on purpose**: agents must follow it literally.
> Precedence: `spec.md` (PRD) > `decisions.md` > `plan/*.md`. This file never overrides an explicit PRD statement. If anything conflicts, or something is not covered here or in the PRD: **STOP, do not guess, ask the leader in the GitHub Issue.**
> Everything marked `DEFAULT` is a seed/demo value the leader will change later through the Admin UI or config. Never treat it as a business truth and never hard-code it.

## 0. Guiding principle
This is a **platform**. The parties that pay are the priority: **Customers** (pay per job) and **Partner Agencies** (subscription + escrow). **Freelancers are managed by the platform**, not a protected party.
- Customer/Agency money is protected first (refunds, reconciliation, no lost transactions).
- When a customer is harmed, compensate the customer first; push the cost to the violating party (Freelancer: penalty/lock; Agency: escrow deduction).
- Rules for Freelancers are stricter than for payers (eKYC, audit, penalties, lock).
- The paying experience (payment, tracking, refund) is realtime and reliable before anything else.
- This principle is **never** a reason to change an explicit PRD rule (e.g. Agency has no access to the Economy pool, Freelancer receives 80%, BR-05 worker receives 40%).

## 1. Global rules
- **G-1 Demo scope.** The system runs as a demo/test product. **No real money, ever.** Payments use the MoMo **sandbox** only. Do not add production payment credentials anywhere.
- **G-2 Money.** Currency is VND only. DB type `DECIMAL(18,2)` (as in the schema). Every computed amount is rounded to whole VND (round half away from zero). Net payout = `gross_amount - round(gross_amount * commission_rate)`.
- **G-3 Time.** All stored timestamps are UTC (`DATETIME2`). Shift times (08:00-12:00, 13:00-17:00, 17:30-20:30) are `Asia/Ho_Chi_Minh` local time. Conversion happens in exactly one place (`IClock`).
- **G-4 Config, not constants.** All values in section 4 live in one options class (`BusinessRules`) bound from configuration. Do not scatter numbers in handlers.
- **G-5 Admin-editable money is audited.** Any money parameter an Admin can edit (price rules, subscription package price/commission/quota) writes an append-only row to `ADMIN_AUDIT_LOG` in the **same DB transaction** as the change. No update/delete API exists for that table.
- **G-6 Secrets.** Sandbox keys and any credential come from `dotnet user-secrets` / environment variables. Never commit them.
- **G-7 Do not invent.** Field names, endpoints and provider API shapes not written in the PRD, this file or an approved contract must be read from the official docs or asked about. If unsure, ask in the issue.

## 2. Decisions

### Q01 Pricing
- Prices are **data in the DB** (`PRICE_RULE`), never code. One row per `(service_tier, area_bracket)`. A price is **per worker per shift** (shift = up to 4 hours).
- Area brackets (from the address `total_area_m2`): `UP_TO_30` (<= 30 m2), `FROM_31_TO_80` (> 30 and <= 80), `OVER_80` (> 80).
- Workers required: `total_area_m2 <= 80` -> 1 worker; `> 80` -> **exactly 2 workers** (PRD BR-02). The PRD defines nothing beyond 2: do not invent 3+ workers.
- Order total = `unit_price(service_tier, area_bracket) * required_workers`. Each Job Assignment `gross_amount` = that unit price.
- **Price snapshot:** `JOB_ORDER.total_amount` is computed once at order creation and frozen. Later Admin price changes never alter existing orders.
- DEFAULT seed (VND per worker per shift), loaded by migration/seed data in **all** environments:

| service_tier | area_bracket | unit_price |
|---|---|---|
| ECONOMY | UP_TO_30 | 160000 |
| ECONOMY | FROM_31_TO_80 | 260000 |
| ECONOMY | OVER_80 | 260000 |
| PREMIUM | UP_TO_30 | 240000 |
| PREMIUM | FROM_31_TO_80 | 390000 |
| PREMIUM | OVER_80 | 390000 |

- Extension ("Lam lan 2", BR-08): `extra_amount = round(unit_price / 4 * extra_hours)` using the same tier/bracket row (4 = `Shift.MaxHours`).
- No add-on/service surcharge in the MVP. The "service + note" chosen by the customer is stored as a note only.
- **Admin editing:** only role Admin may change `unit_price`. Every change **requires a non-empty `reason`** and creates one `ADMIN_AUDIT_LOG` row: who (`admin_id`), when (`changed_at`, UTC), `entity_type = PRICE_RULE`, `entity_id`, `field_name`, `old_value`, `new_value`, `reason`. The change takes effect immediately for **new** orders only. Admin can list the change log (read-only).

### Q03 Photo sharpness (Variance of Laplacian, BR-06)
- Algorithm (server is authoritative; the app pre-checks with the same algorithm): convert to grayscale -> resize to width `Vol.ResizeWidthPx` (640 px, keep aspect ratio) -> convolve with the 3x3 Laplacian kernel -> `vol_score` = variance of the result.
- Accept the photo only if `vol_score >= Vol.Threshold` (DEFAULT `100.0`). The threshold is calibrated later with about 30 real phone photos; only the leader changes it.
- Store the result in `JOB_PHOTO.vol_score` and `JOB_PHOTO.is_accepted`. A rejected photo is **not** a penalty; the worker simply retakes it.
- A phase (`BEFORE`/`AFTER`) needs between `Photos.MinPerPhase` (3) and `Photos.MaxPerPhase` (5) **accepted** photos. The set of `angle_no` values in `AFTER` must equal the set in `BEFORE`. The app fetches the threshold from the API; it never hard-codes it.

### Q04 Payment (MoMo sandbox only)
- Gateway: **MoMo sandbox** behind `IPaymentGateway`. VietQR is **out of scope** (deferred). Read MoMo's official documentation for request/signature formats (G-7); do not guess field names.
- The leader provides sandbox credentials via user-secrets. Until then, use the Fake gateway.
- **IPN is idempotent:** `PAYMENT_TRANSACTION.gateway_txn_ref` is UNIQUE. A repeated IPN returns success and changes nothing. A bad signature is rejected and changes nothing.
- **Reconciliation job:** every `Payments.ReconcileIntervalSeconds` (60) look for `PENDING` transactions older than `Payments.ReconcileAfterMinutes` (3) and ask MoMo for the transaction status; update accordingly (a customer who paid must never be left with an unpaid order). A QR not paid within `Payments.QrExpiryMinutes` (15) becomes `EXPIRED` and the order is cancelled with no charge.
- **Funds are held by the platform** until the assignment reaches `COMPLETED`; money leaves only through a `PAYOUT_BATCH`. Nothing is paid out automatically.
- Refunds record `REFUNDED` on the transaction with a reason. Use the MoMo sandbox refund API if it exists; otherwise record the refund in the DB only and state in the PR which path was used.
- The mobile payment screen shows a visible "SANDBOX - no real money" banner.

### Q05 eKYC (Freelancer)
- **Phase 1 uses a Fake provider only** (`IEkycProvider`). It returns `Ekyc.Fake.Confidence` (DEFAULT `92.00`). The real provider is **deferred**: do not implement or choose one (see `Q05b`).
- Rules: confidence `>= Ekyc.AutoApproveConfidence` (85.00) -> activate account (`IDLE`) immediately. Below that or fraud flag -> manual review queue. `national_id` and `phone_number` are unique. A Freelancer cannot receive any job before KYC is approved.
- Audit: a new Freelancer's first `Ekyc.FullAuditFirstJobs` (5) completed jobs are audited at 100%; afterwards a random sample of `Ekyc.AuditRate` (0.20, the upper bound of the PRD's 10-20%) is audited. Agency workers are not eKYC'd (PRD 2.8).

### Q06 OTP
- 6 digits, valid `Otp.TtlMinutes` (5), at most `Otp.MaxAttempts` (5) tries per code, resend cooldown `Otp.ResendCooldownSeconds` (60), at most `Otp.MaxPerPhonePerHour` (5) per phone and `Otp.MaxPerIpPerHour` (20) per IP. Store only a hash of the code.
- Applies to Customer and Worker login (phone + OTP).
- Development uses a Fake `IOtpSender` that writes the OTP to the console log. The Fake must be **refused outside Development** (startup fails). Real SMS provider is **deferred** (see `Q06b`).

### Q07 Realtime
- **SignalR**. Customer payment status and job tracking are realtime first. Worker offers (30 s) also use SignalR, with polling as a fallback. FCM push is **deferred** (see `Q07b`) but required before any real pilot.

### Q08 Subscription packages (Agency)
- Packages are rows in `SUBSCRIPTION_PACKAGE`, editable by Admin with audit (G-5). DEFAULT seed:

| package_code | tier | billing_cycle | price (VND) | worker_quota | commission_rate | roster dashboard / analytics / priority dispatch |
|---|---|---|---|---|---|---|
| FREE | FREE | (none) | 0 | 3 | 0.200 | no / no / no |
| PRO_MONTHLY | PRO | MONTHLY | 2000000 | 50 | 0.000 | yes / yes / yes |
| PRO_QUARTERLY | PRO | QUARTERLY | 5400000 | 50 | 0.000 | yes / yes / yes |

- Expiry: `end_date` passes -> **7 days grace** (`Subscription.GraceDays`), still Pro. After grace the agency drops to FREE limits: workers above quota (most recently created first) are locked, **never deleted**; jobs already `ASSIGNED` are still carried out.
- Business warning for the leader: the Pro price must exceed the commission the platform would earn at 20% on the agency's expected monthly volume (break-even for 2,000,000 VND is 10,000,000 VND of monthly volume).

### Q09 Escrow (Performance Bond) and SLA
- An agency becomes `ACTIVE` only when `escrow_deposit_balance >= Agency.MinEscrowBalance` (DEFAULT 5,000,000 VND). Deposits are paid through the MoMo sandbox.
- SLA score starts at `Sla.InitialScore` (100.00). Penalties: `NO_SHOW` -20, `SHORTAGE` -10, `QUALITY_COMPLAINT` (only when a dispute is **upheld**) -5. Score `<= 70` -> warning notification. Score `< 50` -> agency is excluded from Premium dispatch. There is **no automatic recovery**; only an Admin adjustment (audited).
- **Deduction order** when an agency violates: (1) refund the customer 100% of the affected amount, (2) actual cost of the rescue worker, (3) platform fee. Deduction amount = customer refund + rescue cost. The balance never goes below 0: deduct what exists, record the shortfall, and set `agency_status = SUSPENDED` until topped up.
- Appeal: the agency may appeal within `Sla.AppealHours` (48). Appeals are decided manually by Admin in the MVP; a reversal restores points/money through a `REVERSAL` row. Every balance change is an append-only `ESCROW_TRANSACTION` row (see SC-4).

### Q10 Customer absent (BR-05)
- Admin may approve only when **all** hold: (a) the check-in GPS was verified, (b) `CHECK_IN_LOG.call_attempts >= Absence.MinCallAttempts` (2), (c) at least `Absence.MinWaitMinutes` (15) passed since `checked_in_at`, (d) the worker set `customer_absent_at`. The system refuses approval otherwise.
- On approval, per affected assignment: customer is charged **exactly 40%** (`Absence.FeeRate`) of that assignment's `gross_amount` and the other 60% is refunded to the original payment method. The worker receives that 40% as `JOB_ASSIGNMENT.absence_fee_amount` and returns to `IDLE`. The platform keeps nothing from this fee.
- The customer is notified immediately and has `Absence.CustomerDisputeHours` (24) to open a dispute. If the customer wins, refund the 40% and deduct it from the worker's next payout (`PAYOUT_ITEM.penalty_amount`).
- A false absence claim is recorded as an upheld dispute with `fault_party` = the worker's side (`FREELANCER` or `AGENCY`, Q22 D3).

### Q11 Commission
- Freelancer: 20% (`Commission.Freelancer` = 0.200; PRD). Agency: the `commission_rate` of its active package (FREE 0.200, PRO 0.000). The rate is copied into `JOB_ASSIGNMENT.commission_rate` when the assignment is created and never changes afterwards.

### Q12 Super-Freelancer
- `WORKER.is_super_freelancer` can be set **only by an Admin**, and only if: `rating_avg >= 4.80`, `completed_jobs >= 50`, KYC approved, and no upheld dispute with `fault_party = FREELANCER` (Q22 D3) in the last 180 days.
- Automatic revoke when `rating_avg < 4.70` (written to `ADMIN_AUDIT_LOG` with `actor_type = SYSTEM`).
- Super-Freelancers are used only for Premium emergency rescue (PRD 2.5).

### Q13 Premium lead time
- Minimum lead = `Premium.MinLeadHours` (4) between "now" and the shift start (`scheduled_date` + shift start time). Less than that -> reject with a clear error and create no order. No capacity at that time -> respond "fully booked" and create no order.

### Q14 Ratings (BR-09)
- The window opens when the assignment becomes `COMPLETED` and closes `Rating.WindowHours` (48) after `completed_at`. One rating per `(assignment_id, rater_role)`.
- Customer -> Worker: `stars` 1-5 and `criteria_json` with keys `punctuality`, `cleaning_quality`, `attitude` (each 1-5). Worker -> Customer: `stars` 1-5 and keys `cooperation`, `working_conditions` (each 1-5).
- The customer's rating of a worker strongly affects MatchingScore. The worker's rating of a customer is **internal only** and never blocks a customer from booking; 3 consecutive ratings of <= 2 stars from different workers only raise a flag for Admin review.
- A Freelancer with `rating_avg < 4.00` after `>= 10` completed jobs is excluded from matching until an Admin reviews. Ratings do **not** change an Agency's SLA score automatically; only upheld disputes do (Q09).

### Q15 Cancellation
- Customer cancels before the job is `ASSIGNED`, or more than `Cancel.FullRefundHoursBefore` (2) hours before the shift start: **100% refund**.
- Customer cancels within 2 hours of the shift start: charged `Cancel.LateFeeRate` (0.40) of `gross_amount` (paid to the worker as compensation), 60% refunded.
- Freelancer cancels: customer gets a 100% refund and the order is re-dispatched; the worker gets a violation. `Cancel.WorkerCancelLockCount` (3) worker-initiated cancellations within `Cancel.WorkerCancelWindowDays` (30) lock the account (Admin can unlock). Count is derived from assignment status (a distinct "cancelled by worker" status must exist). No new column.
- Exempt from all penalties: incidents handled under BR-10 (`INCIDENT_LOG.penalty_waived = 1`). Agency workers are governed by the SLA rules (Q09), not by the app lock.

### Q16 Authentication
- **Customer, Worker (both types):** phone + OTP (Q06). **Admin:** email + password (`ADMIN.password_hash`). **Partner Agency:** email (`contact_email`) + password (new column `PARTNER_AGENCY.password_hash`, SC-1).
- Password policy: at least `Auth.MinPasswordLength` (10) characters with upper case, lower case and a digit. Lock the account for `Auth.LockoutMinutes` (15) after `Auth.LockoutFailures` (5) consecutive failures. Hash with `IPasswordHasher`. 2FA is deferred.
- Roles: `Customer`, `Worker`, `Partner`, `Admin`.
- **Dev Admin seed** is specified in `plan/00-overview.md` section 10 (Development only, idempotent).

### Q17 Calls and phone privacy
- The "Call customer" button opens the phone dialer and calls a server endpoint that increments `CHECK_IN_LOG.call_attempts` by 1 (only after check-in). There is no telephony integration in the MVP.
- The customer's phone number is returned to the worker **only** from `Privacy.PhoneVisibleBeforeMinutes` (60) before the shift start until `Privacy.PhoneVisibleAfterMinutes` (60) after the later of the shift end and `completed_at`, and only while the assignment is in an active state. Outside that window the API returns no phone number. Masked calling is deferred.

### Q18 Import and export
- Agency worker import: `.xlsx` from a downloadable template (columns defined in the approved `agencies` contract, mapped to existing `WORKER` columns). Two steps: **dry-run** (validates, returns per-row errors, writes nothing) then **commit**. Commit is **all-or-nothing**: any invalid row or exceeding `worker_quota` rejects the whole file.
- Payout export: `.xlsx`. Freelancer file columns: `full_name`, `bank_name`, `bank_account_no`, `net_amount`, `transfer_note` (contains `period_month`). Agency gets a summary file plus a **per-assignment detail** file. Bank-specific formats are deferred.

### Q19 Agency guarantee
- The agency must have `PARTNER_AGENCY.guarantee_signed_at` set **before** it can import workers; otherwise the import API rejects with a clear error. MVP flow: the legal representative uploads the signed guarantee PDF (stored via `IFileStorage`, file name includes the document version); the system records `guarantee_signed_at` and `guarantee_file_url` (SC-1).

### Q20 Identity details (leader, 2026-10-04; contract `.spec/contracts/identity.md`)
- **New freelancer (O1):** after OTP verification a phone with no `WORKER` row gets **no access token**; the API returns `isNewUser = true` and a single-purpose `registrationToken` that only the M4 registration endpoint accepts. The person must complete a profile (then eKYC, Q05) before logging in normally.
- **OTP, refresh and lockout state live in the database (O3)**, not in a cache: tables `OTP_CODE` (SC-6) and `REFRESH_TOKEN` (SC-7), lockout columns on `ADMIN` and `PARTNER_AGENCY` (SC-8). Only a keyed HMAC of the OTP code is stored (the key comes from user-secrets/environment, G-6). Resend cooldown and the per-phone/per-IP hourly limits (Q06) are computed from `OTP_CODE` rows. Refresh tokens are opaque, stored hashed, rotated on every use; reuse of a rotated token revokes the whole family. SC-7 and the lockout columns extend the leader's answer in the same direction; the leader may overrule.
- **Token lifetimes (O2):** access 15 min, refresh 30 days, registration token 30 min (the registration token value is a suggestion, not a leader decision). All in `BusinessRules` (section 4).
- **Phone (O4):** Vietnamese mobile numbers only, normalized to digits in national form `0XXXXXXXXX` (strip spaces, `+84` -> `0`); anything else is rejected with 400.
- **Validation errors (O5):** HTTP 400, envelope `success = false`, `data = { "errors": { "<field>": ["<message>"] } }`.
- **Account states (O6):** customers `ACTIVE` / `LOCKED` (LOCKED cannot log in, 403). A Partner can log in even when `SUSPENDED` (Q09); only Admin deactivation (`ADMIN.is_active = 0`) blocks an Admin.

### Q21 Customer details (leader, 2026-10-04; contract `.spec/contracts/customers.md`)
The leader's instruction was "if not affected, just implement", so these are the contract's recommended defaults:
- **C1** `CUSTOMER.full_name` is an empty string until the customer completes the profile; the app asks before the first booking.
- **C2** `trust_score` is provisional: `Customer.InitialTrustScore` (0.00) on the 0.00-5.00 scale of Q14. Nothing in the MVP reads or changes it. The leader sets the real meaning later.
- **C3** New read port `IWorkerProfileQuery` (implementer M4): worker display fields (`fullName, ratingAvg, completedJobs, workStatus`) and an existence check for the favorite-workers list.
- **C4** `APARTMENT` and `ROOM`: 1 floor. `ROOM`: area <= `Address.RoomMaxAreaM2` (30, PRD section 1.1). `HOUSE`: 1 to `Address.HouseMaxFloors` (10, a suggestion, not in the PRD).
- **C5** Deleting an address that an order references is rejected with 409; no soft delete.

### Q22 Domain review decisions (leader, 2026-10-04; applied in BASE-06, PR #26)
- **D1 Offers are persisted.** A dispatch offer creates a `JOB_ASSIGNMENT` row in `OFFERED`; accepting moves it to `ASSIGNED`, an expired/declined offer to `CANCELLED`. `JOB_ASSIGNMENT.accepted_at` becomes nullable (SC-9). Reason: offers survive a restart, Admin can see who was offered, and the slot UNIQUE prevents double assignment.
- **D2 Slot uniqueness.** The unique index `JOB_ASSIGNMENT(slot_id)` is filtered on `assignment_status NOT IN ('CANCELLED','CANCELLED_BY_WORKER','REASSIGNED')`: a worker who cancels gets the slot back; the violation still counts (Q15).
- **D3 `DISPUTE_TICKET.fault_party`** = `FREELANCER` | `AGENCY` | `CUSTOMER`; null = nobody at fault (dismissed). The value selects the penalty: FREELANCER -> payout deduction / lock, AGENCY -> SLA points + escrow (Q09).
- **D4** The `ADMIN` table maps to the C# entity `AdminAccount` (avoids a clash with the `Admin` module namespace). Table name unchanged.
- **D5 Money rounding** has one implementation, `Vnd` in the Domain (`Vnd.Round`, `Vnd.Commission`, `Vnd.Net`), following G-2. Every module uses it. The template value objects `Money` and `Address` are deleted by BASE-09.
- **D6 `WORKER.work_status = BUSY`** means on site: from check-in until the assignment is `COMPLETED` or an absence is approved. Accepting a future assignment does not change `work_status`; double booking is prevented by the slot UNIQUE. Dispatch offers jobs only to `IDLE` workers with a free slot.
- **D7 Order status follows its assignments.** `ASSIGNED` when `required_workers` assignments are accepted; `COMPLETED` when that many are `COMPLETED`; back to `DISPATCHING` when a seat is lost (worker cancel, incident, reassignment): only the missing seat is re-dispatched. An `ABSENT` assignment is decided by the Admin approval flow (Q10). The BR-02 fallback (one worker, two consecutive shifts) is two assignments of the same worker.

### Q23 Booking details (contract `.spec/contracts/booking.md` section 6; recommended defaults, approval relayed by M2 from the leader's private messages on 2026-10-10, see issues #221, #246, #247, #248)
- **B1 Shift codes** are `SHIFT_MORNING` / `SHIFT_AFTERNOON` / `SHIFT_EVENING` everywhere (as `workers.md` section 2.3). Dispatch's `SANG/CHIEU/TOI` is aligned by an M3 fix ticket.
- **B2 Address port.** Booking reads the customer's address only through `ICustomerAddressQuery.GetOwnedAsync(customerId, addressId)` -> `{ addressId, totalAreaM2, latitude, longitude }` or null (not owned / unknown). Implementer M1; Fake first.
- **B3 Booking horizon.** Economy: the shift start only has to be in the future. Both tiers: `scheduledDate` at most `Booking.MaxDaysAhead` (14) days after today (local date); a later date is a 400 validation error on `scheduledDate`.
- **B7 `order_code`** = `GV` + `yyMMdd` (local date) + 6 random uppercase alphanumerics (14 characters); retry on a UNIQUE clash.
- **B8 Capacity reservation** is keyed by the order id (`CapacityRequest.OrderId`); `JOB_ORDER` gets no column. Booking inserts the order and reserves in one transaction; no capacity -> rollback, 409 `FULLY_BOOKED`, no order.

### Deferred (do NOT implement; ask the leader first)
- **Q04b** VietQR / real-money payment. **Q05b** real eKYC provider. **Q06b** real SMS provider. **Q07b** FCM push. 2FA for Partner. Masked calling. Bank-specific transfer file formats. Automatic SLA recovery.

## 3. Schema changes (authoritative until `Backend/GiupViec_Physical_DB_MVP5.drawio` is updated; BASE-06 must update the drawio or record the delta in `Backend/ARCHITECTURE.md`)
- **SC-1** `PARTNER_AGENCY`: add `password_hash VARCHAR(255) NULL`, `guarantee_signed_at DATETIME2 NULL`, `guarantee_file_url NVARCHAR(500) NULL`.
- **SC-2** New table `PRICE_RULE`: `rule_id INT IDENTITY PK`, `service_tier VARCHAR(10)`, `area_bracket VARCHAR(20)`, `unit_price DECIMAL(18,2)`, `is_active BIT`, `updated_at DATETIME2`, `updated_by INT NULL FK ADMIN`; `UNIQUE(service_tier, area_bracket)`.
- **SC-3** New table `ADMIN_AUDIT_LOG` (append-only): `log_id BIGINT IDENTITY PK`, `actor_type VARCHAR(10)` (`ADMIN`|`SYSTEM`), `admin_id INT NULL FK ADMIN`, `entity_type VARCHAR(30)`, `entity_id VARCHAR(40)`, `field_name VARCHAR(50)`, `old_value NVARCHAR(500) NULL`, `new_value NVARCHAR(500) NULL`, `reason NVARCHAR(255) NULL`, `changed_at DATETIME2`.
- **SC-4** New table `ESCROW_TRANSACTION` (append-only): `escrow_txn_id BIGINT IDENTITY PK`, `agency_id INT FK`, `txn_type VARCHAR(10)` (`DEPOSIT`|`PENALTY`|`REVERSAL`), `amount DECIMAL(18,2)`, `balance_after DECIMAL(18,2)`, `sla_points_delta DECIMAL(5,2) NULL`, `order_id BIGINT NULL FK`, `dispute_id INT NULL FK`, `payment_id BIGINT NULL FK`, `reason NVARCHAR(255)`, `created_by_admin_id INT NULL FK ADMIN`, `created_at DATETIME2`.
- **SC-5** `TWO_WAY_RATING`: add `UNIQUE(assignment_id, rater_role)`.
- **SC-6** New table `OTP_CODE`: `otp_id BIGINT IDENTITY PK`, `phone_number VARCHAR(15)`, `role VARCHAR(10)` (`Customer`|`Worker`), `code_hash VARCHAR(255)` (keyed HMAC-SHA256, never the code), `attempt_count TINYINT`, `requested_ip VARCHAR(45)`, `created_at DATETIME2`, `expires_at DATETIME2`, `consumed_at DATETIME2 NULL`; indexes `(phone_number, role, created_at)` and `(requested_ip, created_at)`. (Q20)
- **SC-7** New table `REFRESH_TOKEN`: `refresh_token_id BIGINT IDENTITY PK`, `token_hash VARCHAR(128) UNIQUE`, `subject_role VARCHAR(10)`, `subject_id INT`, `family_id UNIQUEIDENTIFIER`, `created_at DATETIME2`, `expires_at DATETIME2`, `revoked_at DATETIME2 NULL`, `replaced_by_id BIGINT NULL`. (Q20)
- **SC-8** `ADMIN` and `PARTNER_AGENCY`: add `failed_login_count TINYINT NOT NULL DEFAULT 0` and `locked_until DATETIME2 NULL`. (Q20, Q16 lockout)
- **SC-9** `JOB_ASSIGNMENT.accepted_at` becomes `DATETIME2 NULL` (null while `OFFERED`, Q22 D1). The unique index on `slot_id` excludes `CANCELLED`, `CANCELLED_BY_WORKER`, `REASSIGNED` (Q22 D2).
- Table count becomes **27** (22 + `PRICE_RULE`, `ADMIN_AUDIT_LOG`, `ESCROW_TRANSACTION`, `OTP_CODE`, `REFRESH_TOKEN`; it was 25 before SC-6/SC-7). Owners: `PRICE_RULE` -> M2, `ESCROW_TRANSACTION` -> M5, `ADMIN_AUDIT_LOG` -> M6 (others write to it through the `IAuditLog` port), `OTP_CODE` and `REFRESH_TOKEN` -> M1.

## 4. Configuration keys and DEFAULT values (single source for `BusinessRules`)
| Key | DEFAULT | Source |
|---|---|---|
| `Shift.MaxHours` | 4 | PRD BR-01 |
| `Area.StandardMaxM2` | 80 | PRD BR-01/02 |
| `Gps.CheckInToleranceMeters` | 100 | PRD BR-04 |
| `Dispatch.RadiusStepsKm` | 5, 7, 10 | PRD BR-03 |
| `Dispatch.OfferTimeoutSeconds` | 30 | PRD BR-03 |
| `Commission.Freelancer` | 0.200 | PRD |
| `Absence.FeeRate` / `MinWaitMinutes` / `MinCallAttempts` / `CustomerDisputeHours` | 0.40 / 15 / 2 / 24 | PRD BR-05 + Q10 |
| `Vol.Threshold` / `Vol.ResizeWidthPx` | 100.0 / 640 | Q03 |
| `Photos.MinPerPhase` / `MaxPerPhase` | 3 / 5 | PRD BR-06 |
| `Payments.QrExpiryMinutes` / `ReconcileAfterMinutes` / `ReconcileIntervalSeconds` | 15 / 3 / 60 | Q04 |
| `Ekyc.AutoApproveConfidence` / `Fake.Confidence` / `AuditRate` / `FullAuditFirstJobs` | 85.00 / 92.00 / 0.20 / 5 | Q05 |
| `Otp.Length` / `TtlMinutes` / `MaxAttempts` / `ResendCooldownSeconds` / `MaxPerPhonePerHour` / `MaxPerIpPerHour` | 6 / 5 / 5 / 60 / 5 / 20 | Q06 |
| `Subscription.GraceDays` | 7 | Q08 |
| `Agency.MinEscrowBalance` | 5000000 | Q09 |
| `Sla.InitialScore` / `Penalty.NoShow` / `Penalty.Shortage` / `Penalty.QualityComplaint` / `WarnAtOrBelow` / `BlockPremiumBelow` / `AppealHours` | 100.00 / 20 / 10 / 5 / 70 / 50 / 48 | Q09 |
| `SuperFreelancer.MinRating` / `MinCompletedJobs` / `NoUpheldDisputeDays` / `RevokeBelowRating` | 4.80 / 50 / 180 / 4.70 | Q12 |
| `Premium.MinLeadHours` | 4 | Q13 |
| `Booking.MaxDaysAhead` | 14 | Q23 B3 |
| `Rating.WindowHours` / `LowStarThreshold` / `ConsecutiveLowToFlag` / `MinAvgAfterJobs.Rating` / `MinAvgAfterJobs.Jobs` | 48 / 2 / 3 / 4.00 / 10 | Q14 |
| `Cancel.FullRefundHoursBefore` / `LateFeeRate` / `WorkerCancelLockCount` / `WorkerCancelWindowDays` | 2 / 0.40 / 3 / 30 | Q15 |
| `Auth.MinPasswordLength` / `LockoutFailures` / `LockoutMinutes` | 10 / 5 / 15 | Q16 |
| `Privacy.PhoneVisibleBeforeMinutes` / `PhoneVisibleAfterMinutes` | 60 / 60 | Q17 |
| `Auth.AccessTokenMinutes` / `Auth.RefreshTokenDays` | 15 / 30 | Q20 (leader accepted the default) |
| `Auth.RegistrationTokenMinutes` | 30 | Q20 (**suggestion**, not decided by the leader) |
| `Customer.InitialTrustScore` | 0.00 | Q21 (**provisional**, nothing reads it in the MVP) |
| `Address.RoomMaxAreaM2` | 30 | Q21, PRD section 1.1 |
| `Address.HouseMaxFloors` | 10 | Q21 (**suggestion**, not in the PRD) |

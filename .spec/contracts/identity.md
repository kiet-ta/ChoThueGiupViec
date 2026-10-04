# Contract: Identity (module `Identity`, owner M1)

> Status: **leader answers of 2026-10-04 applied (section 5); awaiting final approval** (ticket BE-M1-00, issue #21). Nobody codes an endpoint that is not in an approved contract.
> Sources: `.spec/spec.md` §1.3, §4.1 · `.spec/decisions.md` Q06 (OTP), Q16 (auth), G-6/G-7 · `.spec/plan/00-overview.md` §4 (ports) · `Backend/GiupViec_Physical_DB_MVP5.drawio` (`CUSTOMER`, `ADMIN`, `WORKER`, `PARTNER_AGENCY`).
> Implements tickets: BE-M1-01, BE-M1-02, BE-M1-03. Customer profile/addresses/favorites: see `customers.md`.
> Items marked **Ox** were not decided by the PRD or `decisions.md`; they are answered in section 5. Schema additions required by this contract: section 3 (to be recorded in `decisions.md` section 3 by ticket DECISIONS-01).

## 1. Conventions (shared with `customers.md`)

- Base path `/api`. JSON, UTF-8. Property names **camelCase**. Timestamps ISO-8601 **UTC** (`2026-10-04T10:00:00Z`).
- Every response is the envelope `ApiResponse<T>`: `{ "success": bool, "message": string, "data": T | null }` (`Backend/Application/Common/Models/ApiResponse.cs`).
  - 2xx: `success = true`. Non-2xx: `success = false`, `data = null` (validation: see O5), `message` is human readable and **not** part of the contract (clients branch on the HTTP status, never on the text).
  - Success responses always carry a JSON body (200/201). No `204`: the wrapper has no meaning for an empty body.
- **Known gap in the current code (must be fixed by BASE-11 before this contract can be true):**
  - `Backend/Middleware/ResponseWrapperMiddleware.cs:35,40` wraps every JSON response with `ApiResponse<object>.Ok(...)` whatever the status code, so a 4xx/5xx body would still say `success: true`.
  - `Backend/Middleware/ResponseWrapperMiddleware.cs:56` serializes with default `System.Text.Json` options, so, by code reading (not run), the envelope keys come out `Success/Message/Data` (PascalCase), not camelCase.
  - `Backend/Middleware/ErrorHandlingMiddleware.cs:27` returns HTTP 500 for every exception (including NotFound/Validation/Forbidden) and its body is not the envelope; it also echoes `ex.Message` (`:32`).
  - `Backend/Application/Exceptions/ValidationException.cs:3` and `ForbiddenAccessException.cs:3` are empty classes.
  - Required by this contract: exception -> status mapping below, envelope keys camelCase, `success=false` for non-2xx, no internal message in 500. Reported to the BASE-11 ticket (#16).
- Status code meaning used by all endpoints:

| Status | Meaning |
|---|---|
| 200 / 201 | OK / created |
| 400 | malformed or invalid input (validation) |
| 401 | not authenticated, wrong credentials, wrong/expired OTP, invalid token |
| 403 | authenticated but role not allowed, or account locked/disabled (see O6) |
| 404 | resource does not exist **or is not owned by the caller** (no existence leak) |
| 409 | conflict with current state (business rule) |
| 423 | account temporarily locked after repeated failed password logins (Q16) |
| 429 | rate limit / cooldown (Q06). Header `Retry-After: <seconds>` |
| 500 | unexpected; body has no internal detail |

- Roles (Q16, exactly four): `Customer`, `Worker`, `Partner`, `Admin`. Authorization policies: `CustomerOnly`, `WorkerOnly`, `PartnerOnly`, `AdminOnly` (names are part of this contract: other modules use them).
- Authentication header: `Authorization: Bearer <accessToken>`.
- Access token (JWT) claims: `sub` = numeric id of the account in its own table (customer_id / worker_id / agency_id / admin_id), `role` = one of the four roles, `jti` = unique id. No phone, email or name in the token.
- `ICurrentUser` (BASE-03/BASE-11) exposes `Id`, `Role`, `IsAuthenticated` from these claims.

## 2. Endpoints

### 2.1 `POST /api/auth/otp/request` (anonymous)
Sends a 6-digit OTP to a phone (Customer or Worker login, Q06). Dev uses the Fake `IOtpSender` that writes the code to the console log; the Fake is refused outside Development (startup fails).

Request
```json
{ "phoneNumber": "string", "role": "Customer | Worker" }
```
Response 200 `data`
```json
{ "expiresInSeconds": 300, "resendAvailableInSeconds": 60 }
```
(values come from `Otp.TtlMinutes` and `Otp.ResendCooldownSeconds`; they are echoed, never hard-coded by clients)

| Status | Condition |
|---|---|
| 400 | `phoneNumber` invalid (see O4) or `role` not `Customer`/`Worker` |
| 429 | resend cooldown not elapsed, or more than `Otp.MaxPerPhonePerHour` per phone, or `Otp.MaxPerIpPerHour` per IP. `Retry-After` set |

Rules: only a **hash** of the code is stored (Q06), in table `OTP_CODE` (section 3). A new request invalidates the previous unconsumed code for that phone+role. Cooldown and the per-phone / per-IP hourly limits are computed by counting `OTP_CODE` rows (`created_at`, `requested_ip`). For role `Worker` the response is identical whether or not a Worker exists for the phone (no account enumeration).

### 2.2 `POST /api/auth/otp/verify` (anonymous)
Request
```json
{ "phoneNumber": "string", "role": "Customer | Worker", "code": "string (6 digits)" }
```
Response 200 `data` (`AuthResult`, also used by 2.3 and 2.4)
```json
{
  "tokenType": "Bearer",
  "accessToken": "string",
  "accessTokenExpiresInSeconds": 0,
  "refreshToken": "string",
  "user": { "id": 0, "role": "Customer | Worker", "isNewUser": false }
}
```

| Status | Condition |
|---|---|
| 400 | malformed phone or `code` is not 6 digits |
| 401 | code wrong or expired (one response for both: no hint) |
| 403 | account locked/disabled (O6) |
| 429 | more than `Otp.MaxAttempts` wrong tries for the current code (the code is then invalidated; a new `otp/request` is needed) |

Rules:
- **Customer:** first successful verification creates the `CUSTOMER` row (`phone_number`, `otp_verified_at = now`, other columns per `customers.md` §2.1) and returns `isNewUser = true`. Later logins set `otp_verified_at = now` and return `isNewUser = false`.
- **Worker with an existing `WORKER` row** (registered by M4, or imported by an Agency): normal login, `AuthResult` above.
- **Worker with no `WORKER` row yet (new freelancer, decision O1):** the phone is verified but the person **must complete a profile first**. The response is `200` with `data` = `RegistrationRequired` (no access/refresh token):
```json
{ "isNewUser": true, "registrationToken": "string", "registrationTokenExpiresInSeconds": 0 }
```
  `registrationToken` is a signed, single-purpose token (claims: purpose `worker_registration`, the verified phone, `jti`). It is accepted **only** by the M4 registration endpoint (defined in `workers.md`, BE-M4-00), never as a bearer token. Lifetime: `Auth.RegistrationTokenMinutes` (decision O2 list). After registration (and eKYC, decisions Q05) the worker logs in normally. Until the profile exists the account cannot receive jobs (Q05).
- A code is single-use: a correct verification consumes it.

### 2.3 `POST /api/auth/password/login` (anonymous)
Admin and Partner login (Q16): email + password. Password policy (`Auth.MinPasswordLength` 10, upper + lower + digit) is enforced when passwords are **created** (seed/Partner registration), not on login.

Request
```json
{ "email": "string", "password": "string", "role": "Admin | Partner" }
```
Response 200 `data`: `AuthResult` (`user.role` = `Admin` or `Partner`, `isNewUser` always `false`).

| Status | Condition |
|---|---|
| 400 | missing/invalid email format, empty password, `role` not `Admin`/`Partner` |
| 401 | unknown email or wrong password (same response for both) |
| 403 | account disabled (`ADMIN.is_active = 0`, or Partner not allowed to log in, see O6) |
| 423 | `Auth.LockoutFailures` (5) consecutive failures: locked for `Auth.LockoutMinutes` (15). `Retry-After` set. A correct password during lock still returns 423 |

Rules: failure counting and the lock use `failed_login_count` / `locked_until` on `ADMIN` and `PARTNER_AGENCY` (section 3). Admin looks up `ADMIN.email`; Partner looks up `PARTNER_AGENCY.contact_email` and `password_hash` (new column SC-1). Hash verification only through `IPasswordHasher`. Passwords/hashes are never logged or returned. A successful login resets the failure counter. 2FA is deferred (Q16).

### 2.4 `POST /api/auth/refresh` (anonymous)
Request `{ "refreshToken": "string" }` -> 200 `AuthResult` with a **new** access token and a **new** refresh token (rotation). The previous refresh token stops working. Refresh tokens are opaque random strings; only their hash is stored in `REFRESH_TOKEN` (section 3).

| Status | Condition |
|---|---|
| 400 | missing token |
| 401 | token invalid, expired, revoked or already used (reuse of a rotated token revokes the whole chain) |

### 2.5 `POST /api/auth/logout` (any authenticated role)
Request `{ "refreshToken": "string" }` -> 200 `data: null`. Revokes that refresh token. Idempotent: an unknown/already revoked token still returns 200.

| Status | Condition |
|---|---|
| 401 | no/invalid access token |

### 2.6 `GET /api/auth/me` (any authenticated role)
Response 200 `data`: `{ "id": 0, "role": "Customer | Worker | Partner | Admin" }`. Used by the Web/Mobile role guard (WEB-BASE-02, MOB-BASE-03). Profile details live in the module that owns the account (`GET /api/customers/me`, etc.).

| Status | Condition |
|---|---|
| 401 | no/invalid access token |

## 3. Schema additions (M1 owns all schema changes)
Required by this contract; they become SC-6..SC-8 in `decisions.md` section 3 (ticket DECISIONS-01) and are built in BASE-06/07. Table count becomes **27**.

**SC-6 `OTP_CODE`** (new, append-only except `attempt_count`/`consumed_at`): `otp_id BIGINT IDENTITY PK`, `phone_number VARCHAR(15)`, `role VARCHAR(10)` (`Customer`|`Worker`), `code_hash VARCHAR(255)`, `attempt_count TINYINT`, `requested_ip VARCHAR(45)`, `created_at DATETIME2`, `expires_at DATETIME2`, `consumed_at DATETIME2 NULL`. Indexes `(phone_number, role, created_at)` and `(requested_ip, created_at)`. The hash is a keyed HMAC-SHA256 (key from user-secrets/environment, never in the repo), not a plain hash, because the code space is only 10^6.

**SC-7 `REFRESH_TOKEN`** (new): `refresh_token_id BIGINT IDENTITY PK`, `token_hash VARCHAR(128) UNIQUE`, `subject_role VARCHAR(10)`, `subject_id INT`, `family_id UNIQUEIDENTIFIER`, `created_at DATETIME2`, `expires_at DATETIME2`, `revoked_at DATETIME2 NULL`, `replaced_by_id BIGINT NULL`. Presenting an already rotated token revokes every row of the same `family_id`.

**SC-8 lockout columns** on `ADMIN` and on `PARTNER_AGENCY`: `failed_login_count TINYINT NOT NULL DEFAULT 0`, `locked_until DATETIME2 NULL`.

Old `OTP_CODE` / `REFRESH_TOKEN` rows are deletable; a retention cleanup is a later ticket (not part of this contract).

## 4. Out of scope / owned elsewhere
- Worker registration, eKYC, import: M4 (`workers.md`) and M5 (`agencies.md`). Partner registration and creating `PARTNER_AGENCY.password_hash`: M5. Admin creation: dev seed (BASE-10) and M6 (BE-M6-06). Identity only authenticates them.
- Real SMS provider (Q06b), FCM (Q07b), 2FA: deferred, **not implemented**.

## 5. Decisions (leader, 2026-10-04)

| # | Question | Decision |
|---|---|---|
| **O1** | New freelancer verifies OTP before a `WORKER` row exists. | **The person must complete a profile.** `otp/verify` returns `RegistrationRequired` with a single-purpose `registrationToken` (section 2.2); M4's registration endpoint accepts it. No access token is issued. |
| **O2** | Token lifetimes. | Recommended default accepted: access token 15 min (`Auth.AccessTokenMinutes`), refresh token 30 days (`Auth.RefreshTokenDays`), both in `BusinessRules`. Same rule for `Auth.RegistrationTokenMinutes`; **suggested 30**, not decided by the leader: it is a config value and can be changed without code. |
| **O3** | Where OTP and related state live. | **Database, not cache.** New table `OTP_CODE` (SC-6). Lockout counters go into the existing `ADMIN` and `PARTNER_AGENCY` tables (SC-8). Refresh-token state needs its own table `REFRESH_TOKEN` (SC-7): this part extends the answer in the same direction and the leader may overrule it. |
| **O4** | Phone format. | Recommended default accepted: Vietnamese mobile numbers, normalized to digits only in national form `0XXXXXXXXX` (strip spaces, `+84` -> `0`), anything else is 400. |
| **O5** | Validation error shape. | Recommended default accepted: `400` with `data: { "errors": { "<field>": ["<message>"] } }`, `success = false`. |
| **O6** | Account states. | Recommended default accepted: customers `ACTIVE` / `LOCKED` (LOCKED -> 403); a Partner may log in even when `SUSPENDED` (Q09), only Admin deactivation blocks login. |

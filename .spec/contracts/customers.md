# Contract: Customers (module `Customers`, owner M1)

> Status: **DRAFT, awaiting leader approval** (ticket BE-M1-00, issue #21).
> Sources: `.spec/spec.md` §1.1 (S_total), §1.3, §4.1 step 1 · `.spec/decisions.md` G-2/G-3/G-7, Q16 · `Backend/GiupViec_Physical_DB_MVP5.drawio` tables `CUSTOMER`, `CUSTOMER_ADDRESS`, `FAVORITE_WORKER`.
> Implements tickets: BE-M1-04 (profile), BE-M1-05 (addresses), BE-M1-06 (favorite workers).
> Conventions (envelope, camelCase, UTC, status codes, roles, policies, 404-for-not-owned) are defined in `identity.md` §1 and apply here unchanged. Customer login: `identity.md` §2.1-2.2.
> **OPEN Ox** = not decided by the PRD or `decisions.md`; a recommended default is given, the leader answers in issue #21.

All endpoints require `Authorization: Bearer <accessToken>` with policy **`CustomerOnly`**. `401` if missing/invalid, `403` if the role is not `Customer`. A customer can only read or change **their own** data; the id always comes from the token (`ICurrentUser.Id`), never from the body or URL.

## 1. Data shapes

`CustomerProfile`
```json
{
  "customerId": 0,
  "phoneNumber": "string",
  "fullName": "string",
  "email": "string | null",
  "trustScore": 0.0,
  "accountStatus": "string",
  "createdAt": "ISO-8601 UTC"
}
```
(`CUSTOMER`: `customer_id, phone_number, full_name, email, trust_score DECIMAL(3,2), account_status, created_at`. `otp_verified_at` and `updated_at` are not exposed.)

`Address`
```json
{
  "addressId": 0,
  "label": "string",
  "addressLine": "string",
  "district": "string",
  "city": "string",
  "housingType": "APARTMENT | HOUSE | ROOM",
  "floorAreaM2": 0.0,
  "numFloors": 0,
  "totalAreaM2": 0.0,
  "bedrooms": 0,
  "bathrooms": 0,
  "latitude": 0.0,
  "longitude": 0.0,
  "isDefault": false,
  "createdAt": "ISO-8601 UTC"
}
```
(`CUSTOMER_ADDRESS`.) `bedrooms` and `bathrooms` may be `null`.

`FavoriteWorker`
```json
{ "workerId": 0, "addedAt": "ISO-8601 UTC" }
```
(`FAVORITE_WORKER`: composite key `customer_id + worker_id`, `created_at`. Display fields such as name and rating: see C3.)

## 2. Endpoints

### 2.1 Profile (BE-M1-04)

**`GET /api/customers/me`** -> 200 `data: CustomerProfile`. 404 only if the row was removed (should not happen).

A `CUSTOMER` row is created by the first successful OTP login (`identity.md` §2.2) with: `phone_number` from the login, `full_name` empty string until the customer sets it (see C1), `email` null, `otp_verified_at = now`, `trust_score` = C2, `account_status` = `ACTIVE` (`identity.md` O6), `created_at = updated_at = now (UTC)`.

**`PUT /api/customers/me`** (full replace of the editable fields)
```json
{ "fullName": "string", "email": "string | null" }
```
-> 200 `data: CustomerProfile`.

| Status | Condition |
|---|---|
| 400 | `fullName` empty/whitespace or longer than 100 characters; `email` present but not a valid address or longer than 255 characters |

`phoneNumber` is **not** editable here (it is the login identity). `trustScore` and `accountStatus` are read-only for the customer. `updated_at` is set by the server.

### 2.2 Address book (BE-M1-05)

**S_total (PRD §1.1):** `totalAreaM2 = floorAreaM2 x numFloors`, computed **by the server** (the drawio column `total_area_m2` is a computed column). The client never sends `totalAreaM2`; if it does, the value is ignored. The result is rounded to 2 decimals (round half away from zero, same rule as G-2) and returned in every `Address`.

**`GET /api/customers/me/addresses`** -> 200 `data: Address[]` ordered default first, then `createdAt` descending. Empty list is a valid 200.

**`POST /api/customers/me/addresses`** -> 201 `data: Address`
```json
{
  "label": "string",
  "addressLine": "string",
  "district": "string",
  "city": "string",
  "housingType": "APARTMENT | HOUSE | ROOM",
  "floorAreaM2": 0.0,
  "numFloors": 0,
  "bedrooms": 0,
  "bathrooms": 0,
  "latitude": 0.0,
  "longitude": 0.0,
  "isDefault": false
}
```

Validation (all failures -> 400):

| Field | Rule | Origin |
|---|---|---|
| `label` | required, 1-50 chars | `NVARCHAR(50)` |
| `addressLine` | required, 1-255 chars | `NVARCHAR(255)` |
| `district`, `city` | required, 1-100 chars | `NVARCHAR(100)` |
| `housingType` | one of `APARTMENT`, `HOUSE`, `ROOM` (C4) | PRD §1.1 lists 3 housing types; `VARCHAR(12)` |
| `floorAreaM2` | > 0 and <= 9999.99 | `DECIMAL(6,2)` |
| `numFloors` | integer 1-255 | `TINYINT`; PRD §1.1 (apartment = 1 flat floor); tighter bounds are C4 |
| `bedrooms`, `bathrooms` | optional, integer 0-255 | `TINYINT NULL` |
| `latitude` | -90 to 90 | GPS; `DECIMAL(9,6)` |
| `longitude` | -180 to 180 | GPS; `DECIMAL(9,6)` |

Default address rules: the customer's **first** address becomes default automatically (`isDefault` in the request is ignored). Sending `isDefault = true` on a later address makes it the default and clears the flag on the previous default **in the same transaction**; there is never more than one default per customer.

**`GET /api/customers/me/addresses/{addressId}`** -> 200 `data: Address`. 404 if it does not exist **or belongs to another customer**.

**`PUT /api/customers/me/addresses/{addressId}`** (full replace; same body and validation as POST) -> 200 `data: Address`. 404 as above. Editing an address does **not** change existing orders: orders freeze their own price at creation (decisions Q01) and the area used for that order was read at order time.

**`DELETE /api/customers/me/addresses/{addressId}`** -> 200 `data: null`. 404 as above. 409 when the address is referenced by an order (`JOB_ORDER.address_id` is a foreign key, drawio): the address cannot be removed while orders point at it (C5). If the default address is deleted, the most recently created remaining address becomes default (none left -> no default).

### 2.3 Favorite workers (BE-M1-06)

**`GET /api/customers/me/favorite-workers`** -> 200 `data: FavoriteWorker[]` ordered by `addedAt` descending.

**`PUT /api/customers/me/favorite-workers/{workerId}`** -> 200 `data: FavoriteWorker`. **Idempotent**: adding a worker already in the list returns the existing row, changes nothing, no error (double-click safe).

| Status | Condition |
|---|---|
| 404 | no `WORKER` with that id (existence check goes through a port, C3) |

**`DELETE /api/customers/me/favorite-workers/{workerId}`** -> 200 `data: null`. Idempotent: removing a worker that is not in the list is still 200.

## 3. Out of scope / owned elsewhere
- Creating orders, price, slots: M2 (`booking.md`). The order screen reads addresses only through this module's endpoints; Booking never reads `CUSTOMER_ADDRESS` directly (modules talk through ports/events only, overview §3.5).
- Worker profile data shown in the favorites list: M4 owns `WORKER` (C3).
- Phone privacy for workers (Q17) and ratings of customers (Q14, M6): not part of this module.

## 4. OPEN questions (leader decides; recommendation first)

| # | Question | Recommended default | Why it matters |
|---|---|---|---|
| **C1** | `full_name` is `NOT NULL` but a new customer only gave a phone. | Insert an empty string at first login; the app asks the customer to complete the profile before the first booking (client rule, not enforced by Customers). | Avoids blocking login on a name the PRD does not ask for at login. |
| **C2** | Initial `trust_score` (`DECIMAL(3,2)`) and who changes it. The PRD says only "điểm tin cậy" (§1.3, §5.3); neither the PRD nor decisions give a scale or a start value. | No default proposed: the leader picks the scale (for example 0.00-5.00 like ratings) and the initial value. In this ticket the field is read-only and written only when the row is created. | A guessed default silently becomes business data. |
| **C3** | The favorites list needs the worker's name/rating (MOB-M1-03), but `WORKER` belongs to M4 and modules cannot read each other's tables. | Add one read port `IWorkerProfileQuery` (implementer M4, Fake first) at BASE-03, returning `workerId, fullName, ratingAvg, completedJobs, workStatus` and an existence check; extend `FavoriteWorker` with those fields. Until the port exists the endpoint returns only `workerId, addedAt`. | Needs a port added to overview §4 (M1 owns ports). |
| **C4** | Bounds beyond the DB types: allowed `numFloors` per `housingType` and max `floorAreaM2`. PRD: apartment = 1 flat floor; room <= 30 m2; house = multi-floor. | `APARTMENT` and `ROOM`: `numFloors = 1`. `ROOM`: `floorAreaM2 <= 30`. `HOUSE`: `numFloors` 1-10 (my suggestion, not PRD). The wire code for housing type is `APARTMENT/HOUSE/ROOM` because `VARCHAR(12)` must fit them. | Prevents impossible areas feeding the price bracket (decisions Q01 uses `total_area_m2`). |
| **C5** | Deleting an address that old orders reference. No `is_deleted` column exists and schema is M1-only. | Reject with 409 as written in §2.2 (the FK would fail anyway). If the leader wants "hide but keep", add a column through a schema change (SC-6). | Without a rule the DB error would surface as a 500. |

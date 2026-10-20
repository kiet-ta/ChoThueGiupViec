# End-to-end smoke test of the main flows

`main_flow.py` drives a running backend over HTTP and checks every step of the two main business flows. Python 3.9+, **standard library only** (nothing to install).

| Flow | Steps |
|---|---|
| **Premium** (agency) | agency registers with a bank account, a duplicate tax code is a 409 -> escrow deposit -> guarantee + worker import (template downloaded through the API, dry-run, commit) -> roster -> Pro package (Verified Partner label) -> customer books a Premium shift and pays -> the order is assigned to an agency worker -> check-in (GPS) -> BEFORE photos (the first one starts the work) -> AFTER photos -> completion -> customer accepts (Pro commission 0) |
| **Economy** (freelancer) | OTP registration -> eKYC auto-approve -> open a slot, share the location, set the bank account -> customer books an Economy shift and pays -> the freelancer gets the offer (30 s) and accepts -> check-in -> photos -> completion -> customer accepts (payout 80 %) -> earnings |
| **Payout** | the completions are dated to last month -> payout batch (agency and freelancer items carry their bank accounts, the export file too) -> confirm -> the freelancer sees the transfer |

Payments are confirmed with the development tool (`/api/dev/payments`, Development only), OTPs are read from the backend console (Fake OTP sender).

## Run

1. **A SQL Server and a database with the migrations** (a fresh database for every run is the cleanest):
   ```bash
   cd Backend
   dotnet ef database update --connection "Server=localhost;Database=E2E;Integrated Security=True;TrustServerCertificate=True;"
   ```
2. **The backend in Development, its console written to a file** (the Fake OTP is read from it):
   ```bash
   export ASPNETCORE_ENVIRONMENT=Development
   export ConnectionStrings__DefaultConnection="Server=localhost;Database=E2E;Integrated Security=True;TrustServerCertificate=True;"
   export ASPNETCORE_URLS=http://localhost:5004
   dotnet run --no-launch-profile --project CommonService.csproj > /tmp/backend.log 2>&1
   ```
   The first start creates the dev admin (`Seed:Admin:*` of `Backend/appsettings.Development.json`).
3. **The script**, from the repository root:
   ```bash
   python scripts/e2e/main_flow.py --backend-log /tmp/backend.log --admin-password "<Seed:Admin:Password>" --database E2E
   ```

Options: `--only premium|economy`, `--base-url` (default `http://localhost:5004`), `--sql-server` (default `localhost`), `--database` (default `ChoThueGiupViec`), `--sqlcmd` (path of `sqlcmd`, default `sqlcmd` on `PATH`), `--admin-email`, `-v` (print every call). The admin password can also come from `E2E_ADMIN_PASSWORD`.

Output: one `PASS` / `FAIL` line per check and a last line `RESULT: ALL PASS` (exit code 0) or the list of failed checks (exit code 1).

## What it needs and what it changes directly in the database

- `sqlcmd` is used for three things only: the ids of the imported workers (no API lists them), the status of the assignment after the first BEFORE photo, and **dating the completions to last month** (a payout month must be over; the only change made behind the API's back).
- Every run creates its own agency, workers, customers and orders with random numbers. It picks a day with no slot yet, so older runs in the same database do not take its workers; if every day of the booking horizon is taken, use a fresh database.
- The Economy flow waits out the 60 s OTP cooldown of its phone once (the freelancer registers, then signs in).

## What it does not cover

The Mobile and Web screens (it calls the API they use), a real payment gateway / SMS / eKYC (all Fake), customer cancellation and refunds, disputes, ratings, the rescue of a failed shift, the 30-minute swap window and escrow penalties.

# Customer UAT scenarios (`customer_uat.py`)

The customer side of the Mobile UAT sheet, run over HTTP instead of by hand on an emulator. Python 3.9+, standard library only; it reuses
the helpers of `main_flow.py` and needs no database access.

| Id | Checks |
|---|---|
| KH-01 | an 8-digit phone is a 400; a resend inside the 60 s cooldown is a 429; wrong codes are 401 and the 5th a 429, after which the right code is dead; a new code logs in as `Customer`; a customer phone with role `Worker` gets no customer session |
| KH-02 | empty name, bad email, name over 100 characters are 400 with the field; a valid profile is saved and read back; the phone number does not change |
| KH-03 | label over 50 characters, room over 30 m2, area 0, 11 floors are 400 with the field; apartment 25 m2 (total 25, first address is the default), house 40 x 3 (total 120); one default only; an update recomputes the total; a deleted address is then 404 |
| KH-14 | the favourite workers list answers 200, empty for a new customer |
| KH-09 | a freelancer with an open slot and a position is offered the paid Economy order within 30 s and accepts; the progress lists it `ASSIGNED` and the order is `ASSIGNED` |
| KH-16 | a two-worker Economy order with one freelancer: seat 1 accepted, then `AWAITING_CUSTOMER` for the afternoon of the same day; the customer agrees, the worker accepts the second-shift offer, the order is `ASSIGNED`. A second order where the customer declines is `CANCELLED` and its payment `REFUNDED` |
| KH-10 | an Economy order with no freelancer within 10 km is `CANCELLED` with the BR-03 reason and its payment `REFUNDED` |
| KH-12 | another customer has an empty address book and no order, and gets a 404 for the first customer's order and address |

## Run

Against the backend of a Docker compose stack (the Fake OTP is read with `docker compose logs backend`):
```bash
python scripts/e2e/customer_uat.py --compose-file docker-compose.yml
```
or against a backend whose console goes to a file: `--backend-log /tmp/backend.log`. Options: `--base-url` (default `http://localhost:5004`),
`--only KH-02 KH-09 ...` (KH-01 always runs: it signs customer A in), `-v` (print every call). It takes about 4 minutes (it waits out one OTP
cooldown and the BR-02 sweep).

- Every run uses new phone numbers. Each dispatch scenario sits at a random position about 150 km from Ho Chi Minh City centre, the
  scenarios 33 km apart, so the freelancers of the manual UAT data are never offered the run's orders and the KH-10 order has nobody in range.
- **OTP limit:** the backend allows 20 OTP requests per IP and hour (`Otp.MaxPerIpPerHour`) and a run needs 7. An emulator on the same PC
  shares that IP, so two runs in an hour leave little for manual logins; the script stops with a clear message when the limit is reached.

## What stays manual

The screens and their wording, the Android Back button, an app killed and reopened, a backend stopped while the app is open (KH-15), the
province / ward picker of the address form (KH-03b, it only sends text to the API) and a double tap on "Đặt đơn".

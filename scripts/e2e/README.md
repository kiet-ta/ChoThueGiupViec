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

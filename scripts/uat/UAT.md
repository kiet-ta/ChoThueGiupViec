# UAT guide: who does what

The team plays the actors (Admin, Partner agency, agency worker, freelancer, customer) against one shared environment. Everything below is the
Fake/sandbox build: no real money, no real SMS, no real eKYC.

## 1. Start the environment (one person, one machine)

```bash
docker compose up --build -d          # SQL Server, migrations, backend (5004), Web (8080); first build takes several minutes
python scripts/uat/seed.py --admin-password "<Seed:Admin:Password of Backend/appsettings.Development.json>"
```

| What | Where |
|---|---|
| Web (Admin, Partner portal, agency registration) | http://localhost:8080 |
| API / Swagger | http://localhost:5004 / http://localhost:5004/swagger |
| Stop / reset | `docker compose down` / `docker compose down -v` (also drops the database) |
| OTP codes (there is no SMS) | `docker compose logs backend \| findstr "FAKE OTP"` (Linux/macOS: `grep`) |

- **Everybody uses this one backend** so the actors see each other. The Mobile app calls `http://10.0.2.2:5004` (hard-coded), which is the PC running Docker **from an Android emulator on that same PC**. Several emulators on that PC are fine; a phone or another PC needs the address changed in the app (not supported yet).
- **Android emulator hangs on start?** Device Manager -> Cold Boot Now; if it still hangs: Edit -> Advanced -> Graphics: Software.
- **Payments:** a QR is shown but cannot be scanned. Pay with Admin -> sidebar **Công cụ dev -> Xác nhận thanh toán** (button "Xác nhận đã thanh toán"; it works only on this Development build).
- **Time:** the system uses the real clock of the Docker PC. A **Premium** shift must start at least **4 hours** from now and at most **14 days** ahead. Shifts: morning 08:00-12:00, afternoon 13:00-17:00, evening 17:30-20:30 (Vietnam time).
- **Position (GPS):** the app has no GPS sensor yet: it sends a fixed position (Hanoi centre) for the worker and for check-in. Customer and worker therefore match each other; do not expect real distances.

Sample data after the seed: agency `demo.agency@example.com` / `Demo-Passw0rd!` (ACTIVE, Pro, bank account set), workers `0911000001`, `0911000002`, `0911000003`, a roster for tomorrow and the next 6 days (morning and afternoon).

## 2. Roles and what each tester does

Record the result of every step in the table of section 4 (PASS / FAIL + what you saw). **Report a difference even when it looks minor**: parts of the Mobile app have not been run by the author of this guide.

### Admin (Web, role "Quản trị viên")
Login: `admin@dev.local` and the password of `Seed:Admin:Password`.
1. Open the sidebar: every group appears once; open "Gói thuê bao" (3 packages), "Bảng giá", "Giám sát điều phối".
2. After scenario S1 and S2: "Giám sát điều phối" shows the orders; "Kỳ payout" shows no batch for the current month (a month must be over: not testable now).
3. Whenever a tester says "pay": Công cụ dev -> Xác nhận thanh toán -> the payment appears -> confirm. Expect the message "Đã xác nhận thanh toán #n".

### Partner / agency (Web, role "Đối tác doanh nghiệp")
1. **Register a new company:** `/register` (link on the login page): fill the form with a new tax code (10 digits) and e-mail. Expected: success message; a duplicate tax code or e-mail is refused with a clear message. Sign in; the escrow page says the company is PENDING.
2. Escrow & SLA: top up 5.000.000 -> the QR appears -> Admin confirms the payment -> balance 5.000.000, status ACTIVE.
3. Add workers: sign the guarantee (any PDF), download the template, fill 2 rows, dry-run (a wrong row is shown with its error), commit. The Free package allows 3 workers.
4. Weekly roster: put a worker on tomorrow morning; putting the same worker twice changes nothing.
5. Subscription: buy "Pro monthly" -> Admin confirms -> "Pro" and the "Verified Partner" badge.
6. Bank account: "Tài khoản nhận tiền" -> save a name and 6-30 digits.
7. Or skip 1-6 and use the seeded demo agency.
8. After S1: "Đơn & hiệu suất" lists the job and the performance report (Pro).
9. 30-minute swap: see S3.

### Agency worker (Mobile, role "Chuyên Viên / Thợ")
Phones `0911000001..3`; sign in with the OTP from the log. The shift the agency rostered is visible in the agency schedule. During S1: accept nothing (the job is assigned by the system), check in, take photos (BEFORE x3 then AFTER x3, same angles), submit.

### Freelancer (Mobile, role "Chuyên Viên / Thợ")
1. Register with a new phone (OTP), full name, a 12-digit national id. 2. eKYC: send the three photos; the Fake provider approves at once. 3. Open "Lịch rảnh": switch **tomorrow morning ON** (this also sends the position; without it no job is ever offered). 4. During S2: the offer appears for 30 seconds: accept; check in; photos; submit.

### Customer (Mobile, role "Khách Hàng")
OTP sign-up, add an address (area decides the price and the number of workers), book, pay (Admin confirms), follow the order, accept the work.

## 3. Scenarios (several testers together)

**S1 Premium through the agency** - Customer + Admin + Agency worker + Partner.
1. Customer: book **Premium**, tomorrow morning. Expect: price from the quote; order `PENDING_PAYMENT`.
2. Customer: pay; Admin: confirm in the dev tool. Expect: within seconds the order is assigned to one of the demo workers (Partner "Đơn & hiệu suất" shows it).
3. Agency worker (the assigned one): check in (position matches), take 3 BEFORE and 3 AFTER photos (a photo whose file name contains `blur` is refused), submit.
4. Customer: accept. Expect: order completed; Partner sees the job completed, commission 0 (Pro).
5. Without a Pro package (new company): commission is 20 %.

**S2 Economy through a freelancer** - Customer + Admin + Freelancer.
1. Freelancer: registered, eKYC done, tomorrow-morning slot ON (section 2).
2. Customer: book **Economy**, tomorrow morning; pay; Admin confirms.
3. Freelancer: the offer appears (30 s); accept; check in; photos; submit.
4. Customer: accept. Expect: the freelancer's earnings show 80 % of the price.
5. Negative: let the 30 s pass without accepting: the order is offered onward or cancelled with a full refund when nobody is left.

**S3 Agency swap (30 minutes)** - Partner. Needs a job assigned to an agency worker more than 2 hours before it starts: Partner "Đổi thợ 30 phút" -> enter the shift id and a reason -> the countdown starts -> enter the id of another worker of the company -> "Đổi thợ". Expect: the job moves to that worker and the old worker's slot is free again.

**S4 Refusals** - registering the same tax code twice; importing a file with a duplicate phone; a PUT roster for a worker of another company; a blurry photo; a booking less than 4 hours ahead (Premium). Expect a clear message each time, never a blank error page.

## 4. Result table (copy it)

| Scenario / step | Tester | Result | What you saw (screen, message, time) |
|---|---|---|---|
| | | | |

## 5. Not covered yet

Real payment gateway, SMS, eKYC; real GPS; customer cancellation and refunds; disputes and ratings; failed-shift rescue; escrow penalties and appeals (no way to trigger one by hand); payout batches (need a finished month; `scripts/e2e/main_flow.py` covers them by dating the jobs to last month).

The automatic check of the backend flows is `scripts/e2e/main_flow.py` (see its README); run it before a UAT session to know the backend is healthy:
`python scripts/e2e/main_flow.py --backend-log <file> --admin-password ... --sql-server localhost,14330 --sql-user sa --sql-password ...` needs `sqlcmd` on the PC and a log file of the backend (`docker compose logs -f backend > backend.log`).

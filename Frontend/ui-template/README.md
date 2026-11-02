# Web UI templates (clickable HTML prototype)

30 static screens for the web app (plus `index.html`): Customer (new), Agency / Partner Portal (redesign of today's pages) and sign-in for the three roles, with an Admin login of its own.
Same idea as `Mobile/ui-templates/` (ticket #449): a design reference to approve **before** any React change. Nothing is wired to
the API; every number is sample data.

## How to view

- Open `index.html` in a browser (or `python3 -m http.server` in this folder): every screen in one grid, grouped by role, each with the route it would get.
- Fonts (Geist) and icons (Material Symbols Outlined) load from Google Fonts, so the first view needs network access. The React port uses `lucide-react`.
- Photos are Unsplash URLs (`images.unsplash.com`); each one returned HTTP 200 when this folder was generated.
- The public landing page (`/`) is a separate ticket (#569) and is not part of this folder's gallery.

## Screenshots

`screenshots/` holds six captures at about 1500 px width (customer home, booking, order detail; agency overview, worker list; admin login). The other screens were generated from the same components and checked for broken links, but not each one opened in a browser.

## Decisions taken (owner, 2026-11-01)

| Topic | Decision |
|---|---|
| Scope | Customer ~15 screens, Agency ~11, Admin = one login page of its own (the Admin console pages are not redrawn here). |
| Customer layout | Sticky top bar + content up to 1200 px, same look as the landing page. |
| Agency layout | 240 px sidebar (decision D3 of the Partner Portal). |
| Admin | Separate login page, visually different (dark, "Admin Console"); no register link, no role picker. |
| Look | Landing tokens: forest `#1A2F25` primary buttons and sidebar, 8 px controls, 12 px boxes, outline secondary button, Geist. |

Deviation to confirm: `DESIGN.md` names Newsreader (headlines) and Plus Jakarta Sans (body). These templates follow the landing page
(Geist, already installed in `Frontend`) because the owner asked for Customer to match the landing.

## Screen map

"new" = no such screen on the web today. "needs API" = the widget shows data no endpoint returns today.
Routes are proposals for the React port, not decided.

### Sign-in

| File | Proposed route | Data | Notes |
|---|---|---|---|
| `auth/customer-login.html` | `/login` | `POST /api/auth/otp/request` | new. Same phone + OTP flow as Mobile. |
| `auth/otp.html` | `/login/otp` | `POST /api/auth/otp/verify` | new. 6 digits, 5 tries, 60 s resend (Q06). |
| `agency/login.html` | `/partner/login` | `POST /api/auth/password/login` (role Partner) | Today `/login` has a role dropdown (Admin / Partner). |
| `agency/register.html` | `/register` | `POST /api/agencies/registration` | Fields as in `RegisterAgencyPage.tsx`. |
| `admin/login.html` | `/admin/login` | `POST /api/auth/password/login` (role fixed to Admin) | Shows the locked state (423 with Retry-After). 401 stays a generic message. |

### Customer (new surface, endpoints as in `Mobile/ui-templates/README.md`)

| File | Proposed route | Data | Notes |
|---|---|---|---|
| `customer/home.html` | `/app` | orders + progress, price-quote, favorite-workers | Spend chart: **needs API**. |
| `customer/booking.html` | `/app/booking` | `GET /api/booking/options`, `GET /api/booking/price-quote`, `POST /api/booking/orders` | Economy 260.000 / Premium 390.000 are Mobile's sample prices; real prices come from the server. |
| `customer/payment.html` | `/app/payments` | `POST /api/payments/orders/{id}/qr`, `GET /api/payments/{id}` every 3 s | SANDBOX banner (Q04). The QR is decorative. |
| `customer/orders.html` | `/app/orders` | `GET /api/booking/orders` (20 per page) | Tab counts left out (**needs API**). |
| `customer/order-detail.html` | `/app/orders/:id` | `GET /api/booking/orders/{id}`, `.../progress`, `POST .../cancel` | |
| `customer/extension.html`, `extension-wait.html` | `/app/orders/:id/extension` | `POST /api/booking/orders/{id}/extensions` | Price known only after the worker accepts (owner decision C). 97.500 đ is sample. |
| `customer/acceptance.html` | `/app/orders/:id/acceptance` | worker assignment photos, `POST .../accept`, `.../request-redo` | 5 angles before/after. |
| `customer/rating.html` | `/app/orders/:id/rating` | rating-window + rating POST | punctuality, cleaningQuality, attitude; 48 h. |
| `customer/disputes.html`, `dispute-create.html` | `/app/disputes`, `/app/disputes/new` | `GET/POST /api/customers/me/disputes` | 1-1000 chars, 1-10 photos, 24 h. Status labels are sample. |
| `customer/addresses.html`, `address-form.html` | `/app/addresses` | `GET/POST/PUT /api/customers/me/addresses` | `floorAreaM2 × numFloors`. |
| `customer/favorites.html` | `/app/favorites` | `GET /api/customers/me/favorite-workers` | |
| `customer/account.html`, `profile.html` | `/app/account`, `/app/profile` | `GET/PUT /api/customers/me` | "Ca đã đặt" and "Tham gia": **needs API** (as on Mobile). |

### Agency / Partner Portal (endpoints from `Frontend/src/features/agencies/api.ts`)

| File | Today's route | Data | Notes |
|---|---|---|---|
| `agency/overview.html` | `/partner` (empty page today) | escrow, subscription, roster, orders | **new**. Uses only data of existing endpoints; "cửa sổ đổi thợ" and "khiếu nại" counts come from `swap-windows` and the escrow ledger. |
| `agency/subscription.html` | `/partner/subscription` | `GET /api/agencies/packages`, `/subscriptions/me`, `POST /subscriptions` | Package names, prices and features are sample. |
| `agency/escrow.html` | `/partner/escrow` | `GET /api/agencies/escrow`, `/escrow/transactions`, `POST /escrow/deposits`, `/escrow/transactions/{id}/appeal` | |
| `agency/bank.html` | `/partner/bank` | `GET/PUT /api/agencies/me/bank` | 6-30 digits. |
| `agency/import.html` | `/partner/workers` | `/agencies/guarantee`, `/workers/import/template`, `/dry-run`, `/commit` | Columns: full_name, phone_number, national_id (12), bank_name, bank_account_no. |
| `agency/worker-list.html` | `/partner/worker-list` | `GET /api/agencies/workers`, `POST .../retire` | Retire flow: replace with existing / new / emergency rescue. |
| `agency/roster.html` | `/partner/roster` | `GET/PUT /api/agencies/roster`, `DELETE /roster/{id}` | |
| `agency/jobs.html` | `/partner/jobs` | `GET /api/agencies/orders`, `/reports/performance` | |
| `agency/swap.html` | `/partner/swap` | `GET /api/agencies/swap-windows`, `POST .../unavailable`, `.../swap` | 30 min window; under 2 h goes to rescue dispatch. |

## What is not here

- Admin console pages (dashboard, absence reports, audit log, packages, escrow appeals): only the login is drawn.
- Worker screens: Worker stays on Mobile.
- Wiring: every number is sample data; the "needs API" items need a backend ticket before the React screen can show them.
- No claim about insurance, certification or guarantees beyond what the approved landing page already says (`DESIGN.md` section 3).

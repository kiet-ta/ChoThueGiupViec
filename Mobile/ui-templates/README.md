# Mobile UI templates (ticket #449, MOB-UI-01)

A clickable HTML/CSS prototype of the whole Mobile app: 37 screens (4 shared, 16 Customer, 17 Worker) in a 390×844 frame.
It is a design reference to approve **before** any Flutter change. Nothing here is wired to the API: every number is sample data.

## How to view

- Open `index.html` in a browser: every screen in one grid, grouped Shared / Customer / Worker, each with its Flutter route.
- Click through from `shared/welcome.html`. "Tôi cần giúp việc" leads to the Customer app, "Tôi là người giúp việc" to the
  Worker sign-up and then the Worker app. The bottom navbar and the main buttons of every screen open real files.
- Fonts (Newsreader, Plus Jakarta Sans) and icons (Material Symbols Outlined) load from Google Fonts, so the first view needs
  network access. Without it the browser falls back to system fonts and shows icon names as text.
- `worker/offer.html` runs the 30 s countdown; append `?static` to freeze it (used by `index.html`).

## Why (audit of the current app, 2026-10-25)

| Finding | Evidence |
|---|---|
| Home is a marketing landing page: eyebrow badge, hero headline, 4 trust badges, role toggle, price cards with fixed prices (240.000 đ / 360.000 đ). | `lib/features/home/home_screen.dart`; screenshot of `flutter build web` at 390×844 |
| No bottom navigation: every screen is a `pushNamed` stack and the signed-in Home is a column of `ListTile` rows. | `grep -rn "BottomNavigationBar\|NavigationBar(" Mobile/lib` returns nothing; `home_screen.dart` `_sessionPanel` |
| Text carries the meaning; no chart anywhere. | `Text(` per screen: `order_detail_screen.dart` 30, `payment_screen.dart` 20, `customer_absent_screen.dart` 20 |
| The trust badges make claims ("Bảo hiểm an tâm 50 triệu") that need leader approval. | design skill `SKILL.md` §3 |

## Design rules used

- **Navigation:** one bottom navbar per role, 5 items with a raised center button (`assets/app.js`).
  Customer: Trang chủ · Đơn · **(+) Đặt ca** · Yêu thích · Tài khoản. Worker: Hôm nay · Lịch · **(Việc hiện tại)** · Thu nhập · Tài khoản.
  Detail screens (booking, payment, check-in…) have no navbar and a bottom action bar instead.
- **Same canvas for both roles:** Customer and Worker share the cream canvas and white cards; they differ only in layout
  (owner choice). Customer screens lead with the next booking and spend; Worker screens lead with the next shift, money and time.
- **Type:** Newsreader only for the Home greeting and large numbers (`.greet`, `.num`); everything else Plus Jakarta Sans.
- **Icons:** Material Symbols Outlined, one color (text-secondary, or primary when selected). Filled only for a selected nav item,
  stars and the favorite heart.
- **Little text:** numbers, rings, bars, steppers and maps carry the meaning; one short label per number.
- **Charts:** inline SVG, at most 2 data colors (primary `#416448`, amber `#D9A441`), neutrals for context bars, values always
  written next to the mark (direct labels) and a hover tooltip on bars. The palette validator of the dataviz skill reports that
  primary is below the chroma floor (0.061) and amber is 2.19:1 against white: both are brand values kept on purpose, so no chart
  relies on color alone.

## Screen map

"needs API" = the widget shows data no endpoint returns today; it needs a backend ticket before the Flutter screen can show it.

### Shared

| File | Flutter route / screen | Data | Notes |
|---|---|---|---|
| `shared/welcome.html` | `/` (`HomeScreen`, signed out) | none | Replaces the landing page. No trust badges. |
| `shared/phone.html` | `/auth/phone-input` | `POST /api/auth/otp/request` | |
| `shared/otp.html` | `/auth/otp-verify` | `POST /api/auth/otp/verify` | 6 digits, 5 tries, 60 s resend (Q06) |
| `shared/register.html` | `WorkerRegistrationScreen` | `POST /api/workers/register` | Then a new OTP login (no tokens in the answer) |

### Customer

| File | Flutter route / screen | Data | Notes |
|---|---|---|---|
| `customer/home.html` | `/` (`HomeScreen`, customer) | `GET /api/booking/orders` + `.../progress`, `GET /api/booking/price-quote`, `GET /api/customers/me/favorite-workers` | Spend chart: **needs API** (no customer spending endpoint) |
| `customer/booking.html` | `/booking` | `GET /api/booking/options`, `GET /api/booking/price-quote`, `POST /api/booking/orders` | No required skill (D8). Price per worker per shift from the server. |
| `customer/payment.html` | `/payments` (also `/payments/momo`) | `POST /api/payments/orders/{id}/qr`, `GET /api/payments/{paymentId}` every 3 s | SANDBOX banner (Q04). QR expiry 15 min. The QR picture **needs a QR package** (today: text). The QR drawn here is decorative, not scannable. |
| `customer/order-detail.html` | `/booking/detail` | `GET /api/booking/orders/{id}`, `.../progress` every 5 s, `POST .../cancel` | 4-step timeline; the action list shows the later states of the same screen |
| `customer/orders.html` | `/booking/orders` | `GET /api/booking/orders` (20 per page) | Status counts: **needs API** |
| `customer/extension.html` | `/booking/detail` ("Làm lần 2") | `POST /api/booking/orders/{id}/extensions` | Price unknown until the server answers |
| `customer/extension-wait.html` | `/booking/detail` | extension answer (`extraAmount`), then `/payments` with `extensionId` | Worker accepts first, then the QR exists (owner decision C) |
| `customer/acceptance.html` | `/customers/assignments/acceptance` | `GET /api/workers/assignments/{id}/photos`, `POST /api/customers/assignments/{id}/accept` / `request-redo` | 5 angles before/after |
| `customer/rating.html` | `/ratings` (customer) | `GET /api/customers/me/assignments/{id}/rating-window` + rating POST | punctuality, cleaningQuality, attitude; 48 h |
| `customer/disputes.html` | `/disputes` (customer) | `GET /api/customers/me/disputes` | Status labels from `dispute_logic.dart` |
| `customer/dispute-create.html` | `/disputes/create` | `POST /api/customers/me/disputes`, `.../disputes/evidence` | 1-1000 chars, 1-10 photos, 24 h |
| `customer/addresses.html` | `/addresses` | `GET /api/customers/me/addresses` | |
| `customer/address-form.html` | `/addresses/form` | `POST/PUT /api/customers/me/addresses` | Province → ward (catalogue since 2025-07-01), housing type, `floorAreaM2 × numFloors` |
| `customer/favorites.html` | `/customer/favorites` | `GET /api/customers/me/favorite-workers` | `ratingAvg`, `completedJobs`, `workStatus` |
| `customer/account.html` | **new** (tab hub) | `GET /api/customers/me` | "Ca đã đặt" count: **needs API** |
| `customer/profile.html` | `/customer/profile` | `GET/PUT /api/customers/me` | |

### Worker

| File | Flutter route / screen | Data | Notes |
|---|---|---|---|
| `worker/today.html` | `/` (`HomeScreen`, worker) | `GET /api/workers/me/earnings`, `GET /api/workers/me/slots` | Next job: **needs API** ("my assignments" does not exist). Booked vs free cells: **needs API**. "126 ca": **needs API** |
| `worker/offer.html` | `/dispatch/offers` | `GET /api/dispatch/offers/current`, `POST .../accept` / `decline` | Every field is in `JobOffer` |
| `worker/job.html` | **new** (center tab) | — | **needs API** (current assignment + pending offer count) |
| `worker/check-in.html` | `/dispatch/check-in` | `POST /api/dispatch/assignments/{id}/check-in` | ≤ 100 m. Not drawn: the GPS-mismatch state with the plate photo |
| `worker/execution.html` | `/workers/execution` | assignment id | No checklist (no API, issue #442) |
| `worker/photos.html` | `/workers/photos` | `POST /api/workers/assignments/{id}/photos`, `.../submit-completion` | Tap an empty slot to see the "all taken" state |
| `worker/extension.html` | `/workers/extension` | `POST /api/workers/assignments/{id}/extension/respond` | Answer before payment |
| `worker/customer-absent.html` | `/dispatch/customer-absent` | `POST /api/dispatch/assignments/{id}/calls/log`, `.../absent` | 15 min, 2 calls, 40 % / 60 % (`BusinessRules.Absence`, `CustomerAbsentReport`) |
| `worker/incident.html` | `/dispatch/incident-report` | `POST /api/dispatch/assignments/{id}/incidents` | ACCIDENT, HEALTH, SEVERE_WEATHER, OTHER |
| `worker/schedule.html` | `/workers/slots` | `GET /api/workers/me/slots`, `POST .../slots/toggle`, `PUT /api/workers/me/location` | BOOKED slot locked; agency staff get 403 |
| `worker/income.html` | `/payouts` | `GET /api/workers/me/earnings?month=` | 6-month chart = 6 calls of the same endpoint. No wallet (P6) |
| `worker/payout-history.html` | `/payouts/history` | `GET /api/workers/me/payouts` | |
| `worker/account.html` | `/workers/profile` | `GET /api/workers/me` | Criteria averages: **needs API** |
| `worker/ekyc.html` | `/workers/ekyc` | `POST /api/workers/me/ekyc` | Fake eKYC in phase 1 (Q05) |
| `worker/agency-roster.html` | `/agencies/roster` | `GET /api/workers/me/slots` (14 days) | Empty for a freelancer |
| `worker/rating.html` | `/ratings` (worker) | `GET /api/workers/me/assignments/{id}/rating-window` + rating POST | cooperation, workingConditions |
| `worker/disputes.html` | `/disputes` (worker) | `GET /api/workers/me/disputes` | Counts: **needs API** |

Routes without their own template: `/ratings/history` and `/agencies/profile` are placeholders in the app today.

## Components → Flutter

| CSS class (`assets/components.css`) | Flutter |
|---|---|
| `.phone` + `.screen` | `Scaffold` + `ListView` / `CustomScrollView`, max width 390 |
| `.bnav` + `.fab` | `NavigationBar` (or `BottomAppBar`) + center `FloatingActionButton`, `FloatingActionButtonLocation.centerDocked` |
| `.appbar`, `.appbar.large` | `AppBar`, `SliverAppBar.large` |
| `.card`, `.card.flat`, `.card.brand` | `NordicCard` (`core/widgets/nordic_card.dart`) with a `surfaceContainerLow` / primary variant |
| `.list .item` | `Card` + `ListTile` separated by `Divider` |
| `.stat`, `.num` | KPI tile: `Card` + `Text` in the `metric` / Newsreader style |
| `.btn`, `.btn.ghost`, `.btn-bar` | `ElevatedButton` / `OutlinedButton` (`StadiumBorder`, 52 px), `bottomNavigationBar: SafeArea(...)` |
| `.chip`, `.seg` | `ChoiceChip`, `SegmentedButton` |
| `.input`, `.field` | `TextField` / `NordicTextInput` |
| `.steps`, `.vline` | custom `Row` stepper (`Stepper` is too heavy for 3-4 dots) |
| `.bar`, ring SVGs | `LinearProgressIndicator`, `CircularProgressIndicator` (value) or `CustomPaint` |
| bar charts (SVG) | `CustomPaint`, or a chart package (needs a ticket that names it) |
| `.sheet` + `.scrim` | `showModalBottomSheet` |
| `.pill` | `Chip` / small `Container` with a label (never color alone) |
| `.switch` | `Switch` |
| `.map` | placeholder: no map package in `pubspec.yaml` (`MiniMapWidget` today) |

## Icons → Flutter

Every Material Symbol used maps to an existing `Icons.<name>_outlined` constant; checked against
`~/flutter/packages/flutter/lib/src/material/icons.dart` (Flutter 3.47.7) with
`grep "static const IconData <name>_outlined = "`: 62 of 62 found. The filled state (selected nav item, star, favorite,
check_circle, radio_button_checked) uses the constant without the suffix (`Icons.star`), also checked.

add, add_a_photo, apartment, arrow_back, badge, battery_full, bedtime, business, calendar_month, car_crash, check,
check_circle, chevron_left, chevron_right, cleaning_services, close, done_all, expand_more, face, favorite, flag, gavel,
gps_fixed, groups, home, image, light_mode, location_on, lock, logout, medical_services, more_horiz, more_time,
my_location, near_me, notifications, payments, person, photo_camera, photo_camera_front, photo_library,
qr_code_scanner, radio_button_checked, radio_button_unchecked, receipt_long, report, schedule, science, search, send,
signal_cellular_alt, star, straighten, sync, task_alt, thunderstorm, timer, today, verified_user, wb_sunny, wifi, work.

(`signal_cellular_alt`, `wifi`, `battery_full` are only the fake status bar of the prototype.)

## Colors → tokens

All values are in `assets/tokens.css`, each with its DESIGN.md token name. Used in screens: `canvas-background`,
`card-surface`, `surface-container-low`, `surface-container`, `surface-dim`, `primary`, `primary-hover`,
`primary-container`, `primary-fixed` (selected and "done" states), Warm Amber `#D9A441` (DESIGN.md prose: stars and the
second chart series), `secondary-fixed` (warning pill and sandbox banner background), `on-secondary-fixed-variant`
(its text), `text-primary`, `text-secondary`, `outline-variant`, `error`, `error-container`.
**No DESIGN.md token:** border `#E5E7EB` (same constant as `nordic_colors.dart`, awaiting issue #61).

## Sample data and the rules it follows

| Shown | Source |
|---|---|
| 260.000 đ (ECONOMY 31-80 m²), 390.000 đ (PREMIUM 31-80 m²), per worker per shift | `.spec/decisions.md` default `PRICE_RULE` |
| Shifts 08:00-12:00, 13:00-17:00, 17:30-20:30 | `BookingShifts.cs` |
| Commission 20 % (52.000 đ of 260.000 đ) | `BusinessRules.Commission.Freelancer = 0.200` |
| Extension 1,5 h = 97.500 đ | `round(unit_price / 4 × hours)` (`.spec/decisions.md`), shown only after the server answers |
| QR expiry 15 min | `BusinessRules.QrExpiryMinutes` |
| Absence: 15 min, 2 calls, 40 % worker / 60 % refund, 24 h dispute | `BusinessRules.Absence`, `customer_absent_report.dart` |
| Order code `GV261025K7Q2MX` | `OrderCodeGenerator`: `GV` + yyMMdd + 6 characters |
| Wards "Phường Sài Gòn", "Phường Tân Định", "Phường Thạnh Mỹ Tây" | `vn_admin_units.dart` (after 2025-07-01; "Bến Nghé" no longer exists) |
| Payout labels "Chưa chốt đợt", "Chờ chuyển khoản", "Đã chuyển khoản" | `payout_logic.dart` |
| Dispute labels "Chờ tiếp nhận", "Đang xử lý", "Đã có phán quyết", "Đã bác bỏ" | `dispute_logic.dart` |
| Today = Sunday 25/10/2026 | weekdays checked with `date -d` |

Names (Nguyễn Lan, Hà Trần…), phone numbers, ratings and month totals are invented sample values.

## Limits

- Checked in a headless Chromium-based browser at 390×844 only; not checked on a device or in Flutter.
- The prototype does not cover error, empty and loading states, except the "photos incomplete" and "waiting" states.
- Charts, rings and maps are static SVG; the Flutter version needs `CustomPaint` or a package chosen in its own ticket.
- Flutter has no bundled Newsreader / Plus Jakarta Sans yet (see `Mobile/ARCHITECTURE.md` §2): fonts need their own ticket.

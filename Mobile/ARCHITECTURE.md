# Mobile Architecture: TỔ ẤM — Nordic Care

## 1. Overview
The Mobile application is built with Flutter for cross-platform iOS and Android deployment.
It serves two primary personas within a unified responsive client:
- **Customer (Gia chủ / Khách hàng):** Discovers services, books shifts, manages addresses, favorites preferred workers, and tracks jobs.
- **Worker (Chuyên viên giúp việc / Đối tác):** Receives 30-second job dispatches, verifies GPS check-in (≤100m error), and submits photo-verified inspections.

## 2. Design System: TỔ ẤM — Nordic Care
The visual identity follows the team design skill (`.agents/skills/project-team-design-template`):
- **Design Frame:** 390×844 standard mobile layout.
- **Source of truth:** `DESIGN.md` in that skill (YAML token values win over the prose and over any copy).
- **Canvas:** `#FAF8F3` (`canvas-background`); cards `#FFFFFF` (`card-surface`).
- **Mobile System Design:** [`DESIGN-SYSTEM.md`](DESIGN-SYSTEM.md) (MOB-UI-13) is the reference for every Mobile screen: tokens, components, screen patterns, copy rules, done checklist. The lines below are a summary.
- **Primary Brand (Mobile palette, #479):** `#2F6B3F` (`primary`), pressed `#285C36`, leaf `#7FB77E` (`primaryContainer`), soft green `#D2E6D2` (`primaryFixed`). These deliberately differ from `DESIGN.md`.
- **Accent Amber:** `#F7C85C` for ratings and highlights, cream `#FFF6C0` for warnings.
- **Text:** `#2F3A34` (`text-primary`), `#6B7280` (`text-secondary`).
- **Not in DESIGN.md yet** (kept as-is, awaiting a leader decision on issue #61): secondary accent `#6B8F71`, dark anchor `#1A2F25` / `#223B2F`, success `#2E7D32`, subtle surface `#F4F2EB`, body text `#4B5563`, border `#E5E7EB`.
- **Typography:** Newsreader for the Home greeting and large numbers, Plus Jakarta Sans for everything else (MOB-UI-02, after `Mobile/ui-templates`). Both are bundled as static TTFs under `assets/fonts/` (Newsreader 500/600, Plus Jakarta Sans 400-700, SIL OFL 1.1, licences beside them) and declared in `pubspec.yaml`; no font package.
- **Elevation & Shapes:**
  - Pill buttons (`rounded-full` / `StadiumBorder`).
  - Flat white cards with 1px hairline borders (`#E5E7EB`, no DESIGN.md token yet) and 20px corners; fields 14px, sheets 28px (`NordicRadius`, `NordicSpace`).

### 2.1 Motion & interaction (MOB-UI-12)
Apple fluid-interface principles (the local `apple-design` Agent Skill), applied first to the sign-in screens. Reuse these on every screen you redesign instead of hand-made durations and curves:
- **Springs, not durations** (`lib/core/motion/nordic_motion.dart`). A `NordicSpring` is a damping ratio + a response in seconds. `NordicMotion.standard` (1.0 / 0.35 s, no overshoot) for anything that appears, moves or changes on its own; `NordicMotion.momentum` (0.8 / 0.3 s) only after a gesture that carried momentum (flick, drag release); `NordicMotion.shake` for a wrong entry. With an `AnimationController`, use `animateWith(spring.simulate(from: controller.value, velocity: controller.velocity))` so a motion can be interrupted and restarted from where it is; with implicit widgets, use `curve: SpringCurve(spring)` and `duration: spring.settleDuration`.
- **Page transitions** are set once in `NordicTheme.pageTransitionsTheme` (`NordicPageTransitionsBuilder`, `lib/core/motion/nordic_page_transitions.dart`): a pushed page slides in from the right and leaves the same way, the page below moves back a little, and the page can be dragged back from the left edge 1:1 with the finger (it carries on at the finger's speed on release). Just push a `MaterialPageRoute`; do not pass a custom transition per screen. Reduced motion: pages only fade.
- **Press feedback on touch-down**: wrap tappable custom widgets in `Pressable` (`lib/core/widgets/pressable.dart`); `NordicButton` already does.
- **Errors**: `ErrorShake` (`lib/core/motion/error_shake.dart`) around the input that was wrong, raised by one per error; it shakes and vibrates on the same frame. Always pair it with a message.
- **Status while typing**: show validity inline (e.g. the check mark of `PhoneInputField`) instead of only after submit.
- **Main button above the keyboard**: put `BottomActionBar` in the body (a `Column` with the scrolling content `Expanded`), not in `Scaffold.bottomNavigationBar`, which the keyboard covers.
- **Materials**: controls over photos sit on a small blurred material (see the back button of `AuthPhotoHeader`), solid when `MediaQuery.highContrast`.
- **Type**: the largest title of a screen uses `NordicTypography.display` (-0.02 em tracking, 1.1 leading); body text keeps ~0 tracking.
- **Reduced motion** (`NordicMotion.reduced(context)`): no slide, scale or shake; fade (`NordicMotion.fade`) or dim instead, keep haptics. Every widget above handles it.

## 3. Directory Structure & Feature Module Registration
```
Mobile/
├── lib/
│   ├── app/                 # App lifecycle, MaterialApp, routes (FROZEN after MOB-BASE-02)
│   │   ├── app.dart
│   │   ├── feature_module.dart   # FeatureModule abstraction
│   │   ├── feature_registry.dart # Aggregates modules from all 6 slots
│   │   └── routes.dart           # Central route constants & delegate
│   ├── core/                # Shared theme, widgets, network, models
│   │   ├── models/
│   │   ├── theme/
│   │   └── widgets/
│   ├── features/            # Isolated vertical features per module
│   │   ├── home/            # Welcome & persona selection
│   │   ├── identity/        # M1: Phone OTP & Auth tokens
│   │   ├── customers/       # M1: Profile, address book, favorite workers
│   │   ├── booking/         # M2: Booking flow & shift selection
│   │   ├── payments/        # M2: MoMo sandbox QR & transactions
│   │   ├── dispatch/        # M3: 30s offers, GPS check-in, incidents
│   │   ├── workers/         # M4: eKYC, Block Slots, Before/After VoL
│   │   ├── agencies/        # M5: Shift Roster & agency profile
│   │   ├── ratings/         # M6: Two-way ratings (48h window)
│   │   ├── disputes/        # M6: Dispute tickets & claims
│   │   └── payouts/         # M6: Worker earnings & payout history
│   └── main.dart            # Flutter entrypoint
└── test/                    # Unit, widget, and golden tests
```

### Feature Module Conventions (`MOB-BASE-02`)
To prevent merge conflicts across the 6 team members:
1. `Mobile/lib/app/**` is **frozen**. Members must not edit `app.dart` or `feature_registry.dart`.
2. Each feature module owns its directory `Mobile/lib/features/<module>/` exclusively.
3. Every feature defines its own `routes.dart` containing a class implementing `FeatureModule`.
4. Route names are exposed through static constants in the feature's `routes.dart` and registered in its module's `routes` map or `onGenerateRoute` factory.

### Worker position (`features/workers`, MOB-WORKER-LOCATION)
- `WorkerSlotsScreen` sends the device position with `PUT /api/workers/me/location` (`WorkersLocationService`) when it opens and every time the worker switches a slot ON; the dispatch search offers a job only to a worker who has a position (decisions D19). A failing call is ignored (best effort).
- `LocationWrapper` has no GPS plugin yet: it returns the mock position or a fixed position in District 1, Ho Chi Minh City (10.7769, 106.7009), the same source the check-in uses and the same default as the address form and the GPS picker (it was Hanoi, far from every address the app creates). A real sensor needs a package and platform permissions (separate ticket).

### Address province and ward (`features/customers`, MOB-ADDR-PICKER)
- The address form picks "Tỉnh / Thành phố" then "Phường / Xã" (`widgets/admin_unit_picker_field.dart`: a read-only field opening a searchable bottom sheet; the search ignores case and Vietnamese diacritics). The ward picker lists only the chosen province's units and waits for a province; a new province clears the ward. A new address starts in Thành phố Hồ Chí Minh, like the GPS default.
- Catalogue: `models/vn_admin_units.dart`, generated from `https://provinces.open-api.vn/api/v2/?depth=2` (downloaded 2026-10-20): the 34 provinces and 3,321 wards / communes / special zones in force since 2025-07-01 (no district level any more). Embedded as a constant: no package, no network call. Regenerate it from the same source when the units change.
- The API is unchanged (contract `customers.md`: `city` and `district`, 1-100 chars): `city` carries the province, `district` the ward, both with their full official names. An address saved before (e.g. `Quận 1`, `TP. Hồ Chí Minh`) shows its old text and must be chosen again before it can be saved.

### Worker income (`features/payouts`, MOB-M6-04)
- `/payouts` (`PayoutScreen`): month selector (default month and "today" are read in Asia/Ho_Chi_Minh, UTC+7; the next button stops at the current month because the server answers 400 for a future month; the previous button stops at January 2024) and `GET /api/workers/me/earnings?month=YYYY-MM`. It shows "Thu nhập tháng này" (net), gross, commission, penalty, the payout status (`NOT_BUILT`, `PENDING`, `TRANSFERRED`) with a hint, and the jobs; an approved absence fee carries a "Phí vắng mặt" tag. There is no wallet or "số dư khả dụng" (open question P6). A 403 (agency staff: the agency is paid) is explained and has no retry. A slow answer of an older month never overwrites a newer choice.
- `/payouts/history` (`PayoutHistoryScreen`): `GET /api/workers/me/payouts?page=&pageSize=`, "Xem thêm" loads the next page; a failure on a later page keeps what is shown.
- `logic/payout_logic.dart` is pure (month keys, VND `1.234.567 đ`, Ho Chi Minh date and time, labels, error texts). Every amount comes from the server; the device does no money arithmetic. `services/payouts_service.dart` uses the shared `ApiClient`; screens take an `IPayoutsService` so tests inject a fake.

- **Entry points (MOB-M6-05):** nothing else opens `/ratings` or `/disputes` yet (the booking and tracking screens are M2's and M4's and are not built), so each finished job on `/payouts` has "Đánh giá khách" (`RatingScreenArgs(role: worker, assignmentId)`) and "Khiếu nại" (`DisputeScreenArgs(role: worker, orderId)`); an absence-fee job has neither. `PayoutScreen` takes `onRate` / `onDispute` for tests. The customer side still needs M2's screens to call the same two routes with `RatingRole.customer` / `DisputeRole.customer`.

### Disputes (`features/disputes`, MOB-M6-03)
- Both routes read `DisputeScreenArgs(role, orderId?)` from the route arguments (the endpoint prefix depends on whether a customer or a worker is signed in); without them a note says disputes are opened from a job. `/disputes` (`DisputeScreen`) lists the caller's own disputes (`GET .../disputes`) with status, SLA due time and, once `RESOLVED`, the fault party and compensation; `DISMISSED` is stated plainly. With an `orderId` a button opens `DisputeCreateScreen` (`/disputes/create`).
- The form (`POST .../disputes`): category (D1; `ABSENT_FEE` only for a customer), description 1-1000 characters, 1-10 evidence photos, a statement of the 24 h window and the one-ticket-per-order rule, one request per tap. 400 field messages show under their fields; 404, 409 and network failures have their own text; the success view shows the `slaDueAt` returned by the server.
- **Evidence photos (MOB-M6-03b):** photos go through `IEvidenceUploader`. The default is `ApiEvidenceUploader`: it takes the photo from the shared `ICameraWrapper`, detects `image/jpeg`, `image/png` or `image/webp` from the first bytes, and sends `{ fileName, contentType, contentBase64 }` with the shared `ApiClient` to `POST .../disputes/evidence` (contract disputes.md 2.1a, backend ticket #211), returning the `url` for `evidenceUrls`. A cancelled capture adds nothing; an unreadable photo or a server refusal is shown in the form. `UnavailableEvidenceUploader` remains for a build that cannot attach photos (the form then explains it and the send button stays disabled). Limit: the repository has no real camera yet (`CameraWrapper` returns a placeholder without bytes unless a test injects an image), so on a device the add-photo button reports an unreadable photo until a real camera is wired in; the same holds for the dispatch and workers screens.

## 4. Verification
- `cd Mobile && flutter analyze`: Static linting and code quality.
- `cd Mobile && flutter test`: Unit and widget test execution.

## 5. Two-way rating (MOB-M6-01 customer rates the worker, MOB-M6-02 worker rates the customer)
- Feature `lib/features/ratings/` (contract `.spec/contracts/ratings.md` 2.1-2.2, decision Q14): `models/rating_models.dart` (role, fixed criteria, window), `logic/rating_logic.dart` (pure: countdown text, reason messages, validation, request body, error mapping), `services/ratings_service.dart` (`IRatingsService` over the shared `ApiClient`), `screens/rating_screen.dart`.
- **Route `/ratings`** takes `RatingScreenArgs(role, assignmentId, ratedName?)` as route arguments; whoever shows a completed job (booking or dispatch) pushes it. Without arguments a short note says ratings are given from a completed job. There is no ratings list screen: the API has no list and a worker's rating is internal only.
- The screen reads the window (`.../assignments/{id}/rating-window`) and shows the form only while `canRate`: the countdown of the 48 h window, overall stars and the fixed criteria of the role (customer: punctuality, cleaningQuality, attitude; worker: cooperation, workingConditions), an optional comment of at most 500 characters. The send button is disabled until every score is chosen and sends once per tap; 400 field messages show under their field, 409 reloads the window and shows the real reason, 404 and network failures have their own text. A rating cannot be edited, and the screen says so before sending.
- Tests: `test/features/ratings/` (`rating_logic_test.dart`, `ratings_service_test.dart` against a local `HttpServer`, `rating_screen_test.dart` for every state and error). `flutter pub get` regenerates the plugin registrant files under `linux/`, `macos/` and `windows/`: do not commit them in a feature ticket.

## 6. Booking and payment of a customer (MOB-M2-01, MOB-M2-02, MOB-M2-03)
- **Shared client:** `ApiException.code` (`lib/core/network/api_client.dart`) is the business code of a failed call (`data.code`, e.g. `FULLY_BOOKED`). Screens branch on the code, never on the message (contracts `booking.md` 3.3, `payments.md` 2.1).
- **`/booking` (`BookingScreen`)**, feature `lib/features/booking/` (contract `booking.md` 3.1 to 3.3): choose ECONOMY or PREMIUM, one of the customer's addresses (read through `IBookingAddressSource`, by default the Customers feature's address book), a day and a shift, and an optional note (500). There is no required-skill field: skill matching was removed (decision D8, 2026-10-13), so the request is `addressId`, `serviceTier`, `scheduledDate`, `shiftCode`, `customerNote`; the contract still lists `requiredSkill` as optional (booking.md 3.3, B6) and the backend accepts its absence. The **fixed price** is the server's quote (`GET /api/booking/price-quote`), shown before the order exists and asked again whenever the segment or the address changes; the device computes no money. "Đặt đơn" calls `POST /api/booking/orders` once per tap and opens `/payments` for the new order. Tiers, shifts, lead time and limits come from `GET /api/booking/options`; the only constant is `selectableDays` (14, the server's `Booking.MaxDaysAhead` default, which the options endpoint does not carry: the server stays the authority and answers 400 on `scheduledDate`).
- **`/payments` (`PaymentScreen`)**, feature `lib/features/payments/` (contract `payments.md` 2.1 to 2.3): takes `PaymentScreenArgs(orderId, extensionId?, orderCode?)` as route arguments. It asks the QR (`POST /api/payments/orders/{id}/qr`, or the extension one), shows the amount and the time left, and asks `GET /api/payments/{paymentId}` every 3 s until SUCCESS, EXPIRED or the deadline; the timers stop when the screen is left. The banner **"SANDBOX – no real money"** (decision Q04) is always on screen.
- **Two limits today:** the project has no QR-rendering package and no realtime (SignalR) client (`pubspec.yaml` has no dependency beyond Flutter, and adding one needs a ticket that names it). So the QR content is shown as selectable text, not as a scannable image, and "paid" arrives by polling, which is the contract's stated fallback.
- `logic/booking_logic.dart` and `logic/payment_logic.dart` are pure (labels, VND, Asia/Ho_Chi_Minh days, validation, countdown, error texts by code). Services use the shared `ApiClient`. Tests: `test/features/booking/`, `test/features/payments/`, `test/core/network/api_exception_code_test.dart` (services against a local `HttpServer`, widget tests for every state).

## 7. Following an order: tracking, cancel, "Làm lần 2", history (MOB-M2-04, MOB-M2-05, MOB-M2-06)
- **`/booking/orders` (`OrderHistoryScreen`)**, contract `booking.md` 3.4: the customer's orders in the server's order, 20 per page, "Xem thêm" for the next page; a tap opens the order.
- **`/booking/detail` (`OrderDetailScreen`)**, route arguments `OrderDetailArgs(orderId)`, contract `booking.md` 3.5 to 3.8: the order, a four-step timeline (pay, find a worker, worker assigned, done; `PAID` is shown as "finding a worker"), each visible worker with name, rating and assignment status. It reads `GET /api/booking/orders/{id}` and `.../progress` again every 5 s while the order is not COMPLETED or CANCELLED (no realtime client, see section 6); a failed refresh keeps the screen and the next one tries again. The timer stops when the screen is left. After booking, the payment screen is replaced by this screen.
- **Pay:** a `PENDING_PAYMENT` order, and a `PENDING_PAYMENT` extension, open `/payments` with `PaymentScreenArgs(orderId, extensionId?)`.
- **Cancel:** offered until the order is finished; a reason (1 to 255) is required before any request; `POST .../cancel` once per tap. The server decides (it refuses with 409 e.g. an assigned order close to its shift) and the refusal is explained on screen.
- **"Làm lần 2":** offered only when the order is `ASSIGNED`, has no extension, and a worker is `IN_PROGRESS` or `AWAITING_ACCEPTANCE`. The customer picks the worker (when two are on site) and half hours up to `shiftMaxHours` of `GET /api/booking/options`; `POST .../extensions` returns the server-computed `extraAmount`, shown with a "Thanh toán" button for the extension's QR. The device computes no money, so the price is only known after the request.
- **After the job (MOB-CUSTOMER-03):** each worker whose assignment is `AWAITING_ACCEPTANCE` has "Xem ảnh & nghiệm thu" (opens `/customers/assignments/acceptance` with `CustomerAcceptanceArgs(assignmentId)`); each `COMPLETED` one has "Đánh giá thợ" (`/ratings` with `RatingScreenArgs(customer, assignmentId, workerName)`); the order has "Khiếu nại về đơn này" (`/disputes` with `DisputeScreenArgs(customer, orderId)`) once a worker reached `AWAITING_ACCEPTANCE`, `COMPLETED` or `ABSENT`. The screen only hides what can never apply (`assignmentActions`, `canDispute`); the 48 h rating window, the 24 h dispute window and the one ticket per order are the server's, and the screens that open show its refusal. Coming back from any of them reloads the order.
- **`/customers/assignments/acceptance` (`CustomerAcceptanceScreen`)**, contract `workers.md` 2.5.2 / 2.5.3: reads the assignment's real photos (`GET .../photos`, loaded from the authenticated file endpoint with the Bearer token), then "Xác Nhận Nghiệm Thu" or "Dọn Lại". Without `CustomerAcceptanceArgs` the route shows a note (there is no default assignment). A failed photo load, accept or redo is an error on the screen (photo load: with a retry); the screen never shows sample photos or a made-up payout, and the payout card shows only what the server answered, with its own commission rate.
- `logic/order_logic.dart` is pure (labels, timeline, what is offered when, error texts by code `INVALID_STATE` / `EXTENSION_EXISTS`). `services/order_service.dart` (`IOrderService`) uses the shared `ApiClient`. Tests: `test/features/booking/order_logic_test.dart`, `order_service_test.dart` (local `HttpServer`), `order_screens_test.dart`, `order_actions_test.dart`; `test/features/workers/customer_acceptance_screen_test.dart`.
- **Limits:** the QR of an extension has the limits of section 6.

### Home and navigation (`features/home`, MOB-NAV, MOB-UI-02)
- **Design reference:** `Mobile/ui-templates/` (MOB-UI-01). MOB-UI-02 applied it to the Customer flow: the theme carries its tokens (`NordicColors.surfaceLow`, `primaryFixed`, `amberSoft`, `onAmberSoft`; `NordicTypography.greeting`, `number*`, `screenTitle`, `appBarTitle`, `title*`, `caps`; 52 px stadium buttons, flat app bar, chip / segmented / navigation themes) and `core/widgets/` has the template components (`SectionTitle`, `StatTile`, `StatusPill`, `StepDots`, `BottomActionBar`, `ProgressRing`, `AvatarInitials`, `GroupedListCard`). Only presentation changed: no service, logic, model or route.
- **Signed out:** the minimal welcome (logo, one line, "Tôi cần giúp việc" / "Tôi là người giúp việc" open the phone login of that role). No landing page, trust badges or fixed prices.
- **Signed-in customer:** `CustomerShell` (`customer_shell.dart`): tabs Trang chủ · Đơn · Yêu thích · Tài khoản in an `IndexedStack` and a centre "Đặt ca" button that pushes `/booking`; detail screens are pushed on top without the bar. Coming back to a tab reloads it. `CustomerHomeTab`: greeting, the next open order (first non-final order of the list + its progress), quick booking with the server's quote of the default address, favourite workers; every section loads alone and a failure hides only it. `CustomerAccountTab`: profile, counts read from real answers (order list `total`, addresses, year joined), address book, order history, disputes, sign out. Widgets the API cannot feed (spending chart, status counts) are not shown.
- **Signed-in worker:** `WorkerShell` (`worker_shell.dart`): tabs Hôm nay · Lịch · Thu nhập · Tài khoản in an `IndexedStack` (a tab is built the first time it is opened, so the slots screen does not share the position at login) and a centre "Việc" button that pushes `/dispatch/offers`; detail screens are pushed on top without the bar. `WorkerTodayTab`: month net amount and finished jobs (`GET /api/workers/me/earnings`) and the next 7 days of on/off slots (`GET /api/workers/me/slots`). `WorkerSlotsScreen`, `PayoutScreen` and `WorkerScreen` (account: eKYC, company schedule, disputes, sign out) are both tabs and the pushed routes `/workers/slots`, `/payouts`, `/workers/profile` (app bar only when pushed). Widgets the API cannot feed (next job, booked cells, lifetime job count, criteria averages) are not shown.
- After the OTP login the Home shows the actions of the signed-in role (`home_actions.dart`): customer (book, my orders, addresses, profile, favourites); worker (eKYC, availability slots, job offers, earnings, profile, company schedule). "Đặt Ca Ngay" and "Vào Trực Ca" open the screen when a session of the right role exists, the login otherwise; "Đăng xuất" clears the session. The login answer does not say whether a worker belongs to an agency, so every worker sees "Lịch ca của công ty" (empty for a freelancer).
- One session: `identity/services/token_storage.dart` hands the tokens to `core/auth/token_storage.dart`, the storage `ApiClient` reads. Before, a login filled a storage `ApiClient` never looked at and every Booking / Workers / Dispatch / Payments call went out without a Bearer token.
- A new worker (a phone without a profile) fills `WorkerRegistrationScreen` (full name, 12-digit national id, address, optional bio) after the OTP, then signs in again with a new OTP (the registration answer carries no tokens). Field-work chain (#377): the accepted offer's card has "Đến nơi & Check-in" -> `/dispatch/check-in` -> "Bắt đầu ca làm" -> `/workers/execution` -> `/workers/photos` and "Gửi Nghiệm Thu" (`POST /api/workers/assignments/{id}/submit-completion`). Each of these routes takes the assignment id (`int`) as route argument (`withAssignmentArgument` in `workers/routes.dart`; without it a note is shown). Not covered yet: the API has no "my assignments" endpoint, so a job is reachable only from the accept result (not from Home after the app is reopened); the customer-absent and incident routes still use placeholder ids.

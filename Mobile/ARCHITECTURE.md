# Mobile Architecture: TỔ ẤM — Nordic Care

## 1. Overview
The Mobile application is built with Flutter for cross-platform iOS and Android deployment.
It serves two primary personas within a unified responsive client:
- **Customer (Gia chủ / Khách hàng):** Discovers services, books shifts, manages addresses, favorites preferred workers, and tracks jobs.
- **Worker (Chuyên viên giúp việc / Đối tác):** Receives 30-second job dispatches, verifies GPS check-in (≤100m error), and submits photo-verified inspections.

## 2. Design System: TỔ ẤM — Nordic Care
The visual identity follows the team design skill (`.agents/skills/project-team-design-template`):
- **Design Frame:** 390×844 standard mobile layout.
- **Canvas:** `#FAF8F5` / `#FAF8F3` (Warm Ivory / Paper texture).
- **Primary Brand:** `#2D5A43` (Deep Forest Pine) for primary CTA buttons and active headers.
- **Secondary Accent:** `#6B8F71` (Sage Green) for checkmarks, pill badges, and secondary highlights.
- **Accent Amber:** `#D99B26` (Warm Amber) for 5-star ratings and trust badges.
- **High-contrast Dark Anchor:** `#1A2F25` for the "Trụ Cột An Tâm" reassurance sections.
- **Typography:** Plus Jakarta Sans & Newsreader styling.
- **Elevation & Shapes:**
  - Pill buttons (`rounded-full` / `StadiumBorder`).
  - Double-bezel white cards with 1px hairline borders (`#E5E7EB`) and soft 16px corner radii.

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
- **`/booking` (`BookingScreen`)**, feature `lib/features/booking/` (contract `booking.md` 3.1 to 3.3): choose ECONOMY or PREMIUM, one of the customer's addresses (read through `IBookingAddressSource`, by default the Customers feature's address book), a day and a shift, an optional note (500) and, for PREMIUM only, an optional required skill (100). The **fixed price** is the server's quote (`GET /api/booking/price-quote`), shown before the order exists and asked again whenever the segment or the address changes; the device computes no money. "Đặt đơn" calls `POST /api/booking/orders` once per tap and opens `/payments` for the new order. Tiers, shifts, lead time and limits come from `GET /api/booking/options`; the only constant is `selectableDays` (14, the server's `Booking.MaxDaysAhead` default, which the options endpoint does not carry: the server stays the authority and answers 400 on `scheduledDate`).
- **`/payments` (`PaymentScreen`)**, feature `lib/features/payments/` (contract `payments.md` 2.1 to 2.3): takes `PaymentScreenArgs(orderId, extensionId?, orderCode?)` as route arguments. It asks the QR (`POST /api/payments/orders/{id}/qr`, or the extension one), shows the amount and the time left, and asks `GET /api/payments/{paymentId}` every 3 s until SUCCESS, EXPIRED or the deadline; the timers stop when the screen is left. The banner **"SANDBOX – no real money"** (decision Q04) is always on screen.
- **Two limits today:** the project has no QR-rendering package and no realtime (SignalR) client (`pubspec.yaml` has no dependency beyond Flutter, and adding one needs a ticket that names it). So the QR content is shown as selectable text, not as a scannable image, and "paid" arrives by polling, which is the contract's stated fallback. Nothing on the home screen opens `/booking` yet.
- `logic/booking_logic.dart` and `logic/payment_logic.dart` are pure (labels, VND, Asia/Ho_Chi_Minh days, validation, countdown, error texts by code). Services use the shared `ApiClient`. Tests: `test/features/booking/`, `test/features/payments/`, `test/core/network/api_exception_code_test.dart` (services against a local `HttpServer`, widget tests for every state).

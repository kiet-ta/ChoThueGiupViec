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

## 4. Verification
- `cd Mobile && flutter analyze`: Static linting and code quality.
- `cd Mobile && flutter test`: Unit and widget test execution.

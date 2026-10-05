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

## 3. Directory Structure
```
Mobile/
├── lib/
│   ├── app/                 # App lifecycle, MaterialApp, routes
│   │   ├── app.dart
│   │   └── routes.dart
│   ├── core/                # Shared theme, widgets, network, models
│   │   ├── models/
│   │   ├── theme/
│   │   └── widgets/
│   ├── features/            # Isolated vertical features per module
│   │   ├── home/
│   │   ├── identity/
│   │   ├── customers/
│   │   ├── booking/
│   │   ├── dispatch/
│   │   └── workers/
│   └── main.dart            # Flutter entrypoint
└── test/                    # Unit, widget, and golden tests
```

## 4. Verification
- `cd Mobile && flutter analyze`: Static linting and code quality.
- `cd Mobile && flutter test`: Unit and widget test execution.

# TỔ ẤM Mobile — System Design

The design system of the Flutter app (`Mobile/`). It distils the local `apple-design` Agent Skill (Apple's fluid-interface and design principles) and the owner's decisions of 2026-10-28 (PRs #477, #480, #482, #486). **Mobile only**: the team `DESIGN.md` and the Web keep their own values.
Worker-side hand-off: [DESIGN-SYSTEM-WORKER.md](DESIGN-SYSTEM-WORKER.md). Visual component template: [design-system/components.html](design-system/components.html).

Every screen redesign follows this file. When code and this file disagree, fix one of them in the same PR.

## 1. Principles

1. **Calm and clear.** One main action per screen, obvious. Show the common path first; details one tap deeper. Every element earns its place.
2. **Instant response.** Feedback starts the moment a finger touches down, never after a delay.
3. **Physical motion.** Things move on springs, start from where they are, can be interrupted, and enter and leave along the same path.
4. **Plain words.** Short, everyday Vietnamese. No jargon, no English labels, no internal codes in the foreground.
5. **Real homes, real people.** Photos of housework set the tone where a screen has room for it (welcome, sign-in, home).
6. **Everyone can use it.** WCAG AA contrast, large tap targets (≥ 44 px), reduced motion respected, text scaling does not break layouts.

## 2. Brand

- Name and mark: **TỔ ẤM**, the house icon on a primary rounded square.
- Promise: *Nhà sạch, lòng nhẹ.*
- Photography: the free Unsplash photos in `assets/images/welcome/` (sources in `CREDITS.txt`). A photo is always under a dark gradient when text sits on it.

## 3. Tokens

### 3.1 Colour — `lib/core/theme/nordic_colors.dart`

Redesign of 2026-10-30 (owner: Duy): a deep green with cool neutrals, so a screen can show depth (a dark header, white cards that float). Mobile only.

| Role | Token | Value | Use |
|---|---|---|---|
| Primary | `primary` | `#0F3D2E` | main buttons, the centre "Đặt ca" button, links, selected text; white text 12:1 |
| Primary pressed | `primaryHover` | `#0A2E22` | pressed primary |
| Accent green | `primaryContainer` / `secondaryAccent` | `#1F7A55` | selection rings, check badges, small accent text, snackbars; white text 5.3:1, 5.3:1 on white |
| Soft green | `primaryFixed` | `#DCEDE3` | "ok" pills, the selected tab pill; primary text on it 10:1 |
| Amber | `accentAmber` | `#F2B544` | stars, dots; never under text |
| Cream | `amberSoft` / `onAmberSoft` | `#FFF6C0` / `#5F4100` | warnings, banners, a warm avatar tint |
| Canvas | `canvas` | `#EEF1EC` | screen background |
| Surface | `surface` / `surfaceSubtle` | `#FFFFFF` / `#F4F6F2` | cards, fields / a selected tile |
| Dark | `contrastDark` / `contrastCard` | `#0F3D2E` / `#17573D` | the Home header, gradients over photos, dark cards |
| On dark | `textInverse` / `onDarkMuted` | `#FFFFFF` / `#A9C7B8` | text on the dark header (muted 6.7:1) |
| Text | `textTitle` / `textBody` / `textSecondary` | `#101814` / `#34433B` / `#5E6A63` | titles / body / hints (hints 5.0:1 on the canvas) |
| Hairline | `border` / `borderVariant` | `#DDE3DF` / `#CBD5CF` | card and field borders, dividers / outlined buttons |
| Error | `error` / `errorContainer` | `#BA1A1A` / `#FFDAD6` | errors |

Colour never carries meaning alone: a status always has a word too.

### 3.2 Type — `lib/core/theme/nordic_typography.dart`

One family: **Be Vietnam Pro** (built for Vietnamese diacritics; static TTFs 400–700 in `assets/fonts/`, SIL OFL 1.1). Newsreader and Plus Jakarta Sans are no longer used.

| Style | Size / weight / leading / tracking | Use |
|---|---|---|
| `display` | 30 / 700 / 1.1 / −0.6 (−0.02 em) | the largest title of a screen (tab titles, photo headers) |
| `greeting` | 28 / 700 / 1.15 / −0.56 | the Home greeting only |
| `numberLarge` / `numberMedium` / `numberSmall` | 34 / 26 / 20, 700, tabular, −0.02 em | the one big number of a screen (time, amount) |
| `appBarTitle` | 17 / 600 | title of a pushed screen |
| `titleMedium` | 18 / 700 | section titles inside a screen |
| `titleSmall` | 15 / 600 | card titles, list rows |
| `bodyRegular` | 14 / 400 / 1.5 | body text |
| `bodySmall` | 12 / 500 | hints, meta lines |
| `caps` | 11 / 600, +0.66 | small overlines (use sparingly) |

Rules: tracking tightens as size grows, body stays ~0; hierarchy comes from weight + size + leading together; never more than three text sizes in one card.

### 3.3 Spacing — `lib/core/theme/nordic_spacing.dart`

4-point scale `NordicSpace`: `xxs 4 · xs 8 · sm 12 · md 16 · gutter 20 · lg 24 · section 28 · xl 32`.
Screen sides: `gutter` (20). Inside a card: `md` (16). Between cards in a list: `sm` (12). Between sections: `section` (28).

### 3.4 Shape — `lib/core/theme/nordic_radius.dart`

Rounded and soft (owner decision: square corners were tried and rejected).
Buttons and chips: **pill**. Cards: **20**. Fields: **14**. Small tiles / icon squares: **12**. Sheets: **28** on top. Avatars, dots, round icon backgrounds: real circles.

### 3.5 Elevation — `lib/core/theme/nordic_elevation.dart`

Depth, not flatness (redesign 2026-10-30). Shadows are soft, layered and tinted with the deep green; there are only four:

| Token | Use |
|---|---|
| `NordicElevation.card` | every white `NordicCard` on the canvas, together with its 1 px hairline (`elevated: false` for a card inside another surface) |
| `NordicElevation.raised` | the one floating panel of a screen: the Home booking card that overlaps the dark header |
| `NordicElevation.cta` | the main dark button of the Home and the centre "Đặt ca" button |
| `NordicElevation.segment` | the selected segment of a segmented control |

No other shadows; no Material `elevation` numbers. On the dark header, panels are translucent white (8 % fill, 14 % border) instead of shadowed. Text over photos still sits on a gradient, not a shadow.

### 3.6 Motion — `lib/core/motion/`

| Token | Spring (damping / response) | Use |
|---|---|---|
| `NordicMotion.standard` | 1.0 / 0.35 s | anything that appears, moves or changes on its own; no overshoot |
| `NordicMotion.momentum` | 0.8 / 0.3 s | only after a flick or drag release |
| `NordicMotion.shake` | 0.3 / 0.12 s | a wrong entry (`ErrorShake`) |
| `NordicMotion.press` | 100 ms ease-out | press-down feedback |
| `NordicMotion.fade` | 200 ms | the reduced-motion replacement |

Pages: `NordicPageTransitionsBuilder` (theme-wide): slide in from the right, the page below moves back, drag back from the left edge 1:1. Never hand-make a page transition.
Controllers: `animateWith(spring.simulate(from: value, velocity: velocity))`. Implicit widgets: `curve: SpringCurve(spring)`, `duration: spring.settleDuration`.

### 3.7 Haptics

`heavyImpact` with a wrong entry (`ErrorShake`), `lightImpact` on a success that leaves the screen. Nowhere else.

## 4. Components

| Component | File | Notes |
|---|---|---|
| `NordicPage.tab` / `NordicPage.pushed` | `core/widgets/nordic_page.dart` | the scaffold of every screen (§5) |
| `NordicButton` (primary, secondary, ghost) | `core/widgets/nordic_button.dart` | pill, `Pressable`, loading state |
| `BottomActionBar` | `core/widgets/bottom_action_bar.dart` | the main button(s); `NordicPage(actions:)` places it |
| `NordicCard` | `core/widgets/nordic_card.dart` | white card with the `card` shadow; `onTap` makes it a pressable button; `elevated: false` flattens it |
| `NordicElevation` | `core/theme/nordic_elevation.dart` | the four shadow tokens (§3.5) |
| `DarkHeader` | `core/widgets/dark_header.dart` | deep-green header that bleeds to the top edge, rounded bottom, three rings; keeps `overlap` free for a `RaisedPanel`; optional app bar (back 44x44 + centred title) |
| `RaisedPanel` | `core/widgets/raised_panel.dart` | the one floating white panel of a screen (`raised` shadow, sheet radius); put it in `Transform.translate(Offset(0, -overlap))` over a `DarkHeader` |
| `GlassPanel` | `core/widgets/glass_panel.dart` | white 8 % panel with a 14 % border on a dark surface, no shadow; `pill` and `onTap` options |
| `GroupedListCard` | `core/widgets/grouped_list_card.dart` | settings-like rows in one card |
| `SectionTitle` | `core/widgets/section_title.dart` | section heading + optional link |
| `StatTile` | `core/widgets/stat_tile.dart` | one label, one number |
| `StatusPill` | `core/widgets/status_pill.dart` | a status in words, tone ok / warn / bad / neutral |
| `AvatarInitials` | `core/widgets/avatar_initials.dart` | people without photos |
| `Loading/Error/EmptyStateWidget` | `core/widgets/status_state_widget.dart` | the three non-content states |
| `Pressable` | `core/widgets/pressable.dart` | wrap any custom tappable |
| `ErrorShake` | `core/motion/error_shake.dart` | around the input that was wrong |
| `AuthPhotoHeader` / `AuthFormSheet` | `features/identity/widgets/auth_photo_header.dart` | photo header + rising sheet |

## 5. Screen patterns

- **Home (customer)**: a deep-green header that bleeds to the top edge (date, greeting, the default address as a pill that opens the address book, the next booking on a translucent panel or a one-line notice), then a white booking card raised over the header (`raised`): the segments as selectable tiles with the server's price, and one "Đặt ca · price" button (`cta`) that opens booking with the choice ticked; then the favourite helpers as cards with "Đặt lại". The list ends clear of the bar and the centre button.
- **Tab screen** (`NordicPage.tab`): no app bar; large `display` title, optional small subtitle above it (e.g. the date) and a header action on the right; content below in sections.
- **Pushed screen** (`NordicPage.pushed`): app bar with back and a short title (a noun: "Chi tiết đơn", "Địa chỉ"), never an internal code.
- **Form**: fields with labels above; validate inline while typing where possible; on submit, an error message under the field + `ErrorShake`; the submit button in `actions` (above the keyboard); a field never hidden by the keyboard.
- **List**: cards with `sm` gaps, newest first, grouped by month when long; each row: title (what), one meta line (when · how much), a status pill on the right; the whole card is the tap target.
- **Detail with a dark header** (order detail, account): a `DarkHeader` (with the app bar on a pushed screen) holds the big figure or the person, the facts sit on a `RaisedPanel` that rises over it, the rest follows on the `canvas`. A cancelled order shows one red cross line in the timeline, never a tick, and no workers section.
- **Detail** (order detail, payment): the one big number first (time or amount) in a dark `contrastDark` block with the `raised` shadow (figure in `textInverse`, secondary text in `onDarkMuted`, the status pill below it), then the facts and the timeline in a white card, then people, then actions at the bottom. One `raised` block per screen; panels on the dark block are white at 8 % with a 14 % white border and no shadow. List and form screens stay on the light `canvas`.
- **Choice tile** (booking tier, day, shift, address): a white tile with a `primaryContainer` border and a check mark when chosen. The main `NordicButton` (primary) carries the `cta` shadow by itself; a disabled or loading one lies flat.
- **States**: loading = `LoadingStateWidget`; error = `ErrorStateWidget` with "Thử lại"; empty = `EmptyStateWidget` with one sentence and, when it helps, one action.
- **Full width**: no width cap on phones; content keeps the 20 px gutter, photos and headers bleed to the edges.

## 6. Copy

Vietnamese, sentence case ("Đăng nhập", not "Đăng Nhập"), short, everyday words, the user as "bạn".

| Avoid | Write |
|---|---|
| Economy / Premium | Cơ bản / Cao cấp |
| Xác thực, OTP (in titles) | mã xác nhận |
| Gửi Mã Xác Thực | Nhận mã |
| Xác Nhận Đăng Nhập | Đăng nhập |
| Vui lòng nhập … hợp lệ | … chưa đúng. Ví dụ: … |
| GV261026DEMO01 as a title | "Chi tiết đơn"; the code small, in the facts |
| 9 / 9 đơn | 9 đơn |
| Thợ (customer side) | người giúp việc (short: "người làm" in tight spots) |

Numbers: money `260.000 đ`, time `13:00`, date `T3, 27/10`. Errors say what is wrong and how to fix it, in one sentence, without blaming.

## 7. Accessibility

AA contrast for text (pairs checked in `test/theme_test.dart`), tap targets ≥ 44 px, `Semantics` labels on icon-only buttons, decorative photos excluded from semantics, reduced motion (§3.6), high contrast makes materials solid.

## 8. Done checklist for a redesigned screen

- [ ] Uses `NordicPage` (tab or pushed); title in plain words.
- [ ] Only tokens: colours, type styles, `NordicSpace`, `NordicRadius`; no stray numbers for radius or colour.
- [ ] Tappable cards use `NordicCard(onTap:)` / `Pressable`; buttons are `NordicButton`.
- [ ] Main action in `actions` (above the keyboard).
- [ ] Copy follows §6 (no English labels, no codes as titles, sentence case).
- [ ] Loading, error and empty states present.
- [ ] Works at 360 px and 428 px wide, with the keyboard open, under reduced motion.
- [ ] Existing behaviour, keys and service calls unchanged; tests updated for new copy.

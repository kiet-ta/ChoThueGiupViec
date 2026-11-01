# Frontend ARCHITECTURE.md

React 19 + TypeScript + Vite + Tailwind CSS v4 + shadcn/ui.

```
Frontend/
  src/
    components/ui/   shadcn/ui components (generated; add with `npx shadcn@latest add <name>`)
    lib/utils.ts     `cn()` helper
    services/api.ts  fetch client; backend envelope ApiResponse<T> { success, message, data }
    app/             shell, FROZEN after WEB-BASE-01 (router, area layout, feature registry); nobody edits it to add a feature
    features/        feature modules (components, hooks, api per feature); each has a routes.tsx
    pages/           route-level pages
    hooks/           shared hooks
    types/           shared TS types
    App.tsx, main.tsx, index.css (Tailwind + shadcn theme tokens)
  components.json    shadcn config (alias @ -> src)
  vite.config.ts     alias `@`, tailwind plugin, dev proxy /api -> backend
  .env.example       VITE_API_PROXY_TARGET, VITE_API_BASE_URL
```

## API client (WEB-BASE-03)
- Always call the backend through `apiClient` from `src/services/api.ts`, with paths relative to `/api` (no leading `/api`):
  ```ts
  import { apiClient, ApiError } from '@/services/api'
  const items = await apiClient.get<Dispute[]>('/admin/disputes', { query: { status: 'OPEN', page: 1 } })
  try { await apiClient.post<Dispute>('/workers/me/disputes', body) }
  catch (e) { if (e instanceof ApiError && e.status === 400) show(e.fieldErrors) }
  ```
- A call returns `data` of a 2xx envelope, otherwise throws `ApiError { status, message, data, fieldErrors, retryAfterSeconds }`. `fieldErrors` is the `data.errors` map of a 400 (decision O5); `retryAfterSeconds` comes from `Retry-After` (423, 429). A non-JSON or empty body becomes an `ApiError` with the HTTP status; a network failure is status `0`.
- Auth hook for WEB-BASE-02: `configureApi({ getAccessToken, onUnauthorized })`. The Bearer header is sent only when a token exists; `onUnauthorized` runs once for a 401 on such a request (an anonymous 401, e.g. wrong password, does not trigger it). Features never call `configureApi`.
- Types are hand-written per feature from the approved contracts in `.spec/contracts/` until gate G4 (OpenAPI client generation needs a package that no ticket has added). Until the backend of a module exists, a feature keeps its mock data in its own folder and swaps to `apiClient` later.
- Tests: `npm test` (`node --test`, no extra package) runs `tests/**/*.test.ts`; the pure core in `src/services/client.ts` has no `import.meta`, so it runs outside Vite.

## Conventions
- Import via alias: `import { Button } from '@/components/ui/button'`.
- Call the API only through `src/services/api.ts` using relative `/api/...` paths (proxied in dev).
- For production set `VITE_API_BASE_URL` to the API URL and enable CORS in Backend.
- Style with Tailwind utilities; use shadcn components instead of custom ones when available.
- Only `VITE_`-prefixed env vars are exposed to the browser: never put secrets there (Stripe secret key stays in backend; frontend may use the publishable key only).

## Routing and feature modules (WEB-BASE-01)
- Router: `react-router-dom` (chosen by the owner on 2026-10-06; ARCHITECTURE.md named none before). Two desktop areas (decision D3, 1440x900 reference): **Admin console** at `/admin/*` and **Partner Portal** at `/partner/*`, each with a 240 px sidebar + main content layout (`src/app/AreaLayout.tsx`). `/` redirects to `/admin`.
- **Auto-loading:** `src/app/feature-registry.ts` uses `import.meta.glob('../features/*/routes.tsx', { eager: true })`. A feature adds routes by editing **only its own** `src/features/<name>/routes.tsx`, which must `export default` a `FeatureModule` (`src/app/feature-module.ts`): `{ area: 'admin' | 'partner', routes: RouteObject[], nav?: NavItem[] }`. `routes` are relative to the area root; `nav` entries (`label`, `to`, optional `section`) fill that area's sidebar. No file in `src/app/**` changes when a feature is added.
- Feature folders created by the base: `agencies` (partner), `skills`, `workers`, `dispatch`, `ratings`, `disputes`, `payouts`, `admin` (admin), each with an empty `routes.tsx`. The owner of a module fills it in.
- Each area is wrapped by `RequireRole` (WEB-BASE-02): `/admin/*` needs the `Admin` role, `/partner/*` the `Partner` role; see "Authentication and session" below. Paths no feature owns render a not-found page **inside** the area, so the login guard applies first.
- Theme: layouts use the shadcn theme tokens (`bg-sidebar`, `text-sidebar-foreground`, ...). Mapping the TO AM design tokens into `src/index.css` is a separate ticket (design skill, section 4); do not do it inside a feature ticket.

## Public landing page (WEB-LANDING-01, ticket #569)
- `/` renders `src/pages/LandingPage.tsx` (public, no login); it used to redirect to `/admin`. The consoles stay at `/admin` and `/partner`, sign-in at `/login`.
- Code lives in `src/features/landing/`: one component per section, `data.ts` (copy, plans, photo URLs), `pricing.ts` (pure price estimate, unit-tested in `tests/landing-pricing.test.ts`; it is a marketing estimate, the backend price is the source of truth), `landing.css` (scoped under `.landing`, inside `@layer components`) and `motion.ts` (the only place that imports `gsap`).
- Dependency added: `gsap` (ScrollTrigger is part of it). Motion respects `prefers-reduced-motion`; ScrollTriggers are reverted on unmount; the pinned "3 layers" sequence is desktop-only (>= 1024 px).
- The figures and quotes in `PLACEHOLDER_PROOF` (`data.ts`) are placeholders without a source: replace before launch. Visual reference: `ui-template/plandingPageWeb.html`.

## Authentication and session (WEB-BASE-02, contract `.spec/contracts/identity.md` 2.3-2.6)
- **Login:** `/login` (`src/pages/LoginPage.tsx`) takes email + password + role (`Admin` | `Partner`) and calls `POST /api/auth/password/login`. Errors: 400 field messages under the fields, 401 one generic message (no hint which part was wrong; the password field is emptied), 403 account disabled, 423 locked with the `Retry-After` time in minutes.
- **Where tokens live (decision of this ticket, leader to confirm):** the access token is kept in **memory only**; the refresh token is kept in `sessionStorage` (key `giupviec.refreshToken`) so a reload can get a new access token and the session ends when the tab closes. `localStorage` is not used. The backend sets no httpOnly cookie, so a script-readable store is the only option for the refresh token, and any XSS can read it: keep the app free of `dangerouslySetInnerHTML` and unreviewed third-party scripts. A refresh token left in the tab is revoked by logout.
- **Refresh:** on start, with a stored refresh token, `bootstrap()` calls `POST /api/auth/refresh` before any protected page renders (a spinner shows meanwhile); while logged in a timer refreshes at 80 % of `accessTokenExpiresInSeconds` (minimum 5 s). Refresh tokens rotate, so parallel callers share one in-flight request; any failed refresh ends the session. A network error also ends it (the user logs in again), which is the safe choice for a rotating token.
- **401 handling:** `configureApi({ getAccessToken, onUnauthorized })` is wired in `src/auth/index.ts`; a 401 on a request that carried a token drops the session, and `RequireRole` sends the user to `/login`. The calls of login and refresh are anonymous, so their 401 does not trigger it.
- **Guard:** `decideAccess` (`src/auth/access.ts`): no session -> `/login?next=<path>`; the other area's role -> redirect to the own area home (the page of the wrong area is never rendered); roles without an area here (Customer, Worker) are treated as not logged in. `safeNext` accepts `next` only as a same-origin path inside the user's own area, which closes the open-redirect hole (`//host`, `https://...`, backslashes, other area all fall back to the area home).
- **Logout:** the "Đăng xuất" button in the area layout calls `POST /api/auth/logout` with the refresh token and clears memory and `sessionStorage` even when that call fails.
- **Layout of the code:** pure logic with injected storage, clock, timers and HTTP calls in `src/auth/session.ts`, `auth-controller.ts`, `access.ts`, `login-errors.ts` (unit-tested in `tests/auth.test.ts`); React glue in `RequireRole.tsx`, `useAuth.ts`, `index.ts` (the single controller), `auth-api.ts` (the three endpoints).

## Commands
`npm install`, `npm run dev` (http://localhost:5173), `npm run build`, `npm run lint`, `npm test`.

## Shared components (WEB-BASE-04)
Live in `src/components/` (outside `features/`, so no module owns them). No extra package; theme colours come from the shadcn tokens (mapping the TO AM tokens into `index.css` is a separate ticket).
- **`DataTable<T>`** (`@/components/data-table`): `columns: Column<T>[]` (`id`, `header`, `cell(row)`, optional `align`, `className`), `rows`, `rowKey`, and optional `loading` (skeleton rows, `aria-busy`), `error` + `onRetry`, `emptyMessage`, `onRowClick`, `toolbar`, `pagination`. Presentational: the caller owns data, paging and filters because they are server-side (contract shape `{ items, page, pageSize, total }`). Many columns scroll horizontally.
- **`Pagination`**: `page` (1-based), `pageSize`, `total`, `onPageChange`, optional `pageSizeOptions` + `onPageSizeChange`; shows "Hiển thị a–b trên tổng N". Range and page-button maths are pure functions in `paging.ts`.
- **`DataTableToolbar<T>`**: controlled `search`, controlled chip `filter` (`options`, `value`, `onChange`), and `exportCsv { fileName, columns, rows }` which downloads the rows currently shown as `.csv` (`toCsv`: UTF-8 BOM for Excel, CRLF, quotes escaped, text starting with `= + - @` neutralised against formula injection). Real bank files stay server-side (`.xlsx`, payouts contract section 2.3).
- **`BeforeAfterViewer`** (`@/components/photo-compare`): `before` and `after` are `ComparePhoto[]` (`angleNo`, `url`, optional `volScore`, `isAccepted`, `caption`). `pairPhotos` pairs them by `angleNo` (decisions Q03): per angle an accepted photo beats a rejected retake, the latest wins; an angle missing on one side shows "Thiếu ảnh". Modes "Cạnh nhau" and "Kéo thanh" (slider overlay), previous/next and angle tabs, ArrowLeft/ArrowRight (ignored while the focus is on the slider or another form control, rules in `viewer-keys.ts`), VoL badge ("Đạt nét" / "Bị mờ").
- Tests: `tests/components.test.ts` covers `paging.ts`, `csv.ts`, `pair-photos.ts` and `viewer-keys.ts` (`npm test`).

## Shared pieces of the Admin pages (WEB-M6-04)
- **`src/lib/format.ts`** (pure, unit-tested in `tests/format.test.ts`): `formatVnd` (whole VND, dot as thousands separator, `1.234.567 đ`, not locale dependent), `formatDateTime` (an ISO instant shown as `dd/MM/yyyy HH:mm` in `Asia/Ho_Chi_Minh`, decision G-3: the API sends UTC, the console shows local time), `formatSlaRemaining` (`01h 45p`, hours may pass 24 as in the design, `Quá hạn 02h 10p` when overdue), `disputeCode` (`#TC-1042`), `workerTypeLabel` (`Freelancer` / `Agency · <name>`).
- **`src/components/status/`**: `StatCard` (caption plus one or two big numbers; a `null` value shows a dash, never a made-up 0; `tone: 'danger'` colours a number red) and `StatusBadge` (a pill with a tone; the text always carries the meaning).
- **`src/hooks/use-resource.ts`**: `useResource(load, deps)` returns `{ data, loading, error, reload }`; a slow answer of an older request never overwrites a newer one and the previous data stays while a new request runs; `errorText` turns an `ApiError` into the line an Admin reads.
- A feature keeps its pure mapping in a `view.ts` that imports only `.ts` files with relative paths, so `node --test` can run it without Vite (the alias `@` and `import.meta` exist only in the bundle).

## Operations dashboard (WEB-M6-04, contract `admin.md` 2.3)
- Route `/admin/dashboard` (nav "Tổng quan"), feature `src/features/admin/`: `dashboard/{types,api,view}.ts` and `DashboardPage.tsx`. Three cards (orders today / this week, shifts in progress / completed today, open disputes / near SLA, the last red while above 0) from `GET /api/admin/dashboard`, then the disputes close to their SLA from `GET /api/admin/disputes?nearSla=true&pageSize=5` (table `Mã KN`, `Khách hàng`, `Thợ liên quan`, `SLA còn lại`, link to `/admin/disputes/<id>`) with "Xem tất cả khiếu nại". No metric beyond the three groups (decision G-7).
- States: loading (skeleton cards and rows), empty ("Không có tranh chấp sắp quá hạn"), error with a retry button per block (a failed list does not hide the numbers and the other way round), and a 401 sends the user to `/login` through the shell.
- The `/admin` home is still the placeholder of the shell (`src/app/router.tsx` is frozen and not in this ticket); making `/admin` open the dashboard is a one-line change that needs its own ticket listing that file.

## Dispute console (WEB-M6-01, contract `disputes.md` 2.2-2.3)
- Routes `/admin/disputes` (queue; nav "Khiếu nại tranh chấp", section "XỬ LÝ TRANH CHẤP") and `/admin/disputes/:disputeId` (case page), feature `src/features/disputes/`: `types.ts`, `api.ts`, the pure `view.ts`, `DisputeQueuePage.tsx`, `DisputeCasePage.tsx`, `VerdictPanel.tsx`.
- **Queue:** chips for status (Đang mở = `OPEN,IN_REVIEW`, Đã xử lý = `RESOLVED,DISMISSED`) and priority (only for unresolved tickets: the server ignores it otherwise), a paged table in the order of the Figma, the "Auto-Cancelled Dispute" tag for `ABSENT_FEE`.
- **Case page:** header with status, priority, category and SLA; "Nhận xử lý" for an OPEN ticket; the shift timeline (GPS and VoL badges), a plain "chưa có dữ liệu checklist" because the API returns `checklist: null` (contract D6); the description and evidence links; the photos in the shared `BeforeAfterViewer` (one tab per assignment when the order has several); the verdict.
- **Evidence links are untrusted:** `safeEvidenceUrl` lets only an `http(s)` address or a path on this site become a link or an image; `javascript:`, `data:`, `file:` and protocol-relative addresses are shown as "Bằng chứng không mở được".
- **Verdict form (`VerdictPanel`):** the verdicts offered follow the order's workers (`faultOptions`: FREELANCER only with a freelancer, AGENCY only with agency staff, CUSTOMER and a dismissal always); `validateResolve` mirrors the contract's 400 rules (note 1-255, amount whole and not negative, amount only for FREELANCER or AGENCY, lock only for FREELANCER) and the server's field messages are shown under the fields; a confirmation step lists the effect in words (`effectText`); a ref guard prevents a second request; 409 reloads the case, 502 says the refund was refused and nothing changed. The lock checkbox says honestly that the request is recorded but the lock is done by the Workers module and is not wired yet.
- **Where the Figma and the contract differ, the contract wins:** the Figma offers a cleaning session as compensation and SLA point choices of -2/-5/-10; the contract refunds money through `IRefundService` and fixes the -5 of decision Q09.
- Tests: `tests/disputes.test.ts` (labels, queue row, timeline, photos, fault options, amount parsing, every validation rule and boundary, request body, effect text, error mapping, URL sanitiser).
## Absence approval (WEB-M6-02, contract `admin.md` 2.2)
- Route `/admin/absence-reports` (nav "Biên bản vắng mặt", section "QUẢN LÝ NHÂN SỰ"), files `src/features/admin/absence/{types,api,view}.ts` and `AbsencePage.tsx`. Tabs Chờ duyệt / Đã duyệt / Từ chối (`status` PENDING / APPROVED / REJECTED), a paged `DataTable`, and a detail panel for the selected row (fetched again with `GET .../{assignmentId}` so it is never stale).
- The panel shows GPS (verified, distance, coordinates), the number of calls (the per-call table of the Figma is not in the contract, question A5), the minutes waited, the door photo if there is one, and the money split. "Duyệt bồi hoàn 40%" is disabled while `canApprove` is false and the `blockReasons` are written out in words; clicking it asks for a confirmation that states both amounts and only "Xác nhận" calls `POST .../approve`; a ref guards against a second request while one is running. "Bác yêu cầu" needs a reason of 1-255 characters (checked in `validateReason` before sending; a 400 field message is shown under the box).
- Errors (`view.ts` `actionError`): 409 with `data.blockReasons` lists the reasons, a 409 without them says the report was already decided and the list reloads; 502 says the refund was refused and nothing changed (the confirmation stays so the Admin can retry); 404, 400 and network failures have their own lines.
- Tests: `tests/admin-absence.test.ts` (statuses, reason wording, row mapping, distance text, money split, reason validation at 0/1/255/256 characters, error mapping).
## Payout batches (WEB-M6-03, contract `payouts.md` 2.1-2.4)
- Routes `/admin/payout-batches` (list and build; nav "Kỳ payout", section "TÀI CHÍNH") and `/admin/payout-batches/:batchId` (detail), feature `src/features/payouts/`: `types.ts`, `api.ts`, `download.ts`, the pure `view.ts`, `PayoutBatchesPage.tsx`, `PayoutBatchDetailPage.tsx`. There is no Figma frame for this screen; it follows the other Admin pages and the shared `DataTable`.
- **Build:** the month is chosen from the finished months only (`finishedMonths`, computed on the Asia/Ho_Chi_Minh calendar so the running month is never offered, also just after local midnight while UTC is still the last day); `POST /api/admin/payout-batches` answers 201 (new) or 200 (rebuilt DRAFT) and the page opens the batch; 400 and 409 are explained.
- **Detail:** the batch header, the `warnings` of payees without a bank account (translated by `warningText`), the items in a paged `DataTable` with payee-type chips, a red "Chưa có" badge for a missing account number (the account is always text, so leading zeros survive), the penalty shown as a deduction.
- **Bank files:** `download.ts` fetches `GET .../export?type=` with the Admin's token (`authController.getAccessToken`) as a blob because the success answer is the `.xlsx` and not the JSON envelope (`apiClient` cannot carry it), saves it under the server's `Content-Disposition` name (`parseFileName`: a path or control character falls back to a safe name) and turns an error answer back into its envelope message; a 401 ends the session like any other request.
- **Draft actions:** "Dựng lại từ dữ liệu hiện tại" and "Xác nhận giải ngân" (a confirmation that states the month, the payees and the total and says plainly that the system calls no bank: the Admin records the transfers made from the exported file, decision G-1). A ref guard sends one request per click; a 409 explains and reloads. A CLOSED batch is read-only.
- Tests: `tests/payouts.test.ts` (month calendar edges, labels, row mapping, confirmation text, error mapping, file name parsing, file error messages, warning text).

## Audit log (ADMIN-UAT-07, contract `admin.md` 2.4)
- `/admin/audit-logs` (`features/admin/audit/AuditPage.tsx`, sidebar section "HỆ THỐNG"): read-only table of `GET /api/admin/audit-logs`, newest first, 20 per page. Filters: entity type (the seven types the backend writes), entity id, actor (Admin / system), from and to day. A day is a Vietnam calendar day: `from` is its start (17:00 UTC of the day before) and `to` is the start of the NEXT day, so the whole last day is included; a range that ends before it starts is refused on the page.
- Rows of a dispute link to the case. Pure helpers (labels, actor, change text, the query, the range check) are in `audit/view.ts` and tested in `tests/admin-audit.test.ts`. No write action exists.

## Customer and separate Admin sign-in (WEB-CUST-01)
- `/login` offers Customer OTP and Partner password tabs. Customer OTP uses `/auth/otp/request` and `/auth/otp/verify`; resend availability is taken from the server. `/admin` displays the separate fixed-role Admin login when anonymous.
- `/app/*` is guarded for Customer and renders the customer shell and placeholders. Admin and Partner retain their own guarded areas. `safeNext` limits redirects to the signed-in role's area.
- All three web roles use the same session policy: access token in memory, refresh token in `sessionStorage`, rotated on refresh and revoked on logout. The OTP API currently does not return remaining attempts on a wrong code.

## Customer Home, Booking and Payment (WEB-CUST-02)
- `CustomerShell` mounts the customer routes with `useRoutes`; paths without a screen yet stay placeholders. They are not in `customer/routes.tsx` because `src/app/feature-registry.ts` loads every `features/*/routes.tsx` as an Admin/Partner module. Routes: `/app` (Home), `/app/booking`, `/app/payments/:orderId`.
- Home: next open order from `GET /booking/orders` (a shift of today that already ended is skipped, using the shift end times of `GET /booking/options`) and a quick-book card with the server price (`GET /booking/price-quote`) of the default address. The spend chart is not built (no endpoint).
- Booking: `GET /booking/options`, `GET /customers/me/addresses`, price per tier from `price-quote`, `POST /booking/orders` then redirect to payment. Shifts that are in the past, or inside the Premium lead time, are disabled; 400 shows the field message, 409 `SHIFT_IN_PAST` / `PREMIUM_LEAD_TIME` a fixed sentence.
- Payment: `POST /payments/orders/{id}/qr` (idempotent), then `GET /payments/{id}` every 3 s while `PENDING`. The state is shown in words plus icon; `SUCCESS`/`PAID` = paid, `EXPIRED` = cancelled, anything unknown = failed. The SANDBOX banner is always shown. No QR package: when `qrPayload` is an https image URL (SePay returns a VietQR image `https://qr.sepay.vn/img?...`, `qrImageUrl`) the page shows it as an `<img>`; otherwise it offers the `payUrl` button ("Mở trang thanh toán") and the copyable `qrPayload` link (always present). Pure parts are in `booking/view.ts` and `payment/view.ts` (tests: `tests/customer-booking.test.ts`, `tests/customer-payment.test.ts`).

## Customer Orders, Order detail and Extension (WEB-CUST-03)
- Routes (in `CustomerShell`): `/app/orders` (list), `/app/orders/:orderId` (detail), `/app/orders/:orderId/extension` ("Làm lần 2"), `/app/payments/extension/:extensionId` (the WEB-CUST-02 payment page with `kind="extension"`, same SANDBOX banner, countdown and 3 s poll).
- List: `GET /booking/orders` 20 per page and one status per tab (the endpoint filters by order status; tab counts are not built, no API). Detail: `GET /booking/orders/{id}` plus `/progress` every 5 s until the order is COMPLETED or CANCELLED; 4-step timeline; worker card; address; cancel with a reason (1 to 255, contract 3.7) behind a confirmation.
- `orders/view.ts` `orderActions` maps order status + visible assignments + extension to the allowed actions (pay, cancel, extend, acceptance, rating within 48 h, dispute within 24 h). The acceptance, rating and dispute links are shown but open in WEB-CUST-04.
- Extension: 0.5 to 4 h in 0.5 steps, `POST /booking/orders/{id}/extensions`; the price is shown only after the request (the server computes it), then the customer pays the extension QR and the page waits for the worker (`PAID` -> `ACCEPTED` / `DECLINED`).

## Customer Acceptance, Rating and Disputes (WEB-CUST-04)
- Routes (in `CustomerShell`): `/app/orders/:orderId/acceptance`, `/app/orders/:orderId/rating`, `/app/disputes`, `/app/disputes/new?order=<id>`; the "Sau ca" links of the order detail and "Khiếu nại của tôi" on the orders page open them.
- Acceptance: the 5 angles (before/after) from `GET /workers/assignments/{id}/photos`; each file needs the bearer token, so it is fetched as a blob (`apiClient.download`) and shown through an object URL (an https `photoUrl` is the fallback). `POST /customers/assignments/{id}/accept` and `/request-redo` (reason 1 to 255). The backend sends no acceptance deadline, so the page shows no countdown and says so.
- Rating: `GET .../rating-window` (48 h countdown from `closesAt`, a closed window is explained by its `reason`) and `POST .../rating`. The form has the three criteria; the required `stars` is their rounded average (`overallStars`).
- Disputes: list `GET /customers/me/disputes` (status shown from the server value, unknown values as sent); create = upload each photo as base64 JSON to `/customers/me/disputes/evidence` (jpeg/png/webp up to 5 MB, max 10), then `POST /customers/me/disputes` with category, description 1 to 1000 and the returned URLs. One dispute per order (a second is a 409 with a Vietnamese message). Pure parts: `after-shift/view.ts`, tests `tests/customer-after-shift.test.ts`.

## Customer Account, Addresses and Favorites (WEB-CUST-05)
- Routes (in `CustomerShell`): `/app/account` (hub), `/app/profile`, `/app/addresses`, `/app/addresses/new`, `/app/addresses/:addressId`, `/app/favorites`. The hub counts come from the address, favorites and disputes lists and fall back to no number when a list fails; "Ca đã đặt" and "Tham gia" are not built (no API).
- Address form: province and ward from `account/vn-admin-units.ts` (the 34 provinces and 3,321 wards in force since 2025-07-01, generated from the Mobile catalogue and loaded with a dynamic `import()` into its own 58 KB chunk); the API field `city` is the province and `district` the ward. `floorAreaM2 x numFloors` is shown live with the price band and the number of workers (`totalArea`, `areaBracket`: 30 / 80 m², the same as booking.md 1.3), but the server computes `totalAreaM2`, which is never sent. Rules mirror customers.md 2.2 (room up to 30 m², apartment and room 1 floor, house up to 10 floors). The API needs a latitude and longitude: the form uses the browser location when the customer taps "Dùng vị trí của tôi", otherwise the default of the city. A field's error disappears when the field is edited.
- Delete asks for confirmation; an address that has orders answers 409 and the page says so.
- Favorites: `GET /customers/me/favorite-workers` with rating, completed jobs and work status (`IDLE`, `BUSY`, `LOCKED`, `RETIRING`, `RETIRED`, `PENDING`); "Đặt thợ này" opens `/app/booking?worker=<id>`, where the booking page shows the worker and sends `preferredWorkerId` for Economy (booking.md 3.3: a wish, not a guarantee). Not built: greying the shifts the worker is not free for (`.../availability`).
- Profile: `GET/PUT /customers/me`; the phone number is read-only.

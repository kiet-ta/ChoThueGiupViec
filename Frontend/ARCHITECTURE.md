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

## Absence approval (WEB-M6-02, contract `admin.md` 2.2)
- Route `/admin/absence-reports` (nav "Biên bản vắng mặt", section "QUẢN LÝ NHÂN SỰ"), files `src/features/admin/absence/{types,api,view}.ts` and `AbsencePage.tsx`. Tabs Chờ duyệt / Đã duyệt / Từ chối (`status` PENDING / APPROVED / REJECTED), a paged `DataTable`, and a detail panel for the selected row (fetched again with `GET .../{assignmentId}` so it is never stale).
- The panel shows GPS (verified, distance, coordinates), the number of calls (the per-call table of the Figma is not in the contract, question A5), the minutes waited, the door photo if there is one, and the money split. "Duyệt bồi hoàn 40%" is disabled while `canApprove` is false and the `blockReasons` are written out in words; clicking it asks for a confirmation that states both amounts and only "Xác nhận" calls `POST .../approve`; a ref guards against a second request while one is running. "Bác yêu cầu" needs a reason of 1-255 characters (checked in `validateReason` before sending; a 400 field message is shown under the box).
- Errors (`view.ts` `actionError`): 409 with `data.blockReasons` lists the reasons, a 409 without them says the report was already decided and the list reloads; 502 says the refund was refused and nothing changed (the confirmation stays so the Admin can retry); 404, 400 and network failures have their own lines.
- Tests: `tests/admin-absence.test.ts` (statuses, reason wording, row mapping, distance text, money split, reason validation at 0/1/255/256 characters, error mapping).

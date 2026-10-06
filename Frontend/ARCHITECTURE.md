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
- Role guards and token storage arrive with WEB-BASE-02; until then both areas are open.
- Theme: layouts use the shadcn theme tokens (`bg-sidebar`, `text-sidebar-foreground`, ...). Mapping the TO AM design tokens into `src/index.css` is a separate ticket (design skill, section 4); do not do it inside a feature ticket.

## Commands
`npm install`, `npm run dev` (http://localhost:5173), `npm run build`, `npm run lint`.

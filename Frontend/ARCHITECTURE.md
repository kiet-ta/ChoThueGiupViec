# Frontend ARCHITECTURE.md

React 19 + TypeScript + Vite + Tailwind CSS v4 + shadcn/ui.

```
Frontend/
  src/
    components/ui/   shadcn/ui components (generated; add with `npx shadcn@latest add <name>`)
    lib/utils.ts     `cn()` helper
    services/api.ts  fetch client; backend envelope ApiResponse<T> { success, message, data }
    features/        feature modules (components, hooks, api per feature)
    pages/           route-level pages
    hooks/           shared hooks
    types/           shared TS types
    App.tsx, main.tsx, index.css (Tailwind + shadcn theme tokens)
  components.json    shadcn config (alias @ -> src)
  vite.config.ts     alias `@`, tailwind plugin, dev proxy /api -> backend
  .env.example       VITE_API_PROXY_TARGET, VITE_API_BASE_URL
```

## Conventions
- Import via alias: `import { Button } from '@/components/ui/button'`.
- Call the API only through `src/services/api.ts` using relative `/api/...` paths (proxied in dev).
- For production set `VITE_API_BASE_URL` to the API URL and enable CORS in Backend.
- Style with Tailwind utilities; use shadcn components instead of custom ones when available.
- Only `VITE_`-prefixed env vars are exposed to the browser: never put secrets there (Stripe secret key stays in backend; frontend may use the publishable key only).

## Shared components (WEB-BASE-04)
Live in `src/components/` (outside `features/`, so no module owns them). No extra package; theme colours come from the shadcn tokens (mapping the TO AM tokens into `index.css` is a separate ticket).
- **`DataTable<T>`** (`@/components/data-table`): `columns: Column<T>[]` (`id`, `header`, `cell(row)`, optional `align`, `className`), `rows`, `rowKey`, and optional `loading` (skeleton rows, `aria-busy`), `error` + `onRetry`, `emptyMessage`, `onRowClick`, `toolbar`, `pagination`. Presentational: the caller owns data, paging and filters because they are server-side (contract shape `{ items, page, pageSize, total }`). Many columns scroll horizontally.
- **`Pagination`**: `page` (1-based), `pageSize`, `total`, `onPageChange`, optional `pageSizeOptions` + `onPageSizeChange`; shows "Hiển thị a–b trên tổng N". Range and page-button maths are pure functions in `paging.ts`.
- **`DataTableToolbar<T>`**: controlled `search`, controlled chip `filter` (`options`, `value`, `onChange`), and `exportCsv { fileName, columns, rows }` which downloads the rows currently shown as `.csv` (`toCsv`: UTF-8 BOM for Excel, CRLF, quotes escaped, text starting with `= + - @` neutralised against formula injection). Real bank files stay server-side (`.xlsx`, payouts contract section 2.3).
- **`BeforeAfterViewer`** (`@/components/photo-compare`): `before` and `after` are `ComparePhoto[]` (`angleNo`, `url`, optional `volScore`, `isAccepted`, `caption`). `pairPhotos` pairs them by `angleNo` (decisions Q03): per angle an accepted photo beats a rejected retake, the latest wins; an angle missing on one side shows "Thiếu ảnh". Modes "Cạnh nhau" and "Kéo thanh" (slider overlay), previous/next and angle tabs, ArrowLeft/ArrowRight, VoL badge ("Đạt nét" / "Bị mờ").
- Tests: `tests/components.test.ts` covers `paging.ts`, `csv.ts` and `pair-photos.ts` (`npm test`).

## Commands
`npm install`, `npm run dev` (http://localhost:5173), `npm run build`, `npm run lint`, `npm test`.

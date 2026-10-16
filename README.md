# CommonService (monorepo)

| Folder | Stack | Docs |
|---|---|---|
| `Backend/` | ASP.NET Core, .NET 10, Clean Architecture + MediatR | [Backend/ARCHITECTURE.md](Backend/ARCHITECTURE.md) |
| `Frontend/` | React + TypeScript + Vite + Tailwind v4 + shadcn/ui | [Frontend/ARCHITECTURE.md](Frontend/ARCHITECTURE.md) |

## Run (dev)
```bash
# terminal 1 - API at http://localhost:5004 (Swagger: /swagger)
cd Backend && dotnet run --launch-profile http

# terminal 2 - UI at http://localhost:5173 (proxies /api -> backend)
cd Frontend && npm install && npm run dev
```

## End-to-end check
`python scripts/e2e/main_flow.py ...` runs the main flows (Premium agency, Economy freelancer, payout) over HTTP against a local backend and SQL Server: see [scripts/e2e/README.md](scripts/e2e/README.md). Apply the migrations first (`cd Backend && dotnet ef database update`): the backend does not migrate on start.

## Rules
- Never commit secrets (`.env`, API keys). Use `dotnet user-secrets` / env vars; see `Frontend/.env.example`.
- AI agents: read [AGENTS.md](AGENTS.md) first.

## Pre-commit hooks (Husky)
Husky is installed once at the repo root (`npm install` at root runs `prepare` and sets `core.hooksPath`). Git allows only one hooks dir, so `.husky/pre-commit` dispatches to per-folder hooks, run only when that folder has staged changes:
- `Frontend/.husky/pre-commit`: oxlint on staged files + `tsc -b`.
- `Backend/.husky/pre-commit`: `dotnet format whitespace --verify-no-changes` on staged `.cs` + `dotnet build`.

Setup after clone: `npm install` (root). Skip in emergencies: `git commit --no-verify`.

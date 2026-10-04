# AGENTS.md

Instructions for AI coding agents (Codex, Claude Code, Antigravity, etc.).

- Read [ARCHITECTURE.md](ARCHITECTURE.md) first: layering, request flow, conventions.
- Follow Clean Architecture dependency rule (Domain ← Application ← Infrastructure/WebAPI).
- Build/test: `dotnet build` and `dotnet test` (.NET 10).
- NEVER commit secrets (API keys, tokens, connection strings, `.env`). Use user-secrets or env vars.
- Keep controllers thin; business logic goes in Application handlers / Domain.

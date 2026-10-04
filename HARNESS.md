# HARNESS.md — Simplified Agent Harness (DESIGN PROPOSAL, not implemented)

Goal: agents (Claude / Antigravity / Codex) deliver code **without conflicts, hallucination, code creep, or editing areas they are not allowed to touch**.
Principle: **fewer layers, every rule enforced by a script, not by trust.**

## 1. Simplification: 4 layers → 3 + 1 gate

| Original | Simplified | Why |
|---|---|---|
| 1. Prompt (AGENTS/SOUL/IDENTITY, Socratic Q&A) | **AGENTS.md only** (root + per folder). Drop SOUL/IDENTITY. Socratic Q&A = one rule: "ask when ambiguous" | Persona files add tokens, not safety |
| 2. Context (BRD, PRD, DESIGN, CodeGraph) | **`docs/PRD.md`** (what + why + KPI merged) + **`ARCHITECTURE.md`** per folder; `DESIGN.md` only when Frontend UI tokens exist. CodeGraph = optional | One source of truth per concern; BRD/PRD split is redundant for a small team |
| 3. Loop (5 stages, tracer bullets, blocked-by) | **3 stages: Spec → Tickets → Implement** with human checkpoint after Spec and Tickets. State in `.spec/tasks.md` | "Grilling" merges into Spec; AGY stage = Implement |
| 4. Harness (verify, gates, hooks) | **Gate layer**: `harness/verify` + scope guard + command gate + Husky | The only layer that *enforces* |

```
 Human ── approves ──▶ [Spec] ─▶ [Tickets] ─▶ [Implement (1 ticket / 1 agent)] ─▶ [Gate: verify] ─▶ PR
                         .spec/spec.md   .spec/tasks.md         scope-limited              harness/verify
```

## 2. Anti-hallucination / anti-creep rules (in AGENTS.md)
1. **No spec, no code.** Implement only tickets in `.spec/tasks.md` with status `ready`.
2. **Scope = allowed paths of the ticket.** Touching any other file is a violation (see §4).
3. **Don't invent**: never add packages, endpoints, entities, env vars or files not named in the ticket/spec. If missing → stop and ask (write question into the ticket under `## Questions`).
4. **No drive-by refactors**: no renaming, reformatting or "cleanup" outside the ticket diff.
5. **Verify symbols exist** (grep/CodeGraph) before calling any function, API or component.
6. **One ticket = one branch = one agent** to avoid merge conflicts.
7. Never touch secrets, `.env`, CI, hooks or `harness/` (protected paths).

## 3. `.spec/` — state memory

```
.spec/
  spec.md       what we build (approved by human)
  tasks.md      ticket list + status + scope
```

`tasks.md` ticket format (machine-readable front matter per ticket):
```md
## T-003 Create user endpoint
- status: ready | in-progress | review | done | blocked
- area: backend            # backend | frontend | shared
- blocked-by: T-001
- allowed:
  - Backend/Application/Features/Users/**
  - Backend/WebAPI/Controllers/UserController.cs
  - Backend/Tests/**
- acceptance:
  - POST /api/user returns 201 with ApiResponse<UserDto>
  - unit test for handler passes
- slice: vertical (controller → handler → repo)   # tracer bullet
```
- **Vertical slice first** (tracer bullet): the first ticket of a feature touches every layer thinly end-to-end; later tickets widen it.
- `blocked-by` forms a DAG; an agent may start only tickets whose blockers are `done`.
- Agent updates only the `status` line of its own ticket.

## 4. Gate layer (the enforcement)

```
harness/
  verify.sh / verify.ps1     same logic, 2 shells (Windows dev, Linux CI)
  scope-check.sh / .ps1      staged files ⊆ ticket.allowed ∪ {.spec/tasks.md status line}
  guard-command.*            deny-list for agent shell: rm -rf, git push --force, git reset --hard, dotnet ef database drop, DROP TABLE, curl|sh, writing outside repo
  protected-paths.txt        .env*, .github/**, .husky/**, harness/**, **/appsettings*.json (secrets), Backend/Domain/** unless ticket allows
```

### `verify` = 3 levels, fail-fast
| Level | Check | Backend | Frontend |
|---|---|---|---|
| L1 fast (pre-commit, <10s) | format + lint + scope | `dotnet format --verify-no-changes` | `oxlint` |
| L2 (pre-push) | compile + type-check | `dotnet build` | `tsc -b` |
| L3 (CI + before ticket → review) | tests + secret scan + architecture rules | `dotnet test`, dependency rule (Domain must not reference Infrastructure) | `npm run build`, no `VITE_*SECRET*` |

Integration with existing setup: Husky `pre-commit` calls `harness/verify L1` (reusing current per-folder hooks); `pre-push` calls L2; GitHub Actions calls L3. Nothing new to learn.

### Auto-format feedback + self-correction loop
- After an agent edit: run formatter automatically (hook), so format never causes review noise.
- On verify failure, output is **short and actionable** (`file:line rule → fix hint`), agent gets max **3 retries**, then must write `status: blocked` + reason to the ticket and stop (no infinite loops, no hacks like disabling lint).
- Rate-limit resilience: ticket state lives in `.spec/tasks.md` + git branch, so any agent can resume from the file; no progress is held only in chat.

## 5. Conflict avoidance
- Folder ownership: `Backend/**` and `Frontend/**` tickets never overlap; `shared` tickets (API contract, DTO shape) run **alone** and first.
- API contract single source: `Backend` publishes OpenAPI (`/swagger/v1/swagger.json`); Frontend types derive from it — Frontend never guesses fields.
- `CODEOWNERS` + required CI check `verify-L3` on `main` as last line of defense.

## 6. Human checkpoints (only 3)
1. Approve `spec.md`.
2. Approve `tasks.md` (scopes + dependencies).
3. Review PR (verify already green).

## 7. Implementation plan (if approved)
1. `AGENTS.md` rules (root + `Backend/` + `Frontend/`) with the §2 rules and protected paths.
2. `.spec/spec.md`, `.spec/tasks.md` templates.
3. `harness/verify.{sh,ps1}`, `scope-check.{sh,ps1}`, `protected-paths.txt`.
4. Wire into Husky (`pre-commit`, `pre-push`) and `.github/workflows` (L3).
5. Command deny-list as agent-tool config (`.claude/settings.json` permissions, Antigravity/Codex equivalents).

## 8. Decisions (approved)
- No CodeGraph (grep + evidence).
- Tickets = GitHub Issues (source of truth), mirrored to `.spec/tasks.md` by `node harness/sync-issues.mjs`.
- `scope-check` hard-fails; widening scope only via a GitHub Issue (template 'Scope exception').
- Evidence rule: every report/claim must cite command+output, `file:line` or `.harness/evidence/*.log`; no evidence = not done = fabrication.

## 9. Status
Implemented: `harness/` (verify, scope-check, guard-command, protected-paths, sync-issues, run.ps1), Husky pre-commit/pre-push, CI (scope + verify L3), issue/PR templates, `.claude/settings.json` command gate.

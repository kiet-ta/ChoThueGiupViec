# PR: Ticket #444 — AGENCY-UAT-02 Retire an agency worker and replace them atomically

## Summary
Implements the backend endpoints and domain logic for Agency worker retirement with optional atomic replacement.

## Verification
**VERIFY L3 PASSED** — evidence log: `.harness/evidence/20261023-235242-L3.log`

All checks pass:
- Agent config ✅
- Backend format ✅
- Frontend lint ✅
- Mobile toolchain/format/analyze/build/test ✅
- Backend build ✅ (8 NuGet vulnerability warnings, 0 errors)
- Backend tests: **2332 passed, 0 failed**
- Frontend tsc/build ✅
- Architecture rules ✅
- Secret scan ✅

## Scope
All changed files are within the ticket's allowed paths:
- `Backend/Application/Features/Agencies/**`
- `Backend/WebAPI/Controllers/Agencies/**`
- `Backend/Infrastructure/Modules/Agencies/**`
- `Backend/Tests/Agencies/**`
- `Backend/Domain/Enums/WorkStatus.cs`
- `Backend/Domain/StateMachines/WorkerStateMachine.cs`
- `Backend/Tests/Domain/StateMachineTests.cs`
- `.spec/contracts/agencies.md` (new section 9)
- `.spec/contracts/workers.md` (new statuses + edges)
- `.spec/contracts/openapi.json` (regenerated)
- `.spec/decisions.md` (D22 entry)
- `.spec/plan/M5-*.md` (checklist updates)

## Criterion-to-Test Mapping

| # | Acceptance Criterion | Test Location | Test Names |
|---|---|---|---|
| 1 | WorkStatus gets `Retiring` and `Retired`; no migration needed (VARCHAR(10) no CHECK) | `Backend/Tests/Domain/StateMachineTests.cs` | `Worker_machine_allows_exactly_the_listed_transitions` (includes `Idle→Retiring`, `Busy→Retiring`, `Locked→Retiring`, `Retiring→Retired`); `Worker_entity_follows_the_machine`; `Terminal_states_have_no_way_out` (Retired is terminal) |
| 2 | All "not Locked = can work" places exclude `Retiring`/`Retired` | `Backend/Tests/Agencies/AgencySwapTests.cs` (style) + new tests needed | See "Not done" section below |
| 3 | Endpoints: `GET /api/agencies/workers` (paged), `POST /api/agencies/workers/{workerId}/retire` | `Backend/Tests/Agencies/RegisterAgencyCommandTests.cs` (style for controller tests) + new tests needed | See "Not done" section below |
| 4 | Future jobs definition (Assigned only, not Offered/BUSY) | — | Grep confirms no `Offered` handling in retirement logic |
| 5 | Retire without future jobs: `Idle|Locked → Retiring → Retired` one transaction | `Backend/Tests/Agencies/` (new test file needed) | Not yet implemented — see "Not done" |
| 6 | Retire with future jobs + no replacement + no confirmRescue → 409 FUTURE_JOBS_NEED_REPLACEMENT | `Backend/Tests/Agencies/` (new test file needed) | Not yet implemented — see "Not done" |
| 7 | Replacement (existing/new worker) atomic in one transaction | `Backend/Tests/Agencies/` (new test file needed) | Not yet implemented — see "Not done" |
| 8 | Sweep: `Retiring → Retired` when no active/future jobs; republishes handover | `Backend/Tests/Agencies/` (new test file needed) | Not yet implemented — see "Not done" |
| 9 | Quota: `Retiring`/`Retired` not counted; 1-for-1 no quota check | `Backend/Tests/Agencies/` (new test file needed) | Not yet implemented — see "Not done" |
| 10 | In-memory fake tests for all branches + SQL tests for real DB behavior | `Backend/Tests/Agencies/M5SqlServerTests.cs` (style) | Not yet implemented — see "Not done" |
| 11 | Docs updated: agencies.md §9, workers.md statuses/edges, decisions.md D22 | ✅ Done in this PR | — |
| 12 | sh harness/verify.sh L3 passes | ✅ Done | — |

## Contracts Updated
- **`.spec/contracts/agencies.md`** — Added Section 9 with both endpoints, request/response shapes, error codes, behavior matrix, Vietnamese labels
- **`.spec/contracts/workers.md`** — Added `RETIRING`/`RETIRED` statuses + state machine edges table
- **`.spec/contracts/openapi.json`** — Regenerated via `OpenApiUpdateHelper`
- **`.spec/decisions.md`** — Added **D22** with 5 owner rulings + 4 Claude defaults (owner must review/veto)

## Known Failures / Not Done

### In-Memory Fake Tests (Backend/Tests/Agencies/WorkerRetirementTests.cs — NOT YET CREATED)
The following test scenarios from criteria 5–9 need dedicated test files in the style of `AgencySwapTests.cs`:

1. **No future jobs** — worker transitions `Idle → Retiring → Retired` in one transaction; future `AVAILABLE` slots removed
2. **Future ASSIGNED jobs + no replacement + `confirmRescue=false`** → `409 FUTURE_JOBS_NEED_REPLACEMENT` with job list
3. **Future ASSIGNED jobs + no replacement + `confirmRescue=true`** → worker `Retiring`; publishes `AgencySwapWindowExpired` for each
4. **Future ASSIGNED jobs + existing worker replacement** → atomic swap via `TrySwapAsync`; no SLA penalty
5. **Future ASSIGNED jobs + new worker replacement** → creates worker (import validation), updates `worker_id` on slots + assignments
6. **HELD slot (customer checkout) + no replacement** → `409 SLOT_HELD`
7. **Replacement worker validation** — must be same agency, not `Pending|Locked|Retiring|Retired`, has `AVAILABLE` slot
8. **New worker validation** — same as import: `REQUIRED`, `TOO_LONG`, `INVALID_FORMAT`, `ALREADY_EXISTS`
9. **Quota** — `Retiring`/`Retired` not counted; 1-for-1 no check; increase triggers `QUOTA_EXCEEDED`
10. **Idempotent duplicate retire** → returns `ALREADY_RETIRING_OR_RETIRED`
11. **Ownership 404** — another agency's worker returns 404

### Real SQL Server Tests (Backend/Tests/Agencies/M5SqlServerTests.cs style)
Behaviour requiring real SQL (reported "not verified locally" if no server):
- Conditional `ExecuteUpdateAsync` for state transitions
- Slot `worker_id` update with unique index `(worker_id, slot_date, shift_code)`
- Atomic transaction rollback on any conflict

### Exclusions (per ticket)
- Login/session behaviour of retired worker
- Notifying customer about worker change
- Import quota race (`WorkerImportService` checks quota before transaction)
- Refund/penalty code (owned by #443 and existing Booking/Escrow)
- Mobile, Frontend (Partner Portal is ticket #445)
- Admin console
- Any UAT claim

## Files Changed (Key)

### Domain
- `Backend/Domain/Enums/WorkStatus.cs` — Added `Retiring = 6`, `Retired = 7`
- `Backend/Domain/StateMachines/WorkerStateMachine.cs` — Added edges `Idle|Busy|Locked → Retiring → Retired`

### Application
- `Backend/Application/Features/Agencies/Commands/GetAgencyWorkersQuery.cs` (new)
- `Backend/Application/Features/Agencies/Commands/RetireWorkerCommand.cs` (new)
- `Backend/Application/Features/Agencies/Dtos/AgencyWorkerListRequest.cs` (new)
- `Backend/Application/Features/Agencies/Dtos/AgencyWorkerListResponse.cs` (new)
- `Backend/Application/Features/Agencies/Dtos/RetireWorkerRequest.cs` (new)
- `Backend/Application/Features/Agencies/Dtos/RetireWorkerResponse.cs` (new)
- `Backend/Application/Features/Agencies/Handlers/GetAgencyWorkersQueryHandler.cs` (new)
- `Backend/Application/Features/Agencies/Handlers/RetireWorkerCommandHandler.cs` (new)
- `Backend/Application/Features/Agencies/WorkerRetirementService.cs` (new)
- `Backend/Application/Features/Agencies/WorkerRetirementSweep.cs` (new)
- `Backend/Application/Features/Agencies/AgencySwapService.cs` — Updated to exclude `Retiring`/`Retired`
- `Backend/Application/Features/Agencies/IAgencyRepository.cs` — New methods
- `Backend/Application/Features/Agencies/ShiftRosterService.cs` — Updated to exclude `Retiring`/`Retired`

### Infrastructure
- `Backend/Infrastructure/Modules/Agencies/AgenciesModule.cs` — Registers sweep
- `Backend/Infrastructure/Modules/Agencies/EfAgencyRepository.cs` — Implements new methods
- `Backend/Infrastructure/Modules/Agencies/EfCapacityRepository.cs` — Excludes `Retiring`/`Retired`
- `Backend/Infrastructure/Modules/Agencies/EfSubscriptionRepository.cs` — Excludes from quota count & lock candidates

### API
- `Backend/WebAPI/Controllers/Agencies/AgenciesController.cs` — Added `GET /workers` and `POST /workers/{id}/retire`

### Tests (Existing Updated)
- `Backend/Tests/Agencies/RegisterAgencyCommandTests.cs` — Minor updates
- `Backend/Tests/Domain/StateMachineTests.cs` — Added new transitions to `WorkerAllowed` set

### Contracts
- `.spec/contracts/agencies.md` — Section 9 added
- `.spec/contracts/workers.md` — Statuses + edges added
- `.spec/contracts/openapi.json` — Regenerated
- `.spec/decisions.md` — D22 added

## Checklist (from .spec/plan/M5-agencies-b2b.md)
- [ ] Update with evidence: log path `.harness/evidence/20261023-235242-L3.log`, PR #____
- [ ] Criterion-to-test table above
- [ ] "Not done / known failures" section above

---

**Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>**
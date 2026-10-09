# Tasks (generated from GitHub Issues - DO NOT EDIT BY HAND)

Regenerate: `node harness/sync-issues.mjs`

## #1 [ticket] BASE-01 Skeleton & project test (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-01. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Module folder convention exists for these 12 modules: Identity, Customers, Booking, Payments, Dispatch, Workers, Agencies, Skills, Ratings, Disputes, Payouts, Admin. Per module: Application/Features/<Module>, WebAPI/Controllers/<Module>, Infrastructure/Modules/<Module>, Tests/<Module>.
  - An xUnit test project exists under Backend/Tests, is excluded from CommonService.csproj compilation, is added to CommonService.sln, and dotnet test runs one sample test that PASSES (evidence: .harness/evidence/*.log path and the test name).
  - Backend/ARCHITECTURE.md documents the module folder convention and the frozen shared files (overview section 3, rule 2).
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - No new package outside the xUnit test project packages, no endpoint, no entity (those belong to later tickets).
  - Tick BASE-01 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.

## #3 [ticket] HARNESS-01 start-ticket script and generated tasks.md
- status: done
- area: shared
- blocked-by: -
- allowed:
  - harness/**
  - .github/**
  - .gitignore
  - .spec/tasks.md
  - AGENTS.md
  - HARNESS.md
- acceptance:
  - node harness/start-ticket.mjs <issue#> validates the issue (open, label ticket, not blocked/review, every "Blocked by" issue closed), refuses to switch with uncommitted tracked changes, creates ticket/<issue#>-<slug> from a freshly fetched origin/main (or resumes the existing branch of that ticket), sets label in-progress (removes ready), assigns the caller, regenerates .spec/tasks.md and runs scope-check as proof. Evidence: command output for the create path, the resume path, --dry-run, and each refusal (closed issue, missing issue, uncommitted changes).
  - ./harness/run.ps1 start-ticket <issue#> works on Windows.
  - .spec/tasks.md is a GENERATED, untracked file (added to .gitignore, removed from the index): no merge conflicts between the 6 members.
  - CI scope job regenerates .spec/tasks.md from GitHub Issues (source of truth) before scope-check, so a PR cannot widen its own scope by editing its copy. Evidence: green scope check on this PR, and the workflow diff (permissions: issues: read, GH_TOKEN).
  - AGENTS.md and HARNESS.md tell agents to start tickets only with start-ticket.mjs (no hand-made branches) and that tasks.md is generated (node harness/sync-issues.mjs).
  - sh harness/verify.sh L3 PASSES; paste the last line and log path in the PR.
  - Out of scope: GitHub branch protection settings (leader does it in Settings), any Backend/Frontend/Mobile code.

## #6 [ticket] BASE-02 Module auto-registration (IModule) (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #1
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-02. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - An IModule abstraction scans the assembly: controllers, MediatR handlers, validators (whatever ValidationBehavior discovers today), EF configurations and DI registrations of a module are loaded automatically.
  - After this ticket Program.cs, Application/DependencyInjection.cs and Infrastructure/DependencyInjection.cs do not need to be edited to add a module (they are frozen after gate G0).
  - Test: a fake module added in the test project, WITHOUT editing Program.cs, resolves one of its services from the container (evidence: test name + log).
  - Document the mechanism in Backend/ARCHITECTURE.md.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-02 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #7 [ticket] BASE-03 Cross-module ports, DTOs and Fakes (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #6
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-03. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Every port of overview section 4 exists as an interface + DTOs in Application/Interfaces (inward dependency rule): IPaymentGateway, IRefundService, IAgencyCapacityService, ISlaPenaltyService, IWorkerAvailabilityQuery, IWorkerReputation, INotificationService, IOtpSender, IAuditLog, IPasswordHasher, IEkycProvider, IImageQualityService, IFileStorage, IClock, ICurrentUser, IGeoService, plus IWorkerProfileQuery (decisions Q21 C3: worker fullName, ratingAvg, completedJobs, workStatus and an existence check; implementer M4, Fake first; consumer: the favorite-workers list of Customers). Names are proposals: M1 finalizes them here.
  - Every port has an in-memory Fake registered with TryAdd AFTER the modules, so a real implementation registered by a module wins. Each port has at least one test (evidence: test names + log).
  - A port table (port, implementer, consumers) is added to Backend/ARCHITECTURE.md.
  - Fake IOtpSender must refuse to run outside Development (decisions Q06) - test included.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-03 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #8 [ticket] BASE-04 Domain event catalog and dispatcher (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #6
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-04. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - All events of overview section 5 exist as MediatR notifications, following the convention of the existing OrderCreatedEvent: OrderPaid, OrderCancelled, OrderRefunded, JobAssigned, AssignmentFailed, WorkerCheckedIn, CustomerAbsentReported, CustomerAbsentApproved, IncidentReported, JobCompleted, ExtensionPaid, ExtensionDeclined, SubscriptionActivated, DisputeResolved, RatingSubmitted, PayoutBatchClosed.
  - Test: cross-module publish/subscribe with two dummy handlers living in different module folders (evidence: test name + log).
  - Event names and payload fields are documented in Backend/ARCHITECTURE.md.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-04 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #9 [ticket] BASE-05 BusinessRules options and configuration placeholders (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #6, #23
- allowed:
  - Backend/**
  - .spec/plan/**
  - Backend/appsettings*.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-05. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - One options class BusinessRules holds every key of .spec/decisions.md section 4 (including the keys added by ticket DECISIONS-01: Auth.AccessTokenMinutes, Auth.RefreshTokenDays, Auth.RegistrationTokenMinutes, Customer.InitialTrustScore, Address.RoomMaxAreaM2, Address.HouseMaxFloors) with exactly those DEFAULT values (no value invented), bound from configuration.
  - Config placeholders (NO secrets) exist for MoMo sandbox, OTP, eKYC, file storage in Backend/appsettings.json. Secrets are read from user-secrets or environment variables only (G-6).
  - Test: the bound defaults equal the table in decisions section 4 for every key (evidence: test name + log).
  - Literal allowed path Backend/appsettings*.json is listed because this ticket touches configuration.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-05 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #10 [ticket] BASE-06 Domain model: 27 tables and state machines (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #1, #23
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-06. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Entities (partial), enums and value objects built from Backend/GiupViec_Physical_DB_MVP5.drawio (22 tables) plus SC-1..SC-8 of decisions section 3 (5 new tables PRICE_RULE, ADMIN_AUDIT_LOG, ESCROW_TRANSACTION, OTP_CODE, REFRESH_TOKEN; new PARTNER_AGENCY columns; lockout columns on ADMIN and PARTNER_AGENCY; UNIQUE on TWO_WAY_RATING). Reuse Money / Address.
  - Worker is single-table inheritance (worker_type FREELANCER|AGENCY_STAFF, agency_id null for freelancers). JobAssignment is the flat shared node (order_id, customer_id, worker_id, agency_id, slot_id, service_tier, payout_amount).
  - State machines for JobOrder, JobAssignment (OFFERED, ASSIGNED, CHECKED_IN, IN_PROGRESS, AWAITING_ACCEPTANCE, COMPLETED, CANCELLED, ABSENT, INCIDENT, plus a distinct "cancelled by worker" status per decisions Q15) and Worker. Unit tests cover every valid and every invalid transition (evidence: test names + log).
  - The schema delta is recorded in Backend/ARCHITECTURE.md (or the drawio is updated).
  - Also correct .spec/plan/M1-platform-identity.md BASE-06 text that still says "25 bảng" / "SC-1..SC-5".
  - Domain model only: no database access in this ticket. Persistence is BASE-07, done later on a Windows machine with SQL Server (decision D1 unchanged).
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-06 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #11 [ticket] BASE-07 Persistence with EF Core on SQL Server (Windows) (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #10, #27
- allowed:
  - Backend/**
  - .spec/plan/**
  - Backend/appsettings*.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-07. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - MUST BE DONE ON A WINDOWS MACHINE WITH SQL SERVER (owner decision 2026-10-04: no SQLite, no other provider). The ticket carries the label blocked until the owner confirms the Windows/SQL Server environment is ready; remove the label then.
  - EF Core AppDbContext for SQL Server (decision D1) with one IEntityTypeConfiguration per entity, applied automatically. Packages named by this ticket: Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Design (versions matching net10.0). No other database provider.
  - Connection string placeholder only in Backend/appsettings*.json (no secrets); real value via dotnet user-secrets or environment variable.
  - Configurations: money columns explicitly DECIMAL(18,2), timestamps DATETIME2 (UTC), text NVARCHAR, per the drawio and decisions G-2/G-3.
  - UNIQUE(worker_id, slot_date, shift_code) on BOOKING_SLOT; indexes on worker_id, agency_id, customer_id, order_id of JOB_ASSIGNMENT.
  - First migration created and applied: dotnet ef database update output (evidence: command + log). The ModelSnapshot is committed; migration conflicts are resolved by recreating, never by hand-merging.
  - Schema test against SQL Server: all 27 tables exist and the UNIQUE constraint rejects a duplicate slot (evidence: test names + log).
  - Decisions Q22 / SC-9 (review of BASE-06): JOB_ASSIGNMENT.accepted_at nullable; filtered unique index JOB_ASSIGNMENT(slot_id) WHERE assignment_status NOT IN ('CANCELLED','CANCELLED_BY_WORKER','REASSIGNED'); entity AdminAccount maps to table ADMIN. Test: a second active assignment on the same slot is rejected, and a slot whose assignment is CANCELLED_BY_WORKER can be assigned again.
  - Enum columns use value converters built on Domain/Enums/DbEnum (UPPER_SNAKE_CASE in the database).
  - Every DateTime is stored and read as UTC (decisions G-3): a converter sets DateTimeKind.Utc on read and rejects a non-UTC value on write. Test included.
  - Database CHECK constraints from the drawio legend: Worker STI (worker_type vs agency_id), PAYMENT_TRANSACTION (exactly one of order/extension/subscription per purpose), PAYOUT_ITEM (payee_type vs worker/agency). Test: each rejects an invalid row.
  - Local database instructions are added to Backend/ARCHITECTURE.md.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-07 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).
  - Literal allowed path Backend/appsettings*.json (connection string placeholder).

## #12 [ticket] BASE-07b SQL Server provider and first migration (Windows) (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #11
- allowed:
  - Backend/**
  - .spec/plan/**
  - Backend/appsettings*.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-07b. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Run on a machine with SQL Server (Windows): set Database:Provider = SqlServer, generate the first EF Core migration and apply it with dotnet ef database update (evidence: command output + log).
  - The ModelSnapshot is generated for SQL Server only. Re-run the persistence tests and the integration tests against SQL Server and report any difference from SQLite (money SUM/ORDER BY, concurrency). State clearly what was and was not verified.
  - Update Backend/ARCHITECTURE.md (remove the "SQLite temporary" note once SQL Server is the verified default).
  - This ticket completes the SQL Server part of gate G1 (owner decision 2026-10-04). Plan reference note: it replaces the "first migration" step of BASE-07.
  - Literal allowed path Backend/appsettings*.json.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #13 [ticket] BASE-08 Repository and UnitOfWork conventions (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #11
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-08. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - A repository convention per aggregate plus IUnitOfWork; modules add repositories only inside their own folder.
  - Tests against the SQL Server setup of BASE-07 show one repository and the unit of work committing atomically (evidence: test names + log).
  - Convention documented in Backend/ARCHITECTURE.md.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-08 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #14 [ticket] BASE-09 Remove template demo code (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #13
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-09. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Remove the demo User, Order, OrderItem, BankAccount, ShippingCalculator, PaymentIntent code, the template value objects Money and Address (decisions Q22 D5: money rounding is Vnd) (including Application/Features/Users) and their tests.
  - Evidence: grep -rn for each removed symbol across Backend/ returns nothing (paste the output), and the build and tests still pass.
  - Update the "Known gaps" and folder tables in Backend/ARCHITECTURE.md accordingly.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-09 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #15 [ticket] BASE-10 Seed data (dev Admin, prices, packages) (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #11, #7
- allowed:
  - Backend/**
  - .spec/plan/**
  - Backend/appsettings*.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-10. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - AdminSeeder registered through the module mechanism (no Program.cs edit). It runs only in Development: an empty database gets exactly one ADMIN (Seed:Admin:Email|Password|FullName, dev defaults in Backend/appsettings.Development.json per overview section 10); a second run creates no duplicate and does not change the password; Production never runs it. The password is hashed with IPasswordHasher and never logged.
  - The real IPasswordHasher implementation: if a package is needed, name it in a comment on this issue and wait for the leader before adding it.
  - Seed in ALL environments: the 6 PRICE_RULE rows (decisions Q01) and the 3 packages FREE / PRO_MONTHLY / PRO_QUARTERLY (decisions Q08), exactly the values in .spec/decisions.md, plus sample Skills.
  - Evidence of overview section 10: (1) empty database -> exactly 1 ADMIN row; (2) second run -> still 1; (3) ASPNETCORE_ENVIRONMENT=Production -> 0 rows. Paste the test log. Run against SQL Server (this ticket is done after BASE-07, on the Windows machine).
  - Literal allowed path Backend/appsettings*.json.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-10 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #16 [ticket] BASE-11 Shared infrastructure (clock, current user, geo, storage, helpers) (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #7
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-11. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Real implementations of IClock (the single place that converts UTC <-> Asia/Ho_Chi_Minh, decisions G-3), ICurrentUser (reads the HTTP user claims), IGeoService (GPS distance in meters) and IFileStorage (local disk in development).
  - Helpers: pagination, validation, ProblemDetails mapping and an idempotency helper (double-click, webhook replay). Each has a test (evidence: test names + log).
  - Real implementations replace the Fakes through TryAdd ordering of BASE-03.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-11 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #17 [ticket] BASE-12 Realtime and notification channel (SignalR) (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #7
- allowed:
  - Backend/**
  - .spec/plan/**
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-12. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - SignalR hub and the real INotificationService (decisions Q07): customer payment status and job tracking first. Polling fallback endpoint contract is documented. FCM push is DEFERRED (Q07b): do NOT implement it.
  - Test: publishing a notification reaches a subscribed test client/handler (evidence: test name + log).
  - No new package (SignalR is part of the ASP.NET Core shared framework).
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-12 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #18 [ticket] BASE-13 Stable OpenAPI snapshot (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #6
- allowed:
  - Backend/**
  - .spec/plan/**
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-13. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Swagger with consistent operationId, bearer security scheme, response envelope ApiResponse<T>.
  - Snapshot exported to .spec/contracts/openapi.json and a test fails when the live document differs from the snapshot (evidence: test name + log).
  - Gate G4 itself opens only when the Web/Mobile clients are generated from this snapshot and run; this ticket provides the snapshot.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BASE-13 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #19 [ticket] BASE-14 (G0) Announce gate G0 (Skeleton) (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #1, #6, #7, #8, #9
- allowed:
  - .spec/plan/00-overview.md
  - .spec/plan/M1-platform-identity.md
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-14 (G0). Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Tick G0 in .spec/plan/00-overview.md with evidence (links to the merged PRs of BASE-01..05) and tick BASE-14 (G0) in the M1 checklist.
  - Post the gate announcement as a comment on this issue so the team knows M2..M6 may code against ports/fakes.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #20 [ticket] BASE-14 (G1) Announce gate G1 (Data) (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #10, #11, #13
- allowed:
  - .spec/plan/00-overview.md
  - .spec/plan/M1-platform-identity.md
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BASE-14 (G1). Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Tick G1 in .spec/plan/00-overview.md with evidence (merged PRs of BASE-06..08). The evidence must include the first SQL Server migration applied (BASE-07).
  - Post the gate announcement as a comment on this issue.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #21 [ticket] BE-M1-00 Contract Identity + Customers (M1 - Kiệt)
- status: done
- area: shared
- blocked-by: -
- allowed:
  - .spec/contracts/identity.md
  - .spec/contracts/customers.md
  - .spec/plan/M1-platform-identity.md
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BE-M1-00. Read .spec/decisions.md and .spec/plan/00-overview.md (sections 3 and 4) first.
  - Write .spec/contracts/identity.md and .spec/contracts/customers.md: endpoints, request/response DTOs, error cases and roles for BE-M1-01..06, using the ApiResponse<T> envelope. Follow decisions Q06 (OTP), Q16 (auth), Q17 (call/phone privacy where it touches identity) and the PRD sections 1.1 / 1.3 / 4.1.
  - Do not invent endpoints outside what BE-M1-01..06 need; anything unclear is a question to the leader in this issue (decisions G-7).
  - Acceptance of this ticket is the leader approval of the contracts (opens gate G3 for M1).
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BE-M1-00 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #23 [ticket] DECISIONS-01 Record identity and customer decisions (M1 - Kiệt)
- status: done
- area: shared
- blocked-by: -
- allowed:
  - .spec/decisions.md
  - .spec/plan/00-overview.md
- acceptance:
  - Source of the decisions: the leader's answers of 2026-10-04 to the open questions of the contracts .spec/contracts/identity.md (O1-O6) and .spec/contracts/customers.md (C1-C5). Documents only: no code.
  - .spec/decisions.md section 3 gets SC-6 OTP_CODE, SC-7 REFRESH_TOKEN and SC-8 lockout columns on ADMIN and PARTNER_AGENCY, with the exact columns written in identity.md section 3; the table count becomes 27 (25 + OTP_CODE + REFRESH_TOKEN); owner M1 for both new tables.
  - .spec/decisions.md gets a decision entry for Identity (O1 new worker must complete a profile via a registration token; O2 token lifetimes; O3 OTP/refresh/lockout state in the database; O4 phone normalization; O5 validation error shape; O6 account states) and for Customers (C1-C5).
  - .spec/decisions.md section 4 gets the new configuration keys with their DEFAULT: Auth.AccessTokenMinutes 15, Auth.RefreshTokenDays 30, Auth.RegistrationTokenMinutes 30 (suggested, not leader-decided), Customer.InitialTrustScore 0.00 (provisional), Address.RoomMaxAreaM2 30, Address.HouseMaxFloors 10 (suggested, not in the PRD). Every row states its source and whether it is a leader decision or a suggestion.
  - .spec/plan/00-overview.md: section 4 gets the port IWorkerProfileQuery (implementer M4, consumer M1); sections 2 (gate G1 "25 bảng"), 3 and 6 are updated to 27 tables with the owner of the new tables; section 8 lists the new decisions as resolved.
  - No existing value in decisions.md is changed; mark the additions clearly. If something contradicts the PRD (spec.md), stop and ask in this issue.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #27 [ticket] DECISIONS-02 Record review decisions D1-D7 (M1 - Kiệt)
- status: done
- area: shared
- blocked-by: -
- allowed:
  - .spec/decisions.md
  - .spec/plan/00-overview.md
- acceptance:
  - Source: the leader's answers of 2026-10-04 in the review of BASE-02/BASE-06 (decisions D1-D7, applied in PR #26). Documents only: no code.
  - .spec/decisions.md records D1-D7 as a new entry: D1 offers persisted (OFFERED), JOB_ASSIGNMENT.accepted_at nullable = new SC-9; D2 the unique index Job_Assignment(slot_id) excludes CANCELLED, CANCELLED_BY_WORKER, REASSIGNED; D3 fault_party = FREELANCER | AGENCY | CUSTOMER, null = nobody at fault; D4 the ADMIN table is the entity AdminAccount; D5 single VND rounding helper Vnd, template Money/Address deleted by BASE-09; D6 BUSY = on site (check-in until completed / absence approved), future assignments do not change work_status; D7 order status derived from its assignments (ASSIGNED when enough accepted, COMPLETED when all required completed, back to DISPATCHING when a seat is lost).
  - .spec/decisions.md Q10 and Q12: replace fault_party = WORKER with the D3 wording (FREELANCER or AGENCY) without changing their meaning.
  - .spec/plan/00-overview.md section 6: the JOB_ASSIGNMENT state list includes CANCELLED_BY_WORKER and REASSIGNED; section 8 lists the new entry as resolved.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5).

## #41 [ticket] BE-M1-01 Customer login via phone and OTP (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Identity/**
  - Backend/WebAPI/Controllers/Identity/**
  - Backend/Infrastructure/Modules/Identity/**
  - Backend/Tests/Identity/**
  - Backend/Domain/Entities/*.Identity.cs
  - .spec/plan/M1-platform-identity.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BE-M1-01. Read .spec/decisions.md (Q06, Q16, Q20) and contract .spec/contracts/identity.md (§2.1, §2.2).
  - Implement POST /api/auth/otp/request: validates Vietnamese mobile format (0XXXXXXXXX, 10 digits), checks per-phone and per-IP rate limits using OTP_CODE rows (max 5/phone/hour, max 20/IP/hour, 60s cooldown per decisions Q06), generates 6-digit OTP, stores keyed HMAC-SHA256 in OTP_CODE, dispatches via IOtpSender, returns 200 with expiresInSeconds (300) and resendAvailableInSeconds (60). Violations return 429 with Retry-After header.
  - Implement POST /api/auth/otp/verify: verifies 6-digit code against HMAC hash in OTP_CODE, checks TTL (5 min), tracks attempt_count (max 5 attempts before invalidation -> 429). On success, marks consumed; for Customer, creates new CUSTOMER entity if first login (isNewUser = true) or sets otp_verified_at (isNewUser = false).
  - Unit and integration tests in Backend/Tests/Identity/ covering valid/invalid OTP, cooldown, max attempts, expiry, and customer creation.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick BE-M1-01 in .spec/plan/M1-platform-identity.md with evidence: <log path or PR#> in the same PR.

## #43 [ticket] BE-M1-02 JWT access and refresh tokens, roles and policies, real ICurrentUser (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Identity/**
  - Backend/WebAPI/Controllers/Identity/**
  - Backend/Infrastructure/Modules/Identity/**
  - Backend/Tests/Identity/**
  - Backend/Domain/Entities/*.Identity.cs
  - .spec/plan/M1-platform-identity.md
  - .spec/plan/00-overview.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BE-M1-02. Read .spec/contracts/identity.md (§1, §2.4, §2.5, §2.6), .spec/decisions.md (Q16, SC-7), and .spec/plan/00-overview.md §2 (Gate G2).
  - Implement Bearer authentication scheme with token signature validation (claims sub, role, jti, lifetime Auth.AccessTokenMinutes).
  - Implement authorization policies for exactly four roles: CustomerOnly, WorkerOnly, PartnerOnly, AdminOnly.
  - Wire up real ICurrentUser via ClaimsCurrentUser winning over FakeCurrentUser in DI.
  - Implement POST /api/auth/refresh: opaque refresh token with SHA-256 hash in table REFRESH_TOKEN (SC-7), rotation issuing new access and refresh token, reuse detection revoking family chain, 401 on expired/invalid/reused token.
  - Implement POST /api/auth/logout: revokes refresh token, idempotent 200 data: null, requires authenticated caller.
  - Implement GET /api/auth/me: returns id and role from ICurrentUser, requires authenticated caller.
  - Ensure POST /api/auth/otp/verify creates initial REFRESH_TOKEN row with family_id.
  - Unit and integration tests in Backend/Tests/Identity/ covering JWT verification, refresh rotation, reuse revocation, logout, /api/auth/me, and authorization policies.
  - Open Gate G2: update .spec/plan/00-overview.md Gate G2 table row.
  - sh harness/verify.sh L3 PASSES. Tick BE-M1-02 in .spec/plan/M1-platform-identity.md.

## #45 [ticket] BE-M1-03 Worker / Partner / Admin authentication per Q16 (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Identity/**
  - Backend/WebAPI/Controllers/Identity/**
  - Backend/Infrastructure/Modules/Identity/**
  - Backend/Tests/Identity/**
  - .spec/plan/M1-platform-identity.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BE-M1-03. Read .spec/contracts/identity.md (§2.3) and .spec/decisions.md (Q16, SC-1, SC-8).
  - Implement POST /api/auth/password/login for Admin and Partner roles:
  - - Request: email, password, role (Admin | Partner).
  - - Admin lookup via ADMIN.email, verify password via IPasswordHasher, check is_active (403 if inactive).
  - - Partner lookup via PARTNER_AGENCY.contact_email, verify password via IPasswordHasher (can log in even if SUSPENDED).
  - - Lockout policy (Q16): 5 consecutive failures lock account for 15 minutes (locked_until, failed_login_count in SC-8 columns); 423 status code with Retry-After header; correct password during lock still returns 423; successful login resets failed_login_count and locked_until.
  - - Returns AuthResultDto (Bearer token, refreshToken, user { id, role, isNewUser: false }) and persists REFRESH_TOKEN row with family_id.
  - Unit and integration tests in Backend/Tests/Identity/ covering Admin/Partner login, password verification, inactive admin rejection, lockout threshold, lockout expiry, and counter reset.
  - Update .spec/contracts/openapi.json snapshot.
  - sh harness/verify.sh L3 PASSES. Tick BE-M1-03 in .spec/plan/M1-platform-identity.md.

## #47 [ticket] BE-M1-04 Customer profile (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Customers/**
  - Backend/WebAPI/Controllers/Customers/**
  - Backend/Infrastructure/Modules/Customers/**
  - Backend/Tests/Customers/**
  - Backend/Domain/Entities/*.Customers.cs
  - .spec/plan/M1-platform-identity.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BE-M1-04. Read .spec/contracts/customers.md (§1 CustomerProfile, §2.1, §4 C1/C2) and .spec/contracts/identity.md (§1 conventions).
  - Implement GET /api/customers/me (policy CustomerOnly): returns CustomerProfile { customerId, phoneNumber, fullName, email, trustScore, accountStatus, createdAt } for the caller. The id always comes from ICurrentUser, never from the URL or body. otp_verified_at and updated_at are not exposed. 404 only if the row was removed.
  - Implement PUT /api/customers/me (policy CustomerOnly): full replace of { fullName, email }, returns the updated CustomerProfile. 400 (data.errors map per decisions O5) when fullName is empty/whitespace or longer than 100 characters, or when email is present but not a valid address or longer than 255 characters. phoneNumber, trustScore and accountStatus are not editable. updated_at is set by the server using IClock.
  - 401 without a valid Bearer token; 403 for a token whose role is not Customer.
  - Unit and integration tests in Backend/Tests/Customers/ covering: GET returns the caller's own profile and never another customer's, PUT happy path, every 400 rule, email null accepted, phone/trustScore/accountStatus unchanged after PUT, 401 and 403.
  - Update the .spec/contracts/openapi.json snapshot (regenerate with the OpenApiUpdateHelper test; the OpenApiSnapshotTests must pass).
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5). Addresses (BE-M1-05) and favorite workers (BE-M1-06) are separate tickets.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. Tick BE-M1-04 in .spec/plan/M1-platform-identity.md with evidence in the same PR.

## #49 [ticket] BE-M1-05 Customer address book (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: #47
- allowed:
  - Backend/Application/Features/Customers/**
  - Backend/WebAPI/Controllers/Customers/**
  - Backend/Infrastructure/Modules/Customers/**
  - Backend/Tests/Customers/**
  - Backend/Domain/Entities/*.Customers.cs
  - .spec/plan/M1-platform-identity.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BE-M1-05. Read .spec/contracts/customers.md (§1 Address, §2.2, §4 C4/C5), .spec/contracts/identity.md (§1 conventions) and .spec/decisions.md (G-2 rounding, Q21 C4/C5).
  - Blocked by #47 on purpose: both tickets regenerate .spec/contracts/openapi.json and edit Backend/Infrastructure/Modules/Customers/CustomersModule.cs. Start only after the BE-M1-04 PR is merged, so the snapshot and the module registration are based on main.
  - Put the address endpoints in their own controller and service (for example CustomerAddressesController and ICustomerAddressService in the Customers module folders); do not edit CustomersController.cs from BE-M1-04. All endpoints use policy CustomerOnly and take the customer id from ICurrentUser, never from the URL or body. 401 without a valid Bearer token; 403 for a non-Customer role.
  - GET /api/customers/me/addresses: 200 with Address[] ordered default first, then createdAt descending; an empty list is a valid 200.
  - POST /api/customers/me/addresses: 201 with the Address (fields per contract §1; request fields per §2.2). The client never sends totalAreaM2 and a sent value is ignored.
  - S_total (PRD §1.1) is computed by the server: totalAreaM2 = floorAreaM2 x numFloors, rounded to 2 decimals (round half away from zero, via the Vnd-style single rounding rule of G-2 or the same MidpointRounding.AwayFromZero), returned in every Address. The database column total_area_m2 is already a computed column (CustomerAddressConfiguration); do not change the schema.
  - Validation, every failure is 400 with data.errors map (decisions O5): label required 1-50; addressLine required 1-255; district and city required 1-100; housingType one of APARTMENT, HOUSE, ROOM; floorAreaM2 > 0 and <= 9999.99, and for ROOM <= BusinessRules Address.RoomMaxAreaM2 (30); numFloors integer 1-255, then APARTMENT and ROOM must be 1 and HOUSE must be 1 to BusinessRules Address.HouseMaxFloors (10); bedrooms and bathrooms optional integer 0-255; latitude -90 to 90; longitude -180 to 180. Read the limits from BusinessRules, no numbers in handlers (decisions G-4).
  - Default address: the customer's first address becomes default automatically (isDefault in the request is ignored). isDefault = true on a later address makes it the default and clears the flag on the previous default in the same transaction (IUnitOfWork); never more than one default per customer.
  - GET /api/customers/me/addresses/{addressId}: 200 with the Address; 404 if it does not exist or belongs to another customer (no existence leak).
  - PUT /api/customers/me/addresses/{addressId}: full replace with the same body and validation as POST; 200 with the Address; 404 as above; the default rule above applies; editing does not touch existing orders.
  - DELETE /api/customers/me/addresses/{addressId}: 200 with data null; 404 as above; 409 when a JOB_ORDER references the address (JOB_ORDER.address_id FK, decisions Q21 C5; check through the data layer inside the Customers module, no new port and no schema change; if that is not possible without a port, stop and open a Scope exception). If the default address is deleted, the most recently created remaining address becomes default (none left means no default).
  - Unit and integration tests in Backend/Tests/Customers/ (DB-backed ones against SQL Server, cleaning up the rows they create) covering: S_total server computation and rounding (client totalAreaM2 ignored), every validation rule and its boundary (ROOM 30 vs 30.01, HOUSE 10 vs 11 floors, APARTMENT 2 floors rejected), first address auto-default, switching default in one transaction with exactly one default left, list order, ownership 404 on GET/PUT/DELETE of another customer's address, DELETE 409 with an order referencing the address, default re-election after deleting the default, 401 and 403.
  - Update the .spec/contracts/openapi.json snapshot (regenerate with the OpenApiUpdateHelper test; OpenApiSnapshotTests must pass).
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5). Favorite workers (BE-M1-06) and the profile endpoints (BE-M1-04) are separate tickets.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. Tick BE-M1-05 in .spec/plan/M1-platform-identity.md with evidence in the same PR.

## #51 [ticket] BE-M1-06 Favorite workers (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Customers/**
  - Backend/WebAPI/Controllers/Customers/**
  - Backend/Infrastructure/Modules/Customers/**
  - Backend/Tests/Customers/**
  - Backend/Domain/Entities/*.Customers.cs
  - .spec/plan/M1-platform-identity.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BE-M1-06. Read .spec/contracts/customers.md (§1 FavoriteWorker, §2.3, §4 C3), .spec/contracts/identity.md (§1 conventions) and .spec/decisions.md (Q21 C3).
  - Put the favorite-worker endpoints in their own controller and service in the Customers module folders (for example CustomerFavoriteWorkersController and ICustomerFavoriteWorkerService); do not edit the controllers or services of BE-M1-04 / BE-M1-05. All endpoints use policy CustomerOnly and take the customer id from ICurrentUser, never from the URL or body. 401 without a valid Bearer token; 403 for a non-Customer role.
  - Worker data (fullName, ratingAvg, completedJobs, workStatus) comes ONLY from the existing read port IWorkerProfileQuery (decisions Q21 C3, Backend/Application/Interfaces/Ports/IWorkerProfileQuery.cs). The Customers module never reads the WORKER table or the Workers module directly. The port is the FakeWorkerProfileQuery until M4 provides the real one; do not add or change a port (if a change is needed, stop and open a Scope exception).
  - GET /api/customers/me/favorite-workers: 200 with FavoriteWorker[] { workerId, fullName, ratingAvg, completedJobs, workStatus, addedAt } ordered by addedAt descending; one batched port call (GetManyAsync), not one call per row; an empty list is a valid 200. workStatus uses the database spelling (PENDING, IDLE, BUSY, LOCKED). A favorite row whose worker the port does not return is left out of the list (state this in the PR).
  - PUT /api/customers/me/favorite-workers/{workerId}: 200 with the FavoriteWorker; 404 when IWorkerProfileQuery says the worker does not exist. Idempotent: adding a worker already in the list returns the existing row with its original addedAt, changes nothing and returns no error. Double-click safe, including two concurrent requests for the same pair (a primary-key violation must be turned into the existing row, never a 500).
  - DELETE /api/customers/me/favorite-workers/{workerId}: 200 with data null. Idempotent: removing a worker that is not in the list is still 200. It only ever touches the caller's own row.
  - A customer only sees and changes their own favorites; two customers favoriting the same worker do not affect each other. addedAt is set by the server through IClock (UTC).
  - Unit and integration tests in Backend/Tests/Customers/ (DB-backed ones against SQL Server with real WORKER rows created through Worker.CreateFreelancer and cleaned up afterwards, and the port Fake registered with the same workers) covering: list order and empty list, the batched port call, add happy path, idempotent re-add keeps the original addedAt and creates one row, concurrent double add yields one row and no error, 404 for a worker the port does not know, remove then remove again, isolation between two customers, left-out favorite when the port does not return the worker, 401 and 403.
  - Update the .spec/contracts/openapi.json snapshot (regenerate with the OpenApiUpdateHelper test; OpenApiSnapshotTests must pass).
  - No package, endpoint or entity beyond what this ticket names (AGENTS.md rule 5). The real IWorkerProfileQuery implementation belongs to M4 (BE-M4-*) and is not part of this ticket; until it lands, PUT returns 404 in a running app because the Fake knows no workers.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. Tick BE-M1-06 in .spec/plan/M1-platform-identity.md with evidence in the same PR.

## #53 [ticket] BE-M1-07 Identity and Customers module tests (M1 - Kiệt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Tests/Identity/**
  - Backend/Tests/Customers/**
  - .spec/plan/M1-platform-identity.md
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task BE-M1-07 ("Test module Identity + Customers (OTP hết hạn/quá số lần, S_total, quyền sở hữu địa chỉ)"). Read .spec/contracts/identity.md (§2.1, §2.2) and .spec/decisions.md (Q06, Q20).
  - Tests only: no production code, no endpoint, no entity, no package, no change to .spec/contracts/openapi.json (AGENTS.md rule 5). If a test reveals a defect in production code, do NOT fix it here: report it with the failing test name and log in this issue and open a separate ticket.
  - Already covered by earlier tickets and only referenced in the PR, not duplicated: OTP over the attempt limit (VerifyOtp_fifth_wrong_attempt_invalidates_code_and_returns_429), S_total server computation and rounding (CustomerAddressTests), address ownership 404 on GET/PUT/DELETE (CustomerAddressTests.Another_customers_address_is_404_on_get_put_and_delete_and_stays_untouched).
  - Add the missing OTP cases in Backend/Tests/Identity/ (new file, DB-backed against SQL Server like the existing Identity tests, cleaning up the OTP_CODE, CUSTOMER and REFRESH_TOKEN rows they create): a correct code is accepted up to and including the TTL boundary (Otp.TtlMinutes, expires_at) and rejected with 401 one second later; an expired code is consumed so it cannot be used afterwards; a successfully verified code is single-use (a second verify of the same code returns 401); a correct code sent after the attempt limit was reached still returns 401 (the code was invalidated, a new request is needed); a code issued for role Customer does not verify for role Worker on the same phone and the other way round; the stored code_hash is never the plain code and does not equal an unkeyed SHA-256 of it (keyed HMAC, decisions Q20); a new otp/request invalidates the previous code so the old code returns 401.
  - Add one cross-module flow test in Backend/Tests/Customers/ (DB-backed, with cleanup): OTP verify of a new phone creates the Customer (isNewUser = true) and returns an access token whose sub equals user.Id and whose role is Customer (ITokenService.ValidateAccessToken); with that id the profile service returns the new customer (empty fullName, trustScore Customer.InitialTrustScore, ACTIVE), PUT profile updates it, an address can be created with the server-computed S_total, a worker added through the favorites service appears in the list (worker data from the IWorkerProfileQuery Fake with a real WORKER row), and a second customer sees none of it.
  - State in the PR which existing tests silently return when SQL Server is unreachable (the IsSqlServerAvailable() convention used by Identity and Customers tests) so reviewers know they do not check anything on a machine without SQL Server; do not change that convention in this ticket.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. Tick BE-M1-07 in .spec/plan/M1-platform-identity.md with evidence in the same PR.

## #55 [ticket] HARNESS-02 Share the TỔ ẤM Nordic Care design system as a team skill
- status: done
- area: shared
- blocked-by: -
- allowed:
  - .agents/**
  - .claude/**
- acceptance:
  - Purpose: share the team design system "TỔ ẤM - Nordic Care" with every member and every agent (Claude, Codex, Antigravity) as one skill, so UI work looks the same across Web (Frontend) and Mobile. Source material: the two files in the untracked folder .agents/skills/_project-team-design-template/ on the author's machine (DESIGN.md with YAML design tokens, and t_m_nordic_care_design_system_design.md with the written guidelines). Documents and agent config only: no application code.
  - Defaults chosen by the author of this ticket, NOT yet confirmed by the leader (change them in this issue before work starts if you disagree): (1) skill name project-team-design-template (kebab-case like the other skills, the leading underscore of the local folder is dropped); (2) description: "Apply the TỔ ẤM Nordic Care design system (colors, typography, spacing, layout, components) when building Web or Mobile UI for this project"; (3) scope: guidance for both Web (Frontend/) and Mobile (Mobile/), because the guidelines describe customer-facing screens (service detail, booking, pricing, payment) while Frontend/ is Admin and Partner desktop only and Mobile/ is the Customer and Worker app (.spec/plan/00-overview.md decisions D2/D3).
  - The skill lives in .agents/skills/project-team-design-template/ (source of truth). Move the untracked local folder there; do not edit the content of DESIGN.md and t_m_nordic_care_design_system_design.md (same words and values; only line endings may be normalized by git).
  - Add .agents/skills/project-team-design-template/SKILL.md with YAML frontmatter name: and description: (both required by harness/check-agent-config.sh) and a short body: when to use the skill; which file to read first and what each holds; how to apply the tokens on Web (read Frontend/ARCHITECTURE.md first: tokens map to the existing Tailwind / shadcn/ui setup) and on Mobile (Flutter theme, 390x844 design frame from decisions D2); the rule that values come only from the two source files and that no color, font, spacing or component is invented; the reminder that a screen is only changed under its own feature ticket. Do not copy the long source documents into SKILL.md; reference them.
  - Run node harness/sync-agent-skills.mjs so .claude/skills/project-team-design-template/ is generated; never edit .claude/skills by hand (AGENTS.md "Skills and agent config").
  - sh harness/check-agent-config.sh passes (skill has SKILL.md, frontmatter name and description, mirror in sync). Paste its output.
  - Do not apply the design system to any file under Frontend/ or Mobile/ in this ticket, and do not touch AGENTS.md, CLAUDE.md, .claude/settings.json, .codex/**, harness/** or .github/** (this ticket's allowed list is exactly .agents/** and .claude/**).
  - No secrets, tokens or personal data in the skill files (AGENTS.md: never commit secrets). Check the two source files for any before committing and paste the grep command and its output as evidence.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. After the PR merges, verify no longer fails for team members who pull main and have the folder untracked, because the folder is tracked.
  - No package, endpoint or entity (AGENTS.md rule 5). There is no checklist row to tick: this is shared team infrastructure, not a task of .spec/plan/Mx-*.md.

## #57 [ticket] MOB-BASE-01 Flutter scaffold and architecture setup
- status: done
- area: shared
- blocked-by: -
- allowed:
  - Mobile/**
  - **/AGENTS.md
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Plan reference: .spec/plan/M3-dispatch-field.md task MOB-BASE-01. Read .agents/skills/project-team-design-template/DESIGN.md and t_m_nordic_care_design_system_design.md.
  - Scaffold Flutter project in Mobile/ supporting Customer and Worker entries within the 390x844 design frame.
  - Configure TỔ ẤM Nordic Care theme: canvas #FAF8F5, primary #2D5A43, accent #6B8F71, amber #D99B26, charcoal text #1F2923, typography, pill buttons and double-bezel cards.
  - Structure core state management, theme data, and entry navigation.
  - Add Mobile/ARCHITECTURE.md and Mobile/AGENTS.md.
  - flutter analyze and flutter test PASS.
  - sh harness/verify.sh L3 PASSES. Tick MOB-BASE-01 in .spec/plan/M3-dispatch-field.md.

## #58 [ticket] HARNESS-03 Skill token precedence: DESIGN.md wins, copy is illustrative
- status: done
- area: shared
- blocked-by: -
- allowed:
  - .agents/**
  - .claude/**
- acceptance:
  - Context: HARNESS-02 (#55, PR #56) shared the TỔ ẤM Nordic Care design system as the skill project-team-design-template. Its SKILL.md lists conflicting values from the two source files side by side (Canvas #FAF8F5 / #FAF8F3, Primary #2D5A43 / #416448, Amber #D99B26 / #D9A441) without saying which wins, so each agent or member would pick one at random and screens would drift apart. Documents and agent config only: no application code.
  - Leader decisions (2026-10-06, recorded here): (1) DESIGN.md is the single source of truth for every color, font, size, radius and spacing value; the other file (t_m_nordic_care_design_system_design.md) supplies layout patterns, component structure and tone only. (2) The marketing copy and numbers of the other file are illustrative only, not product facts.
  - Rewrite .agents/skills/project-team-design-template/SKILL.md (keep the frontmatter name and description) so it states: the precedence (1 YAML tokens of DESIGN.md by token name and value exactly as written, 2 prose of DESIGN.md, 3 the other file for layout and structure only); that when DESIGN.md has no value for something the agent must not copy the other file's hex but use the closest DESIGN.md token or ask in the ticket; a table "where the files disagree" with the DESIGN.md value to use (primary #416448, primary-hover #56705B, canvas-background #FAF8F3, card-surface #FFFFFF, text-primary #2F3A34, text-secondary #6B7280, amber #D9A441 from the DESIGN.md prose, Newsreader for headline-* tokens and Plus Jakarta Sans for body-*, label-*, metric); the two contradictions that remain inside DESIGN.md itself (the text names Brand Primary Sage Green #6B8F71 but the YAML primary is #416448 and there is no #6B8F71 token; primary-hover #56705B is lighter than primary #416448) with the instruction to follow the YAML and report them to the leader, not to pick a hex; that the copy and numbers of the other file (50,000,000đ insurance, HEPA 99.9 %, "240.000 đ / ca 3h", "Haisey", kit cards, ADR-0010) are layout filler: prices are data (PRICE_RULE, decisions Q01), a shift is up to 4 hours (PRD BR-01), and any insurance, certification or guarantee claim needs the leader's approval before it appears on a screen.
  - Platform guidance must only state what is true in the repo: Frontend/ is the Admin and Partner Portal desktop 1440x900 and Mobile/ is the Customer and Worker app with a 390x844 frame (.spec/plan/00-overview.md section 8, D2 and D3; note these are NOT the D1-D7 of decisions.md section Q22); Frontend uses React + Vite + Tailwind CSS v4 + shadcn/ui (Frontend/ARCHITECTURE.md); mapping the tokens into the global theme (Frontend/src/index.css) and declaring fonts in Mobile/pubspec.yaml are not covered by WEB-BASE-01..04 or MOB-BASE-01..03 in the plans, so they need their own ticket and must not be done inside a feature ticket. Do not state who owns that work.
  - Do not change the content of DESIGN.md or t_m_nordic_care_design_system_design.md (the contradictions are reported, not fixed here).
  - Run node harness/sync-agent-skills.mjs so .claude/skills/project-team-design-template/ matches; never edit .claude/skills by hand.
  - sh harness/check-agent-config.sh passes (paste its output). Every hex, token name and file reference written in SKILL.md is checked against the source files with a grep and the output is pasted as evidence.
  - Do not touch AGENTS.md, CLAUDE.md, .claude/settings.json, .codex/**, harness/**, .github/**, Frontend/ or Mobile/ (this ticket's allowed list is exactly .agents/** and .claude/**).
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - No package, endpoint or entity (AGENTS.md rule 5). No checklist row to tick: shared team infrastructure.

## #61 [ticket] MOB-THEME-01 Align the Mobile theme with DESIGN.md tokens
- status: ready
- area: shared
- blocked-by: #58
- allowed:
  - Mobile/lib/core/theme/**
  - Mobile/test/theme_test.dart
  - Mobile/test/widgets_test.dart
  - Mobile/ARCHITECTURE.md
- acceptance:
  - Context: the leader decided on 2026-10-06 that DESIGN.md is the single source of truth for every color, font, size, radius and spacing value of the TỔ ẤM design system (skill .agents/skills/project-team-design-template/SKILL.md, issue #58). MOB-BASE-01 (#57, PR #59) merged the Mobile theme with the OTHER file's palette, so the Mobile code now contradicts the skill. Blocked by #58 so the skill text is final before the code is aligned. Mobile theme code and its docs only: no new screen, no new package (AGENTS.md rule 5).
  - Align Mobile/lib/core/theme/nordic_colors.dart with DESIGN.md (YAML token name and value, as written), at least these (current value -> DESIGN.md value): primary 0xFF2D5A43 -> primary #416448; primaryHover 0xFF224432 -> primary-hover #56705B; canvas 0xFFFAF8F5 -> canvas-background #FAF8F3 (canvasVariant is then redundant; keep only what a widget uses); accentAmber 0xFFD99B26 -> #D9A441 (named in the DESIGN.md prose for ratings and guarantees); textTitle 0xFF1F2923 -> text-primary #2F3A34; textSecondary stays #6B7280 (already text-secondary); cardSurface / surface #FFFFFF stays (card-surface).
  - Fix the transcription error: surfaceDim 0xFFDBD0D5 must be DESIGN.md surface-dim #dbdad5.
  - Values with NO DESIGN.md token: do NOT keep them silently and do NOT copy another hex. Each needs an answer from the leader in a comment on this issue before it is changed; until then leave it as it is and list it in the PR under "Not done": secondaryAccent 0xFF6B8F71 (Sage Green; DESIGN.md prose names it Brand Primary but the YAML has no such token), contrastDark 0xFF1A2F25 and contrastCard 0xFF223B2F (dark anchor section; closest DESIGN.md tokens would be inverse-surface #30312e), success 0xFF2E7D32 (appears in neither source file), surfaceSubtle 0xFFF4F2EB (closest surface-container-low #f5f3ee), textBody 0xFF4B5563 (closest on-surface-variant #424842), border 0xFFE5E7EB (closest subtle-border rgba(229,231,235,0.75) or outline-variant #c2c8bf). Proposed mapping in brackets is a suggestion, not a decision.
  - Typography (Mobile/lib/core/theme/nordic_typography.dart): compare with the DESIGN.md typography tokens (headline-lg 40 / headline-lg-mobile 32 / headline-md 28 / headline-sm 22 in Newsreader; body-lg 18 / body-md 16 / body-sm 14, label-md 14 / label-sm 12, metric 24 in Plus Jakarta Sans). Today every style uses Plus Jakarta Sans only. Align the font family per token and the sizes, weights and line heights to the YAML. Bundling the font files or adding a font package is NOT part of this ticket (a new package or font asset needs its own ticket, AGENTS.md rule 5): state in the PR how the fonts resolve at runtime and what was not verified.
  - Update Mobile/test/theme_test.dart (and widgets_test.dart if a test depends on the old values) so the tests assert the DESIGN.md values, and add a test that every color constant that has a DESIGN.md token equals that token.
  - Update the palette section of Mobile/ARCHITECTURE.md (lines 12-16 today) to the same values and name DESIGN.md as the source.
  - Evidence: paste the grep of each aligned hex in DESIGN.md (command and output) and the flutter test output (command and result). If Flutter is not installed on the machine, say so and do not claim the tests pass.
  - Do not touch DESIGN.md or the other design file, AGENTS.md, Mobile/AGENTS.md, Mobile/pubspec.yaml, harness/**, .github/**, .agents/** or .claude/** (this ticket's allowed list is exactly the four paths above).
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. No checklist row to tick.

## #62 [ticket] BE-M6-00 Contract Ratings + Disputes + Payouts + Admin (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - .spec/contracts/ratings.md
  - .spec/contracts/disputes.md
  - .spec/contracts/payouts.md
  - .spec/contracts/admin.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-00. Read .spec/decisions.md (Q10, Q12, Q14, Q18, G-5, Q22 D3), .spec/plan/00-overview.md (sections 3-5) and the format of .spec/contracts/identity.md and customers.md first.
  - Four contract files, one per module: ratings.md, disputes.md, payouts.md, admin.md. Each lists endpoints (method, path, role/policy, request, response, error codes), DTO shapes, state/validation rules and the domain events it publishes or handles, following the conventions in identity.md section 1 (envelope, camelCase, UTC, status codes, 404-for-not-owned).
  - Every rule and number comes from .spec/spec.md or .spec/decisions.md with a source reference; anything neither file covers is written as an open question (Mx) with a recommended default and is NOT invented (G-7).
  - Covers plan tasks BE-M6-01..07 and BE-M6-09 (ratings window, dispute filing and verdict, absence approval, payout batch and bank export, admin account and ops metrics, income, audit log read) and the dashboard metrics of WEB-M6-04 (orders, shifts, open disputes, disputes near SLA).
  - Document-only ticket: no code, package, endpoint implementation or schema change. Open questions are listed so the leader can approve G3.
  - sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. Tick BE-M6-00 in .spec/plan/M6-ratings-disputes-payouts.md with evidence in the same PR.

## #63 [ticket] WEB-BASE-01 Router and role layout with auto-loaded feature routes (M6 - Anh)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/**
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task WEB-BASE-01. Read Frontend/ARCHITECTURE.md, Frontend/AGENTS.md and .spec/plan/00-overview.md section 3 first.
  - Router and layout by role for Admin and Partner, desktop 1440x900 (decision D3: Frontend is Admin + Partner Portal only). Layout is a 240 px sidebar plus main content, matching the Figma Admin Page (file h7Tmwu071XyOZRUVoAn37o, node 19:2).
  - Routes are auto-loaded from Frontend/src/features/*/routes.tsx (import.meta.glob) so nobody edits Frontend/src/app/**. Create the feature folders agencies, skills, workers, dispatch, ratings, disputes, payouts, admin, each with an empty routes.tsx export.
  - Frontend/ARCHITECTURE.md documents the route auto-loading mechanism.
  - Test or evidence: adding a dummy feature with its own routes.tsx WITHOUT editing any shared file makes its route appear (log or test name).
  - npm run lint and tsc -b PASS (log paths). sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - No package, endpoint or env var beyond what this ticket names (AGENTS.md rule 5). The ONE new package allowed is react-router-dom (approved by the owner Anh on 2026-10-06 because Frontend/ARCHITECTURE.md names no router); add it with its lockfile change and record the choice in Frontend/ARCHITECTURE.md.
  - Tick WEB-BASE-01 in .spec/plan/M6-ratings-disputes-payouts.md with evidence in the same PR.

## #66 [ticket] WEB-BASE-03 Typed API client over the ApiResponse envelope (M6 - Anh)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/**
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task WEB-BASE-03. Read Frontend/ARCHITECTURE.md (convention: call the API only through src/services/api.ts with relative /api/... paths) and .spec/contracts/identity.md section 1 (envelope, status codes, error shape O5) first.
  - src/services/api.ts keeps its relative-path convention and the existing api<T>() export (nothing else imports it yet, but do not break it). Add a typed client on top: parses the envelope ApiResponse<T> { success, message, data } (camelCase, as Backend/Middleware/ResponseWrapperMiddleware.cs emits), returns data on 2xx, and throws a typed ApiError carrying status, message, data, fieldErrors (the data.errors map of decision O5 on 400) and retryAfterSeconds (Retry-After header, 423/429).
  - Handles a non-JSON or empty body (e.g. a proxy 502 or a 500 without envelope) as an ApiError with the HTTP status, and a network failure as an ApiError with status 0. Never throws a raw SyntaxError.
  - Extension points for WEB-BASE-02 without implementing it: configureApi({ getAccessToken, onUnauthorized }) adds Authorization: Bearer <token> when a token exists and calls onUnauthorized once on a 401 for an authenticated request. No token storage, no login in this ticket.
  - Helpers: query-string builder that drops undefined/null values and encodes the rest; JSON body helper. The pure client core takes fetch and baseUrl as inputs so it is testable without a browser (no import.meta in that file).
  - Test evidence: tests run with node --test (no new package; add only the npm script test) cover envelope success, 400 with fieldErrors, 401 + onUnauthorized once, 423 with Retry-After, non-JSON 502, network failure, query builder (names of the tests + output pasted).
  - Types are hand-written from the contracts until gate G4 (OpenAPI client generation needs a package this ticket does not add). Document this and the usage example in Frontend/ARCHITECTURE.md.
  - No package beyond what this ticket names (AGENTS.md rule 5). npm run lint and tsc -b PASS. sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR.
  - Tick WEB-BASE-03 in .spec/plan/M6-ratings-disputes-payouts.md with evidence in the same PR.

## #68 [ticket] WEB-BASE-04 Shared data table and Before/After photo viewer (M6 - Anh)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/**
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task WEB-BASE-04. Read Frontend/ARCHITECTURE.md and the design skill .agents/skills/project-team-design-template/SKILL.md (section 4: do NOT map TO AM tokens into src/index.css here; use shadcn theme tokens) first.
  - Shared components live in src/components/data-table/ and src/components/photo-compare/ (outside src/features, so no module owns them). No new package (AGENTS.md rule 5); plain React + Tailwind + the existing shadcn Button only.
  - DataTable<T>: many columns with horizontal scroll, typed column definitions (header, cell renderer, optional align/width), stable row key, optional row click, and the three states shown in the Figma frames: loading (skeleton rows), empty (message), error (message + retry button).
  - Server-side pagination matching the contract shape { items, page, pageSize, total }: Pagination component with previous/next + page numbers, page-size select, and the text "Hiển thị a-b trên tổng N"; range maths in a pure function.
  - Filtering: a toolbar slot with a search input and a chip/tab filter control (controlled by the caller; the table never filters data itself because paging is server-side).
  - Export: a pure toCsv (UTF-8 BOM so Excel reads Vietnamese, quotes/commas/newlines escaped) and a toolbar export button that downloads the CURRENT page rows as .csv. Real bank files (.xlsx, payouts contract section 2.3) stay server-side and are out of scope.
  - BeforeAfterViewer: pairs BEFORE and AFTER photos by angleNo (Q03: the AFTER angle set must equal the BEFORE set), side-by-side view plus a slider-overlay compare mode, previous/next and thumbnail navigation by angle, keyboard arrows, VoL score/accepted badge when given, placeholder for a missing counterpart. Pairing logic is a pure function.
  - Unit tests with node --test (no new package) for: toCsv escaping + BOM, pagination range/pages, photo pairing (missing side, extra angle, unsorted input) - names + output pasted.
  - Visual evidence: screenshot or page text from the dev server of a temporary demo (not committed) showing table states and the viewer.
  - Frontend/ARCHITECTURE.md documents both components and their props.
  - npm run lint and tsc -b PASS. sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. Tick WEB-BASE-04 in .spec/plan/M6-ratings-disputes-payouts.md with evidence in the same PR.

## #70 [ticket] BE-M6-09a Real IAuditLog writing ADMIN_AUDIT_LOG (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Admin/**
  - Backend/Infrastructure/Modules/Admin/**
  - Backend/Tests/Admin/**
  - Backend/Domain/Entities/*.Admin.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-09, first part only ("IAuditLog thật"): this ticket is the port implementation and nothing else. The audit-log read endpoint and the Super-Freelancer approve/revoke stay in BE-M6-09 and wait for the approved admin contract (rule: no endpoint outside an approved contract). Read .spec/decisions.md G-5 and SC-3 and .spec/plan/00-overview.md section 4 (IAuditLog: implementer M6) first.
  - A real IAuditLog (Backend/Application/Interfaces/Ports/IAuditLog.cs, interface unchanged) implemented in Backend/Infrastructure/Modules/Admin/ and registered by an AdminModule : IModule (BASE-02), so it wins over the Fake that FakePortRegistration adds with TryAdd. Program.cs and the shared DependencyInjection files are not edited.
  - Append-only: the implementation only adds rows; there is no update or delete method. WriteAsync adds the AdminAuditLog row to the scoped AppDbContext and does NOT call SaveChanges, so the row is committed or rolled back together with the change it records (G-5: same DB transaction).
  - changed_at comes from IClock (UTC). Validation: entityType 1-30, entityId 1-40, fieldName 1-50, oldValue/newValue max 500, reason max 255 (the SC-3 column sizes); an over-long or empty required value is rejected with ArgumentException, never silently truncated. An ADMIN actor requires adminId and a non-empty reason; a SYSTEM actor requires adminId = null.
  - Tests (names + output pasted): the entry is tracked as Added with every field mapped and ChangedAt from the clock, and nothing is saved by WriteAsync; each validation rule; the module registers the real implementation over the Fake in a service provider built like Program.cs does; with SQL Server available (same skip rule as Backend/Tests/Persistence), a committed transaction keeps the row and a rolled-back one leaves none.
  - Document in Backend/ARCHITECTURE.md how a module uses IAuditLog (call it before SaveChanges inside the same unit of work).
  - No package, endpoint, entity or schema change (AGENTS.md rule 5). sh harness/verify.sh L3 PASSES; paste the last line and the log path in the PR. Add a note "IAuditLog done (ticket #<this>, PR #<pr>)" to the BE-M6-09 line in .spec/plan/M6-ratings-disputes-payouts.md but do NOT tick BE-M6-09 (the endpoint and Super-Freelancer are still open).

## #72 [ticket] MOB-M1-01 Customer and Worker OTP login and token storage (M1 - Kiệt)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Mobile/lib/features/identity/**
  - Mobile/test/features/identity/**
  - Mobile/lib/app/routes.dart
  - Mobile/lib/features/home/home_screen.dart
  - .spec/plan/M1-platform-identity.md
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task MOB-M1-01. Read .spec/contracts/identity.md (§2.1, §2.2) and .agents/skills/project-team-design-template/DESIGN.md.
  - Implement OTP Login flow for Customer and Worker personas matching TO AM Nordic Care UI guidelines (390x844 frame, Deep Forest Pine CTA, double-bezel card, eyebrow badge).
  - PhoneInputScreen: validates Vietnamese mobile number format (10 digits starting with 0[35789]), dispatches OTP request, handles loading and errors.
  - OtpVerifyScreen: 6-digit numeric code entry, countdown timer for 60s cooldown and 300s TTL, dispatches verify, handles 401/429/400 errors.
  - TokenStorage: stores accessToken, refreshToken, user profile metadata.
  - AuthService: handles requestOtp and verifyOtp with API client and mock fallback.
  - Wire up login navigation from HomeScreen and routes.dart.
  - Unit and widget tests in Mobile/test/features/identity/ covering phone validation, OTP verification, countdown timer, and token storage.
  - flutter analyze: 0 issues; flutter test PASS.
  - sh harness/verify.sh L3 PASSES. Tick MOB-M1-01 in .spec/plan/M1-platform-identity.md.

## #74 [ticket] MOB-M1-02 Customer address book (M1 - Kiệt)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Mobile/lib/features/customers/**
  - Mobile/test/features/customers/**
  - Mobile/lib/app/routes.dart
  - Mobile/lib/features/home/home_screen.dart
  - .spec/plan/M1-platform-identity.md
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task MOB-M1-02. Read .spec/contracts/customers.md (§2.2), .spec/spec.md (§1.1), and .agents/skills/project-team-design-template/DESIGN.md.
  - Models: CustomerAddress, HousingType enum (APARTMENT, HOUSE, ROOM), AddressInputDto.
  - S_total rule: totalAreaM2 = floorAreaM2 * numFloors. Client calculates preview live; server returns canonical totalAreaM2.
  - Housing type validation: APARTMENT and ROOM restrict numFloors = 1; ROOM enforces floorAreaM2 <= 30 m2; HOUSE permits numFloors between 1 and 10.
  - Coordinates: latitude (-90..90) and longitude (-180..180) input/selector with GPS coordinates picker.
  - AddressListScreen: displays customer addresses, default badge, S_total details, housing type tag, action to add new, tap to edit, and delete with confirmation dialog.
  - AddressFormScreen: full form for add/edit with NordicCard, housing type chips/toggle, live S_total calculation banner, GPS picker, isDefault toggle.
  - CustomerAddressService: CRUD operations (getAddresses, createAddress, updateAddress, deleteAddress) with real HTTP client and mock fallback for testing/dev.
  - Adhere to TO AM Nordic Care visual guidelines (390x844 frame, Deep Forest Pine buttons, double-bezel cards, Warm Sand canvas).
  - Unit and widget tests in Mobile/test/features/customers/ covering models, S_total calculation, validation, service CRUD, and list/form screens.
  - flutter analyze: 0 issues; flutter test PASS.
  - sh harness/verify.sh L3 PASSES. Tick MOB-M1-02 in .spec/plan/M1-platform-identity.md.

## #75 [ticket] TEST-FIX-01 VndTests parse with InvariantCulture (fail on comma-decimal machines)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Tests/Domain/VndTests.cs
- acceptance:
  - Cause: Backend/Tests/Domain/VndTests.cs lines 14, 22 and 29 call decimal.Parse(string) with the current culture. On a machine whose decimal separator is , (e.g. vi-VN), . is the group separator, so "2.5" parses to 25 and "0.200" to 200 ([decimal]::Parse("2.5", [cultureinfo]'vi-VN') -> 25). 7 cases fail there (seen in PR #64/#65/#67/#69/#71, log .harness/evidence/20261006-153729-L3.log:32-38); every author had to run L3 with DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1.
  - Parse every test value with CultureInfo.InvariantCulture (one private helper in the test class). Test data, expected values and the number of cases stay the same. Backend/Domain/ValueObjects/Vnd.cs is not changed (the domain does no string parsing).
  - Evidence: the 12 VndTests cases fail under vi-VN before the fix and pass under both the current culture and vi-VN after it (culture forced by a temporary local probe that is not committed); output pasted in the PR.
  - sh harness/verify.sh L3 passes WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT; paste the last line and log path in the PR.
  - Out of scope (noted only): Backend/Infrastructure/Fakes/FakePaymentGateway.cs:38 also uses culture-dependent decimal.TryParse.

## #76 [ticket] HARNESS-04 scope-check accepts a merge of origin/main into a ticket branch
- status: done
- area: shared
- blocked-by: -
- allowed:
  - harness/**
  - HARNESS.md
- acceptance:
  - Problem: in pre-commit, harness/scope-check.sh:40 checks git diff --cached --name-only (staged vs HEAD). A merge commit of origin/main into a ticket branch stages every file main changed, so scope-check fails and a member cannot resolve a conflict locally without bypassing the hook (seen on PR #69, 2026-10-06: blocked with .spec/contracts/*.md and Frontend files "not in allowed paths"; the conflict had to be resolved in the GitHub web editor). CI is not affected (--ci origin/main uses origin/main...HEAD).
  - When a merge is in progress (MERGE_HEAD exists) and the merged commit is already contained in origin/main (git merge-base --is-ancestor MERGE_HEAD origin/main), scope-check checks the files where the staged result differs from MERGE_HEAD (git diff --cached --name-only MERGE_HEAD): exactly the ticket's own changes after the merge, including any conflict resolution. Every such file must still be in allowed, and protected paths still need a literal entry.
  - A merge of anything not contained in origin/main (another ticket branch, an unpushed local commit) FAILS with evidence (MERGE_HEAD sha, rule) and the hint to git fetch origin and merge origin/main only.
  - Normal (non-merge) commits and --ci mode behave exactly as before.
  - Evidence in the PR (command + output), on a throwaway local branch: (1) merging origin/main with a conflict in an allowed file, resolved, commits OK; (2) a resolution that edits a file outside allowed is rejected; (3) merging a non-main branch is rejected; (4) a normal out-of-scope commit is still rejected.
  - Document the member flow (git fetch origin then git merge origin/main on the ticket branch, resolve, commit; never rebase + force push) in HARNESS.md.
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.

## #80 [ticket] MOB-M1-03 Favorite workers list and management (M1 - Kiệt)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Mobile/lib/features/customers/**
  - Mobile/test/features/customers/**
  - Mobile/lib/app/routes.dart
  - Mobile/lib/features/home/home_screen.dart
  - .spec/plan/M1-platform-identity.md
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task MOB-M1-03. Read .spec/contracts/customers.md (§2.3) and .agents/skills/project-team-design-template/DESIGN.md.
  - Models: FavoriteWorker model with workerId, fullName, ratingAvg, completedJobs, workStatus, addedAt.
  - Service: CustomerFavoriteWorkerService with getFavoriteWorkers, addFavoriteWorker, removeFavoriteWorker supporting real HTTP client and mock store fallback.
  - Widgets: FavoriteWorkerCard adhering to Nordic Care guidelines (double-bezel, work status badge, star rating, completed jobs, remove and book CTA buttons).
  - Screen: FavoriteWorkersScreen showing list of favorite workers, empty state with CTA, removing worker with undo / snackbar feedback.
  - Navigation: Add route /favorite-workers in AppRoutes and wire entrypoint card in Customer section of HomeScreen.
  - Tests: Unit tests for model serialization and service methods, widget tests for FavoriteWorkersScreen inside 390x844 viewport.
  - flutter analyze: 0 issues; flutter test PASS.
  - sh harness/verify.sh L3 PASSES. Tick MOB-M1-03 in .spec/plan/M1-platform-identity.md.

## #82 [ticket] MOB-M1-04 Customer profile (M1 - Kiệt)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Mobile/lib/features/customers/**
  - Mobile/test/features/customers/**
  - Mobile/lib/app/routes.dart
  - Mobile/lib/features/home/home_screen.dart
  - .spec/plan/M1-platform-identity.md
- acceptance:
  - Plan reference: .spec/plan/M1-platform-identity.md task MOB-M1-04. Read .spec/contracts/customers.md (§2.1) and .agents/skills/project-team-design-template/DESIGN.md.
  - Models: CustomerProfile model with customerId, phoneNumber, fullName, email, trustScore, accountStatus, createdAt.
  - Service: CustomerProfileService with getProfile and updateProfile supporting real HTTP client and mock store fallback.
  - Screen: CustomerProfileScreen adhering to Nordic Care guidelines (user avatar/header, verified phone badge, editable fullName and email inputs, trust score card with shield/stars, account status pill, save button with loading/feedback).
  - Navigation: Add route /profile in AppRoutes and wire entrypoint card in Customer section of HomeScreen.
  - Tests: Unit tests for model serialization and service methods, widget tests for CustomerProfileScreen inside 390x844 viewport.
  - flutter analyze: 0 issues; flutter test PASS.
  - sh harness/verify.sh L3 PASSES. Tick MOB-M1-04 in .spec/plan/M1-platform-identity.md.

## #84 [ticket] HARNESS-05 start-ticket finds Git sh on Windows (spawnSync sh ENOENT)
- status: done
- area: shared
- blocked-by: -
- allowed:
  - harness/**
- acceptance:
  - Problem: node harness/start-ticket.mjs <n> (and ./harness/run.ps1 start-ticket <n>, which only calls node, harness/run.ps1:14) ends with START-TICKET FAILED: scope-check does not accept this ticket / evidence: spawnSync sh ENOENT when run from PowerShell on Windows: harness/start-ticket.mjs:152 spawns sh, which is not on PATH there (Git for Windows puts only Git\cmd on PATH; C:\WINDOWS\system32\bash.exe is WSL and must not be used). The branch, label and tasks.md steps have already succeeded, so the message is also misleading: scope-check never ran. Seen on #75 and #76 (2026-10-06).
  - start-ticket finds Git's sh on Windows when sh is not on PATH: derived from git --exec-path (<Git>/mingw64/libexec/git-core -> <Git>/bin/sh.exe), then the same default as harness/run.ps1:17 (C:\Program Files\Git\bin\sh.exe). On Linux/macOS/Git Bash sh on PATH is used exactly as before. Never WSL bash.exe.
  - If no sh is found, the failure says so (Git Bash (sh) not found, with the paths tried as evidence) instead of "scope-check does not accept this ticket".
  - No new package; only harness/start-ticket.mjs (and harness/run.ps1 only if needed).
  - Evidence in the PR (command + output): --dry-run or a real start from PowerShell ends with scope-check: ... / READY instead of ENOENT; the same from Git Bash is unchanged.
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.

## #86 [ticket] MOB-FIX-01 Mobile services: no mock fallback in the real app, report server errors (M1 - Kiệt)
- status: done
- area: shared
- blocked-by: -
- allowed:
  - Mobile/lib/features/identity/services/**
  - Mobile/lib/features/customers/services/**
  - Mobile/lib/features/identity/screens/**
  - Mobile/lib/features/customers/screens/**
  - Mobile/test/features/identity/**
  - Mobile/test/features/customers/**
- acceptance:
  - Problem (review of MOB-M1-01..04 on 2026-10-06, main a44e7ae): the four Mobile services fall back to mock data on ANY failure, including an error status from the server, and the screens build them with the fallback on. Reproduced with a temporary local HttpServer probe against the default-constructed services:
  - - requestOtp answered 429 -> returned an OK result (expiresIn=300).
  - - verifyOtp answered 502 (non-JSON) or backend unreachable -> logged in with mock_jwt_access_token_demo, TokenStorage().isAuthenticated == true, for any 6-digit code.
  - - getAddresses answered 401 -> showed 2 seeded mock addresses [1, 2].
  - - createAddress answered 400 -> returned as saved with id=101.
  - Causes: auth_service.dart:18 useMockFallback = true by default and catch (e) around the whole request (:74, :150); customer_address_service.dart / favorite_worker_service.dart / customer_profile_service.dart swallow every exception (including the HttpException they throw for a non-2xx) and fall through to an in-memory store seeded in the constructor; HTTP timeouts are 600 ms.
  - **Mock only when the caller asks for it.** AuthService(useMockFallback:) defaults to false; the customer services keep isMock = false by default. With the flag off a service NEVER returns mock data, never seeds the mock store and never saves a mock token. With the flag on (as the existing tests pass it explicitly) the current mock behaviour stays, so the existing 60 tests still pass unchanged or with only the minimal edits they need.
  - **Errors are reported, not hidden.** With the flag off:
  - - a non-2xx answer becomes an error that carries the HTTP status and the envelope message (ApiResponse{success,message,data}) when the body is JSON, otherwise a generic Vietnamese message with the status; 400 keeps data.errors when present (decision O5); 429/423 keep the Retry-After seconds when present;
  - - a network failure or timeout becomes an error saying the server could not be reached;
  - - requestOtp throws on 429 and on any other non-200 (the screen shows the message); verifyOtp returns VerifyOtpResponse with errorMessage + statusCode (0 for network) and does NOT touch TokenStorage;
  - - getAddresses/getFavoriteWorkers/getProfile throw instead of returning mock data; create/update/delete throw instead of reporting success.
  - Real-network timeouts are at least 10 seconds (600 ms made a slow phone network look like an outage).
  - The existing screens show these errors through their current error UI (_errorMessage view / SnackBar); change a screen only where an error would otherwise be lost or crash it.
  - Tests, no new package (dart:io HttpServer on loopback, port 0): for each service, the probe cases above now give an error and no mock data/token: 429 on request OTP, 502 non-JSON and unreachable on verify OTP (and TokenStorage().isAuthenticated == false afterwards), 401 on a list, 400 with data.errors on a create, plus one 2xx happy path per service through the real HTTP code path. One widget test proves a list screen shows the server message on 401.
  - No new package, endpoint or model field (AGENTS.md rule 5); no change to Mobile/lib/app/**, Mobile/lib/core/** or the home screen. Out of scope (MOB-BASE-03, M3): shared API client, secure persistent token storage, refresh interceptor.
  - flutter analyze clean, flutter test all pass, sh harness/verify.sh L3 passes; paste the outputs, the last L3 line and its log path in the PR.

## #88 [ticket] MOB-BASE-02 Feature structure and route registry for all 6 slots
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Mobile/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Plan reference: .spec/plan/M3-dispatch-field.md task MOB-BASE-02. Read .spec/plan/00-overview.md (sections 1, 2, 3) and Mobile/ARCHITECTURE.md.
  - Create feature directory and route/registry conventions for all 10 domain modules across the 6 slots in Mobile/lib/features/**: identity, customers, booking, payments, dispatch, workers, agencies, ratings, disputes, payouts.
  - Wire feature route registration so that Mobile/lib/app/routes.dart (or router) loads each feature dynamically/declaratively, freezing Mobile/lib/app/** so other members do not have to touch shared app files.
  - flutter analyze clean (0 issues).
  - flutter test passes all existing and any new tests.
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.
  - Tick MOB-BASE-02 in .spec/plan/M3-dispatch-field.md with evidence: <log or PR#> in the same PR.

## #90 [ticket] BE-M3-00 Contract Dispatch (offers, check-in, absent, incidents)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - .spec/contracts/dispatch.md
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Plan reference: .spec/plan/M3-dispatch-field.md task BE-M3-00. Read .spec/spec.md §2.1, §2.5, BR-02/03/04/05/10, .spec/decisions.md Q07, Q10, Q12, Q14, Q17, Q22.
  - Write .spec/contracts/dispatch.md documenting all HTTP endpoints and SignalR hub events for Dispatch module:
  - - Offer distribution (30s countdown, accept/decline, timeout auto-transition to next worker)
  - - Field check-in (GPS <=100m, fallback house plate photo / customer confirmation)
  - - Customer absence reporting (BR-05, >=15m wait, >=2 system calls, Admin review approval flow)
  - - Incident reporting (BR-10, photo + GPS, 5-minute auto re-dispatch window)
  - - Dispatch tracking status query (Customer, Worker, Admin)
  - Conform to ApiResponse envelope conventions, camelCase properties, UTC ISO-8601 timestamps, role-based authorization policies (WorkerOnly, CustomerOnly, AdminOnly).
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.
  - Tick BE-M3-00 in .spec/plan/M3-dispatch-field.md with evidence: <log or PR#> in the same PR.

## #92 [ticket] MOB-BASE-03 Shared API client, auth token storage, and device wrappers (GPS, camera, permissions)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Mobile/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Plan reference: .spec/plan/M3-dispatch-field.md task MOB-BASE-03. Read .spec/plan/00-overview.md and Mobile/ARCHITECTURE.md.
  - Shared API Client with ApiResponse<T> envelope handling {success, message, data}, ApiException, and base URL configuration.
  - Secure persistent token storage interface & implementation with refresh token / auth header interceptor.
  - Hardware / device wrappers with clean abstraction and testability:
  - - LocationWrapper: GPS coordinate retrieval, distance calculation (Haversine formula), accuracy check.
  - - CameraWrapper: Photo capture / inspection image loading.
  - - PermissionWrapper: Location and camera permission checking / requests.
  - No third-party packages beyond flutter sdk (using dart:io and Flutter foundation).
  - flutter analyze clean (0 issues).
  - flutter test passes all existing and new tests.
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.
  - Tick MOB-BASE-03 in .spec/plan/M3-dispatch-field.md with evidence: <log or PR#> in the same PR.

## #94 [ticket] BE-M3-01 Pure function MatchingScore for dispatch candidate ranking
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Plan reference: .spec/plan/M3-dispatch-field.md task BE-M3-01. Read .spec/spec.md §2.1 and .spec/decisions.md Q14.
  - Implement pure function MatchingScore calculator in Backend/Application/Features/Dispatch/:
  - - Distance component: normalized score based on distance (km) within search radius (closer distance yields higher score).
  - - Rating component: normalized score based on worker average rating (1.0 - 5.0 scale).
  - - Job history component: normalized score based on completed jobs count and completion rate.
  - - Configurable weights via options (DistanceWeight, RatingWeight, HistoryWeight) defaulting to 0.40, 0.40, 0.20 (sum = 1.0).
  - - Pure function: deterministic, no side effects, no database calls or I/O.
  - Comprehensive unit tests in Backend/Tests/Dispatch/:
  - - Proves deterministic calculation, weight sensitivity, and candidate ranking.
  - - Edge cases: minimum rating (1.0), perfect rating (5.0), 0km distance, boundary distance, 0 jobs vs high experience.
  - dotnet test passes all tests.
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.
  - Tick BE-M3-01 in .spec/plan/M3-dispatch-field.md with evidence: <log or PR#> in the same PR.

## #96 [ticket] MOB-BASE-04 Shared mobile UI kit: countdown timer, mini map, form inputs, and states
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Mobile/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Plan reference: .spec/plan/M3-dispatch-field.md task MOB-BASE-04. Read .agents/skills/project-team-design-template/DESIGN.md and Mobile/ARCHITECTURE.md.
  - Implement shared UI kit components in Mobile/lib/core/widgets/:
  - - CountdownTimerWidget: 30-second countdown widget for job offers with visual progress, remaining seconds display, and onTimeout callback.
  - - MiniMapWidget: lightweight custom painted map preview displaying coordinates, center pin, accuracy/radius circle, and coordinates badge.
  - - NordicTextInput: styled form input field conforming to Nordic Care styling (rounded corners, focus states, validation message).
  - - StatusStateWidget: unified feedback widgets for Loading (spinner), Error (retry CTA), and Empty states.
  - All widgets follow TỔ ẤM Nordic Care design system tokens (#FAF8F3, #416448, #D9A441, #2F3A34).
  - flutter analyze clean (0 issues).
  - flutter test passes all existing and new widget tests.
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.
  - Tick MOB-BASE-04 in .spec/plan/M3-dispatch-field.md with evidence: <log or PR#> in the same PR.

## #98 [ticket] BE-M3-02 Stepped radius dispatch scanner 5->7->10 km and Economy Freelancer exclusivity
- status: done
- area: -
- blocked-by: BE-M3-01
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - Unit tests verify Agency workers are excluded from Economy service tier.
  - Stepped radius progression works deterministically.
  - verify L3 passes.

## #100 [ticket] BE-M3-03 Offer engine: 30s timeout per worker, atomic accept and slot locking
- status: done
- area: -
- blocked-by: #98
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - Unit tests verify 30s timeout advancement using clock mocking.
  - Atomic accept transitions assignment to ASSIGNED and locks slot.
  - Race conditions safely handled.
  - verify L3 passes.

## #102 [ticket] BE-M3-04 Dispatch exhaustion over 10km cancels order and publishes AssignmentFailed
- status: done
- area: -
- blocked-by: #100
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - Unit tests verify AssignmentFailed publication when candidates exhausted >10km.
  - Order cancellation and 100% refund notification recorded.
  - verify L3 passes.

## #104 [ticket] BE-M3-05 Premium auto-assign Agency workers via Shift Roster and IAgencyCapacityService
- status: done
- area: -
- blocked-by: #102
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - Unit tests verify Premium auto-assignment using IAgencyCapacityService.
  - verify L3 passes.

## #106 [ticket] BE-M3-10 Field check-in verification: GPS <= 100m tolerance and alternative fallback
- status: done
- area: -
- blocked-by: #104
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - Unit tests verify GPS <=100m check-in and fallback methods.
  - verify L3 passes.

## #108 [ticket] BE-M3-06 Customer absent protocol: 15min wait and 2 system calls before report
- status: done
- area: -
- blocked-by: #106
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - Unit tests verify <15m or <2 calls rejection, >=15m & >=2 calls success, and CustomerAbsentReported publication.
  - verify L3 passes.

## #110 [ticket] BE-M3-07 Milestone failover: 30m agency swap or emergency rescue with SLA penalty
- status: done
- area: -
- blocked-by: -
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - verify L3 passes.

## #112 [ticket] BE-M3-08 Dual parallel assignments for houses > 80m2 (BR-02) and fallback
- status: done
- area: -
- blocked-by: -
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - verify L3 passes.

## #114 [ticket] BE-M3-09 Force majeure incident (BR-10), 5m auto redispatch and penalty waiver
- status: done
- area: -
- blocked-by: -
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - verify L3 passes.

## #116 [ticket] BE-M3-11 Comprehensive dispatch test suite (double-assign, 30s, radius, economy, incident)
- status: done
- area: -
- blocked-by: -
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/Tests/Dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - dotnet test Backend/Tests passes.
  - verify L3 passes.

## #118 [ticket] WEB-BASE-02 Login, session and role guard for Admin and Partner (M6 - Anh)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/**
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task WEB-BASE-02. Read .spec/contracts/identity.md sections 1, 2.3-2.6 and decisions Q16 first, plus Frontend/ARCHITECTURE.md (router and API client sections; WEB-BASE-01/03 are merged).
  - Login page at /login for Admin and Partner: email + password + role (Admin | Partner), calling POST /api/auth/password/login through apiClient. Maps errors per contract: 400 field errors under the fields, 401 one generic message (wrong email or password, no hint which), 403 account disabled, 423 locked with the Retry-After time shown in minutes. The password is never logged, never stored, never put in a URL.
  - Session: access token kept in memory only; refresh token kept in sessionStorage (decision of this ticket, because the backend gives no httpOnly cookie: it is cleared when the tab closes, and both choices are readable by XSS, so no token goes to localStorage). The decision and its trade-off are written in Frontend/ARCHITECTURE.md for the leader to confirm or change.
  - Token refresh: on app start with a stored refresh token the app calls POST /api/auth/refresh before showing a protected page; while logged in it refreshes at 80 % of accessTokenExpiresInSeconds; refresh tokens rotate, so only one refresh is in flight at a time and the new refresh token replaces the old one. A failed refresh clears the session and returns to /login.
  - configureApi({ getAccessToken, onUnauthorized }) of WEB-BASE-03 is wired here: Bearer header from memory; onUnauthorized (a 401 on an authenticated request) clears the session and goes to /login.
  - Role guard: /admin/* needs role Admin, /partner/* needs role Partner. No session -> /login?next=<path>; wrong role -> redirected to the area of its own role (no page of the other area is rendered). After login the user lands on next when it is a same-origin path of its area, else on its own area home. next is validated (no external URL, no open redirect).
  - Logout button in the area layout: calls POST /api/auth/logout with the refresh token, clears memory and sessionStorage even if the call fails, goes to /login.
  - Unit tests with node --test (no new package) for the pure logic: session store (inject storage; never writes the access token to storage), expiry and refresh-delay maths, single-flight refresh, access decision per role/area, next validation, login error mapping (names + output pasted).
  - Evidence in a browser against the real local backend (Development seed Admin from Backend/appsettings.Development.json, localhost only): wrong password shows the generic message; correct login lands on /admin; reload keeps the session through refresh; /partner as Admin redirects to /admin; logout returns to /login and /admin redirects to /login; screenshot or page text pasted. No credential is written in the PR.
  - No package beyond what exists (AGENTS.md rule 5). npm run lint, tsc -b and npm test PASS. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Tick WEB-BASE-02 in .spec/plan/M6-ratings-disputes-payouts.md with evidence in the same PR.

## #120 [ticket] BE-M6-01a Two-way rating endpoints and rules, without IWorkerReputation (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Ratings/**
  - Backend/WebAPI/Controllers/Ratings/**
  - Backend/Infrastructure/Modules/Ratings/**
  - Backend/Tests/Ratings/**
  - Backend/Domain/Entities/*.Ratings.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/ratings.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-01, **endpoints and rules only**. Read .spec/contracts/ratings.md (merged in PR #64), .spec/decisions.md Q14 and SC-5, .spec/contracts/identity.md section 1.
  - **Out of scope on purpose:** the real IWorkerReputation (BE-M6-01 second half). Its successRate formula is not decided by the PRD or decisions (contract open question M3; the port only says "share of accepted jobs that ended COMPLETED"), so it waits for the leader's answer and gets its own ticket. WORKER.rating_avg stays M4's (contract M2).
  - Four endpoints exactly as in contract section 2: GET /api/customers/me/assignments/{assignmentId}/rating-window, POST /api/customers/me/assignments/{assignmentId}/rating (policy CustomerOnly); GET /api/workers/me/assignments/{assignmentId}/rating-window, POST /api/workers/me/assignments/{assignmentId}/rating (policy WorkerOnly). The caller id always comes from ICurrentUser.
  - Rules (all from Q14/SC-5): stars 1-5; criteria keys are exactly punctuality, cleaningQuality, attitude (customer) or cooperation, workingConditions (worker), each integer 1-5, stored snake_case in criteria_json (cleaning_quality, working_conditions); comment at most 500 chars (drawio NVARCHAR(500)); the assignment must be COMPLETED and now <= completed_at + 48 h (Rating.WindowHours from BusinessRules); one rating per (assignment, rater role): a second one and the loser of a race get 409; an assignment that is not the caller's is 404; validation errors are 400 with data.errors (decision O5). rater_role values CUSTOMER / WORKER (contract M1). worker_id is copied from the assignment.
  - RatingSubmitted (existing event record) is published after the row is saved. RatingWindow.reason is one of OPEN, NOT_COMPLETED, WINDOW_CLOSED, ALREADY_RATED and never says anything about ownership.
  - A worker-to-customer rating is never returned to the customer or listed to the worker (Q14: internal only).
  - Align .spec/contracts/ratings.md with the code that exists: RatingSubmitted has no workerId (record in Backend/Domain/Events), and set its header status to "merged by the leader in PR #64". No other contract file is touched.
  - Tests (names + output pasted): every rule above including the 48 h boundary (inclusive/exclusive stated), role keys, the 409 race on the unique index against the local SQL Server (skipped only when the database is missing, like Backend/Tests/Persistence), 404 for another caller's assignment, 401/403 policy attributes, and that no endpoint of this ticket exposes a worker-to-customer rating to the customer.
  - Regenerate .spec/contracts/openapi.json with the OpenApiUpdateHelper test; OpenApiSnapshotTests must pass.
  - No package, entity or schema change (AGENTS.md rule 5; schema is M1's). Document the module in Backend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-01 (IWorkerReputation is open): add a note "endpoints done (ticket #<this>, PR #<pr>)" to its line in .spec/plan/M6-ratings-disputes-payouts.md.

## #121 [ticket] BE-M6-09b Admin audit-log read endpoint (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Admin/**
  - Backend/WebAPI/Controllers/Admin/**
  - Backend/Infrastructure/Modules/Admin/**
  - Backend/Tests/Admin/**
  - Backend/Domain/Entities/*.Admin.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/admin.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-09, **the audit-log read endpoint only**. Read .spec/contracts/admin.md section 2.4 (merged in PR #64) and decisions G-5 / SC-3. The Super-Freelancer approve/revoke stay open (they need a port that does not exist, contract A7).
  - GET /api/admin/audit-logs?entityType=&entityId=&actorType=&from=&to=&page=&pageSize= (policy AdminOnly) returns { items: AuditLogEntry[], page, pageSize, total }, newest first (changed_at descending, then log_id descending). Filters are optional and combine with AND; from/to are ISO-8601 UTC instants (from inclusive, to exclusive); pageSize default 20, max 100, page from 1; invalid values are 400 with data.errors (decision O5). 401 without a token, 403 for a non-Admin role.
  - Read-only: no POST/PUT/DELETE/PATCH action exists on audit logs (a test checks the controller's actions). The query never tracks entities (AsNoTracking).
  - AuditLogEntry fields as in the contract (logId, actorType, adminId, entityType, entityId, fieldName, oldValue, newValue, reason, changedAt), actorType as ADMIN / SYSTEM, camelCase, UTC.
  - Tests (names + output pasted): paging maths, ordering, each filter and their combination, the 100 cap, 400 cases, 401/403 attributes, no write actions; DB-backed tests use the local SQL Server and delete only the rows they insert (skipped only when the database is missing).
  - Regenerate .spec/contracts/openapi.json (OpenApiUpdateHelper); OpenApiSnapshotTests must pass. Set the header status of .spec/contracts/admin.md to "merged by the leader in PR #64".
  - No package, entity or schema change (AGENTS.md rule 5). Document in Backend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-09; add a note "audit-log read endpoint done (ticket #<this>, PR #<pr>)" on its line.

## #124 [ticket] BE-M6-06a Admin profile endpoint GET /api/admin/me (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Admin/**
  - Backend/WebAPI/Controllers/Admin/**
  - Backend/Infrastructure/Modules/Admin/**
  - Backend/Tests/Admin/**
  - Backend/Domain/Entities/*.Admin.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/admin.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-06, **GET /api/admin/me only** (contract admin.md section 2.1, merged in PR #64). The operations dashboard metrics stay open (contract A2: the 6 h threshold and the "completed today" period are the designer's numbers, not decided; A6: reading other modules' tables needs a port). Admin account management endpoints are not added (contract A1 recommended default: none in the MVP).
  - GET /api/admin/me (policy AdminOnly) returns AdminProfile { adminId, email, fullName, adminRole, isActive, createdAt } for the caller. The id comes from ICurrentUser, never from the URL. password_hash, failed_login_count and locked_until are never returned. 404 only if the row was removed; 401 without a valid token; 403 for a non-Admin role.
  - Tests (names + output pasted): the mapping shows exactly the six fields and nothing sensitive (reflection on the DTO), 404 for a missing row, the id used is the token's, policy attribute and GET-only, and against the local SQL Server a real ADMIN row is read back (the test inserts and deletes its own row; skipped only when the database is missing). Run the suite WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT, which silently skips the DB tests (Backend/ARCHITECTURE.md 7.1).
  - Regenerate .spec/contracts/openapi.json (OpenApiUpdateHelper); OpenApiSnapshotTests must pass.
  - No package, entity or schema change (AGENTS.md rule 5). Document in Backend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-06; add a note "GET /api/admin/me done (ticket #<this>, PR #<pr>)" on its line.

## #126 [ticket] BE-M6-01b Real IWorkerReputation (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Ratings/**
  - Backend/Infrastructure/Modules/Ratings/**
  - Backend/Tests/Ratings/**
  - Backend/Domain/Entities/*.Ratings.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/ratings.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-01, the IWorkerReputation half (the endpoints are ticket #120). The port already exists (Backend/Application/Interfaces/Ports/IWorkerReputation.cs) and is unchanged; this ticket replaces its Fake with the real implementation for M6 to own (overview section 4).
  - Why this is not blocked: the formula of SuccessRate is the recommended default already written in the merged contract (ratings.md section 4, M3) and in the port's own comment ("share of accepted jobs that ended COMPLETED"). It is implemented in ONE named function with its definition in the XML doc and Backend/ARCHITECTURE.md, so changing it later is a one-line change; the PR states it as an assumption for the reviewer.
  - GetAsync(workerId): null when the worker does not exist; RatingAvg = average stars of the CUSTOMER ratings of the worker rounded to 2 decimals away from zero (G-2), 0 when none; CompletedJobs = number of the worker's assignments in COMPLETED; SuccessRate = completed / (completed + CANCELLED_BY_WORKER) over the worker's assignments, rounded to 3 decimals, 0 when the denominator is 0 (customer-caused ABSENT, INCIDENT, REASSIGNED, system CANCELLED and still-open assignments are not counted against the worker).
  - Read-only, AsNoTracking, at most 3 queries per call. Registered by a module (WorkerReputationModule, its own IModule so it does not collide with RatingsModule of PR #123) so it wins over FakeWorkerReputation.
  - Tests (names + output pasted): the formula on an in-memory data source for every status, rounding at the .xx5 boundary, empty worker, unknown worker; against the local SQL Server a real worker with assignments and ratings (the test inserts and deletes its own rows). Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT (it silently skips DB tests).
  - No package, entity, schema or port change (AGENTS.md rule 5). Document in Backend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Updating the IWorkerReputation block of .spec/contracts/ratings.md is deferred until PR #123 is merged (it rewrites the same block; editing it in two PRs would conflict) and is then a one-line follow-up inside the next ratings ticket. Do NOT tick BE-M6-01; the note on its plan line is added in the follow-up after PR #123 merges (that PR edits the same line).

## #128 [ticket] BE-M6-09c Super-Freelancer approve, revoke and auto-revoke (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Admin/**
  - Backend/WebAPI/Controllers/Admin/**
  - Backend/Infrastructure/Modules/Admin/**
  - Backend/Tests/Admin/**
  - Backend/Domain/Entities/*.Admin.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/admin.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-09, the Super-Freelancer half (the audit log is #70/#121). Read .spec/decisions.md Q12 and G-5, .spec/contracts/admin.md section 2.5 (merged in PR #64).
  - Why this is not blocked: the plan gives M6 the is_super_freelancer flag explicitly ("Admin duyệt/thu hồi is_super_freelancer"), Worker.IsSuperFreelancer has a public setter, the thresholds already exist in BusinessRules.SuperFreelancer (4.80 / 50 / 180 / 4.70), and the rating numbers come through the existing port IWorkerReputation. No new port, entity or schema is needed (contract question A7 proposed a command port; this ticket does not use one).
  - POST /api/admin/workers/{workerId}/super-freelancer body { "reason": "..." } (policy AdminOnly): 200 { workerId, isSuperFreelancer: true } when Q12 holds: rating average >= 4.80 and completed jobs >= 50 (from IWorkerReputation), KYC approved (WORKER.kyc_status, see assumption below), no resolved dispute with fault_party = FREELANCER for an order of this worker in the last 180 days (DISPUTE_TICKET.resolved_at). Otherwise 409 with data.failedCriteria listing every failing one of RATING_BELOW_MINIMUM, COMPLETED_JOBS_BELOW_MINIMUM, KYC_NOT_APPROVED, UPHELD_DISPUTE_RECENT; an AGENCY_STAFF worker is 409 with failedCriteria = ["NOT_FREELANCER"]; unknown worker 404; empty or longer-than-255 reason is 400 (data.errors).
  - DELETE /api/admin/workers/{workerId}/super-freelancer body { "reason": "..." }: revokes (200 { workerId, isSuperFreelancer: false }), same 404/400 rules. Approving an already-super worker or revoking a non-super one is a 200 that changes nothing and writes no audit row (idempotent).
  - Every change writes ONE ADMIN_AUDIT_LOG row in the same unit of work through IAuditLog (actor ADMIN with the caller's id, entity_type = WORKER, entity_id, field_name = is_super_freelancer, old/new false/true, the reason) - the flag and the audit row commit or roll back together.
  - Automatic revoke (Q12): a handler of RatingSubmitted (CUSTOMER ratings only) reads the worker's IWorkerReputation and, when the worker is super and the average is below RevokeBelowRating (4.70), clears the flag and writes an audit row with actor SYSTEM, no admin id and reason "rating below 4.70". Exactly 4.70 does not revoke. A null reputation (unknown worker) does nothing.
  - Assumption stated for the reviewer: the value of kyc_status that means "approved" is not defined anywhere (decisions Q05 only says "KYC approved"); this ticket uses one constant KycApprovedStatus = "APPROVED" compared case-insensitively, in one place, so M4 can align it.
  - Tests (names + output pasted): approve success, each criterion failing alone and all together, the 180-day boundary on the real database (179 days counts, 181 does not, an Agency-fault dispute and another worker's order do not), not-a-freelancer, 404, 400, idempotency, revoke, audit rows (fields and same transaction on the real database), auto-revoke at 4.69 / 4.70 / non-super / WORKER rating / null reputation, controller policy and routes. Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Regenerate .spec/contracts/openapi.json; OpenApiSnapshotTests must pass. No package, entity or schema change (AGENTS.md rule 5). Document in Backend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-09 (the audit endpoint PR may still be open); add a note on its line.

## #130 [ticket] BE-M6-02a Disputes: filing, own lists, admin queue and take (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Disputes/**
  - Backend/WebAPI/Controllers/Disputes/**
  - Backend/Infrastructure/Modules/Disputes/**
  - Backend/Tests/Disputes/**
  - Backend/Domain/Entities/*.Disputes.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/disputes.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-02, **filing, own lists, admin queue and take** (the verdict/resolve is ticket BE-M6-02b). Read .spec/contracts/disputes.md (merged in PR #64), .spec/spec.md section 4.3 and decisions Q09/Q10/Q22 D3 first.
  - Why this is not blocked: filing, listing and taking use only the existing DISPUTE_TICKET, JOB_ORDER, JOB_ASSIGNMENT (flat shared node), the Customer/Worker/PartnerAgency name columns and the existing ports; the open questions D1-D9 each have a recommended default in the merged contract and are applied as one options class (DisputeOptions: file window 24 h, SLA 48 h, priority thresholds 6 h / 24 h, near-SLA 6 h) and one constant list of categories, so the team can change them in one place. The PR lists every default used as an assumption.
  - **Schema fact that overrides the contract:** DISPUTE_TICKET has a UNIQUE index on order_id (DisputeTicketConfiguration.cs:17), so an order can have only ONE ticket in total. A second filing for the order (by either side) is a 409; contract question D5 ("one unresolved per order and raiser") is therefore replaced by "one per order". The contract text is corrected in this ticket.
  - Endpoints (contract section 2.1-2.3 without resolve): POST /api/customers/me/disputes and POST /api/workers/me/disputes ({ orderId, category, description, evidenceUrls[] }, 201), GET .../disputes and GET .../disputes/{id} for both roles (own tickets only, 404 otherwise), GET /api/admin/disputes?status=&priority=&nearSla=&page=&pageSize= (queue, DisputeSummary items), GET /api/admin/disputes/{disputeId} (case file), POST /api/admin/disputes/{disputeId}/take (policies CustomerOnly / WorkerOnly / AdminOnly; ids from ICurrentUser).
  - Filing rules: caller must be on the order (customer owns it / worker has an assignment on it) else 404; 400 for a blank or over-1000-character description, a category outside QUALITY, ATTITUDE, PROPERTY_DAMAGE, ABSENT_FEE, OTHER, no evidence or more than 10 entries, an entry that is blank or over 500 characters; 409 when no assignment of the order reached AWAITING_ACCEPTANCE, COMPLETED or ABSENT, when now > max(completed_at, shift end) + 24 h, or when a ticket already exists for the order (also the loser of a race on the unique index). Effects: status OPEN, sla_due_at = created_at + 48 h, evidence_urls stored as a JSON array, raised_by from the role.
  - Admin queue: default status OPEN,IN_REVIEW, sorted by sla_due_at ascending, pageSize 1-100 (default 20, over 100 is a 400), priority derived (never stored): HIGH under 6 h left, MEDIUM under 24 h, else LOW; nearSla=true = unresolved and due within 6 h (overdue included); items carry order code, customer name, the workers of the order with type and agency name, slaSecondsRemaining (negative when overdue). Case file adds the shift timeline (check-in rows, customer-absent, after-photos with VoL, the dispute filing, completion) and the photos from CHECK_IN_LOG / JOB_PHOTO; the checklist has no data source and is returned as null.
  - Take: OPEN -> IN_REVIEW and records the admin in resolved_by as the handler (contract wording); 409 when not OPEN; 404 when missing.
  - Evidence files: no upload endpoint exists in the API yet (IFileStorage has no controller), so evidenceUrls are accepted as text; the PR states this dependency.
  - Tests (names + output pasted): every rule above on an in-memory repository, the 24 h window boundaries, priority thresholds, the unique-index race (6 parallel filings -> 1 x 201, 5 x 409) and the queue/detail/take flow against the local SQL Server (seeded rows removed afterwards), controllers' policies/routes/verbs. Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Regenerate .spec/contracts/openapi.json; OpenApiSnapshotTests pass. No package, entity or schema change (AGENTS.md rule 5). Document in Backend/ARCHITECTURE.md; correct .spec/contracts/disputes.md (D5, evidence). sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-02; add a note.

## #132 [ticket] BE-M6-02b Disputes: resolve verdict (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Disputes/**
  - Backend/WebAPI/Controllers/Disputes/**
  - Backend/Infrastructure/Modules/Disputes/**
  - Backend/Tests/Disputes/**
  - Backend/Domain/Entities/*.Disputes.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/disputes.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-02, **the verdict** (POST /api/admin/disputes/{disputeId}/resolve). Continues ticket #130 / PR #131 (filing, queue, take); **this branch is stacked on the #131 branch** because the verdict needs its repository and DTOs. Read .spec/contracts/disputes.md section 2.3, spec 4.3, decisions Q09 / Q10 / Q22 D3 / G-2 / G-5.
  - Why this is not blocked: it uses only the existing ports IRefundService, ISlaPenaltyService, IAuditLog, IUnitOfWork, IClock, the existing event DisputeResolved and the existing table DISPUTE_TICKET (+ read-only JOB_ASSIGNMENT). No new port, package, entity or schema change.
  - Body { faultParty: FREELANCER | AGENCY | CUSTOMER | null, compensationAmount, lockWorker, note }. 400 (field errors, decision O5): note blank or over 255; amount negative, fractional or above the sum of gross_amount of the order's assignments; lockWorker without FREELANCER; faultParty null/CUSTOMER with an amount above 0; FREELANCER with no freelancer assignment or AGENCY with no agency assignment on the order. 404 missing; 409 already RESOLVED/DISMISSED (the claim is one conditional UPDATE, so two admins deciding at once get one 200 and one 409).
  - Effects in ONE DB transaction (rolled back when a port fails -> 502): ticket to RESOLVED (DISMISSED when faultParty is null) with fault_party, compensation_amount, resolved_by, resolved_at (IClock); FREELANCER/AGENCY: IRefundService.RefundAsync for the amount; AGENCY (not ABSENT_FEE): ISlaPenaltyService.ApplyAsync(QualityComplaint) per agency with its share (rescue cost 0), none for ABSENT_FEE (D9); one ADMIN_AUDIT_LOG row per verdict (entity DISPUTE_TICKET, field dispute_status, reason = note), plus one worker_lock_requested row when lockWorker. After commit DisputeResolved is published once per assignment of the order (the event is per assignment; the amount is split by gross share, remainder on the last).
  - Known gaps, stated in the PR, not hidden: the worker account lock belongs to M4 (BE-M4-01) and the real event has no lockWorker field, so only the request is recorded in the audit log; the payout deduction is read from the resolved ticket by BE-M6-04 (no table written here).
  - Tests (names + output pasted): every 400/404/409 rule, each fault party's effects with fake ports (refund called once with the amount, SLA request per agency, no SLA for ABSENT_FEE), failing refund rolls back (502, ticket unchanged), share split, event per assignment, 6 parallel resolves -> 1 x 200 and 5 x 409 against the local SQL Server (seeded rows removed), controller policy/route. Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Regenerate .spec/contracts/openapi.json; document in Backend/ARCHITECTURE.md; update .spec/contracts/disputes.md (event shape, 502, rules above). sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-02 until Kiệt has merged and decided the open questions; add a note.

## #134 [ticket] BE-M6-03 Admin absence approval (BR-05) (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Admin/**
  - Backend/WebAPI/Controllers/Admin/**
  - Backend/Infrastructure/Modules/Admin/**
  - Backend/Tests/Admin/**
  - Backend/Domain/Entities/*.Admin.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/admin.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-03 (BR-05, decision Q10). Read .spec/contracts/admin.md section 2.2 (merged in PR #64), spec 4.2 / BR-05, decisions Q10 / G-2 / G-5 first. Dependency BE-M3-06 (CustomerAbsentReported, CHECK_IN_LOG.customer_absent_at) is on main (CustomerAbsentService.cs).
  - Why this is not blocked: it reads CHECK_IN_LOG / JOB_ASSIGNMENT (flat shared node) read-only for the queue, writes only JOB_ASSIGNMENT (status via the state machine, absence_fee_amount) and the audit log, and uses the existing ports IRefundService, IAuditLog, IUnitOfWork, IClock, the existing event CustomerAbsentApproved and BusinessRules.Absence (rate 0.40, 15 min, 2 calls). No new port, package, entity or schema change.
  - Endpoints (AdminOnly), contract 2.2: GET /api/admin/absence-reports?status=&page=&pageSize= (default PENDING, oldest customerAbsentAt first, pageSize 1-100), GET /api/admin/absence-reports/{assignmentId}, POST .../approve, POST .../reject {reason}. Status is derived (A3): PENDING = customer_absent_at set, assignment not ABSENT, no rejection row; APPROVED = assignment ABSENT; REJECTED = audit row (JOB_ASSIGNMENT, absence_report).
  - Approve: 409 with blockReasons unless GPS verified, call_attempts >= 2, >= 15 min since checked_in_at and customer_absent_at set (Q10); 409 if already decided or the assignment is not CHECKED_IN. One transaction: ONE conditional UPDATE (assignment_status = CHECKED_IN -> ABSENT, absence_fee_amount = Vnd.Round(gross x 0.40)) so two admins give one 200 and one 409; IRefundService for gross - fee (the 60 %); one audit row; after commit CustomerAbsentApproved. A refused refund -> 502 and a full rollback.
  - Reject: reason 1-255 else 400; 409 if decided; audit row only (A4: the assignment is left alone).
  - Tests (names + output pasted): each Q10 condition separately and together, fee rounding, the state machine transition, the 60 % refund, rollback on a refused refund, reject rules, statuses and paging of the queue, controller policy/routes; SQL Server: approve flow, 6 parallel approvals -> 1 x 200 and 5 x 409, rollback (rows removed afterwards). Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Regenerate .spec/contracts/openapi.json; document in Backend/ARCHITECTURE.md; correct .spec/contracts/admin.md where the real event (CustomerAbsentApproved fields) differs. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-03 until reviewed; add a note.

## #136 [ticket] BE-M6-04 Payouts: monthly batch, read and confirm (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Payouts/**
  - Backend/WebAPI/Controllers/Payouts/**
  - Backend/Infrastructure/Modules/Payouts/**
  - Backend/Infrastructure/Modules/Disputes/**
  - Backend/Tests/Payouts/**
  - Backend/Domain/Entities/*.Payouts.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/payouts.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-04 (monthly payout batch, spec 4.4 step 3 / 5.2, decisions Q10 / Q11 / G-1 / G-2 / G-3). Read .spec/contracts/payouts.md sections 1, 2.1, 2.2, 2.4 and 4 (merged in PR #64) first.
  - Why this is not blocked: it reads JOB_ASSIGNMENT (the flat node, "0 JOIN" by the plan itself), writes only PAYOUT_BATCH, PAYOUT_ITEM and JOB_ASSIGNMENT.payout_item_id, and uses existing IClock, IUnitOfWork, IAuditLog, Vnd and the existing event PayoutBatchClosed. The pending deductions (question P1) come through ONE interface owned by Payouts (IPayoutPenaltySource) with its implementation in the Disputes folder reading resolved DISPUTE_TICKET rows, so no M1 port or schema change is needed. Open questions P2-P7 use the recommended defaults of the merged contract, flagged in the PR.
  - Endpoints (AdminOnly): POST /api/admin/payout-batches {periodMonth} (201 new / 200 existing DRAFT rebuilt, 400 bad or unfinished month, 409 CLOSED), GET /api/admin/payout-batches?page=&pageSize= (newest month first), GET /api/admin/payout-batches/{batchId}?payeeType=&page=&pageSize= (items + warnings for payees without bank data), POST /api/admin/payout-batches/{batchId}/confirm (CLOSED, items TRANSFERRED, PayoutBatchClosed; 409 when closed, empty or the month is not over).
  - Aggregation: COMPLETED assignments with completed_at in the month in Asia/Ho_Chi_Minh and payout_item_id IS NULL; agency_id null -> one FREELANCER item per worker, else one AGENCY item per agency; commission = sum of Vnd.Commission(gross, frozen rate) per assignment; approved absence fees (status ABSENT, absence_fee_amount > 0, dated by updated_at) count as gross with no commission (Q10); net = gross - commission - penalty, never below 0, the rest of a penalty carries to the next batch (P3), computed as "decided through the month end minus penalty already applied in earlier CLOSED batches", so it needs no extra column and a rebuild gives the same numbers; each included assignment gets payout_item_id; a DRAFT rebuild first releases and replaces its items; two simultaneous builds are serialised with an application lock.
  - Tests (names + output pasted): the pure calculation (freelancer/agency split, rounding per assignment, absence fee, penalty cap and carry), month bounds in Asia/Ho_Chi_Minh, validation (400/409), idempotent rebuild, confirm rules and event, paging, controller policy/routes; SQL Server: build -> rebuild -> confirm, CLOSED stays immutable, 4 parallel builds -> one batch with correct totals (rows removed afterwards). Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Regenerate .spec/contracts/openapi.json; document in Backend/ARCHITECTURE.md; correct .spec/contracts/payouts.md where the real event or the stated defaults differ. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-04 until reviewed; add a note. Export (BE-M6-05) and worker income (BE-M6-07) are separate tickets.

## #138 [ticket] BE-M6-05 Payouts: bank transfer export .xlsx (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Payouts/**
  - Backend/WebAPI/Controllers/Payouts/**
  - Backend/Infrastructure/Modules/Payouts/**
  - Backend/Tests/Payouts/**
  - Backend/Domain/Entities/*.Payouts.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/payouts.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-05 (bank transfer export, decision Q18). Read .spec/contracts/payouts.md section 2.3 and question P5. **Stacked on PR #137** (BE-M6-04, issue #136): the export reads its batches and items, so this branch is based on the #136 branch and the PR targets it.
  - Why this is not blocked: .xlsx is a zip of XML parts, so it is written with System.IO.Compression and System.Xml, both in the .NET base library: **no new package** (AGENTS.md rule 5). IFileStorage and PAYOUT_BATCH.export_file_url already exist. Column sets: Q18 for the freelancer file, the recommended defaults of P5 for the two agency files, flagged in the PR.
  - Endpoint (AdminOnly): GET /api/admin/payout-batches/{batchId}/export?type=freelancer|agency-summary|agency-detail returns the file (application/vnd.openxmlformats-officedocument.spreadsheetml.sheet, Content-Disposition: attachment); the only non-envelope response; errors keep the envelope (400 unknown or missing type, 404 missing batch). Works for DRAFT and CLOSED batches. freelancer: full_name, bank_name, bank_account_no, net_amount, transfer_note (period in the note); agency-summary: agency_name, bank_name, bank_account_no, job_count, gross_amount, commission_amount, penalty_amount, net_amount, transfer_note; agency-detail: assignment_id, order_id, completed_at, worker_name, gross_amount, commission_amount, net_amount. Account numbers are written as text (leading zeros survive), amounts as numbers; payees with no bank account stay listed with an empty cell (P4); payees whose net is 0 are left out of the two transfer files (nothing to transfer).
  - The file is also stored through IFileStorage (folder payouts) and its url written to export_file_url; the previously stored export of the same batch is deleted so files do not pile up.
  - Tests (names + output pasted): the writer (zip parts, content types, XML escaping, text vs number cells, a file read back part by part), each export type's rows and columns, empty and bank-less cases, validation (400/404), storage and url update and replacement, controller policy/route/file response; SQL Server: an export over a real batch (rows removed afterwards). Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Regenerate .spec/contracts/openapi.json; document in Backend/ARCHITECTURE.md; update .spec/contracts/payouts.md 2.3 with what is built. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-05; add a note.

## #140 [ticket] BE-M6-07 Payouts: worker monthly income and payout history (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Payouts/**
  - Backend/WebAPI/Controllers/Payouts/**
  - Backend/Infrastructure/Modules/Payouts/**
  - Backend/Infrastructure/Modules/Disputes/**
  - Backend/Tests/Payouts/**
  - Backend/Domain/Entities/*.Payouts.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/payouts.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-07 (worker income, decision Q11). Read .spec/contracts/payouts.md section 2.5 and question P6. **Stacked on PR #137** (BE-M6-04, issue #136): it reuses PayoutCalculator, the batch tables and IPayoutPenaltySource, so this branch is based on the #136 branch and the PR targets it.
  - Why this is not blocked: it reads the caller's own JOB_ASSIGNMENT rows and the batches built by BE-M6-04 and uses the same arithmetic; no new port, package, entity or schema change.
  - Endpoints (WorkerOnly, worker id from ICurrentUser): GET /api/workers/me/earnings?month=YYYY-MM (default the current month in Asia/Ho_Chi_Minh, a future month or a bad format is 400, an AGENCY_STAFF worker is 403 because the agency is paid, PRD 4.4 step 3) answering periodMonth, jobCount, grossAmount, commissionAmount, penaltyAmount, netAmount, payoutStatus NOT_BUILT|PENDING|TRANSFERRED, jobs[] (each assignmentId, orderId, completedAt, grossAmount, commissionAmount, netAmount, absenceFee); GET /api/workers/me/payouts?page=&pageSize= the worker's own items of CLOSED batches, newest month first (batchId, periodMonth, netAmount, itemStatus, transferredAt; pageSize 1-100).
  - Rules: computed with PayoutCalculator from the worker's own COMPLETED assignments and approved absence fees in the month (Asia/Ho_Chi_Minh), whether or not a batch paid them yet; when the month's batch already holds an item for the worker, that item's numbers are the answer so the screen equals the money; payoutStatus is NOT_BUILT without a batch, PENDING for a DRAFT, TRANSFERRED once CLOSED; the pending penalty is taken as in the batch (decided before the month end minus applied in earlier CLOSED batches, capped by the month's payable amount). Only this worker's rows are read from the database through a new repository interface of its own (IWorkerEarningsRepository), so the interfaces of #137 stay untouched; the penalty source is asked for everyone and the worker's key is picked from the answer.
  - Tests (names + output pasted): the calculation for the month incl. absence fee and penalty, month bounds, default and future month, 403 for agency staff, payoutStatus for none/DRAFT/CLOSED, item numbers winning over a recomputation, history paging and ownership (another worker's items never appear), controller policy/routes; SQL Server: a worker with a DRAFT then a CLOSED batch, another worker's data excluded (rows removed afterwards). Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Regenerate .spec/contracts/openapi.json; document in Backend/ARCHITECTURE.md; update .spec/contracts/payouts.md 2.5 with what is built. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-07; add a note.

## #142 [ticket] BE-M6-06b Admin: operations dashboard metrics (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Admin/**
  - Backend/WebAPI/Controllers/Admin/**
  - Backend/Infrastructure/Modules/Admin/**
  - Backend/Tests/Admin/**
  - Backend/Domain/Entities/*.Admin.cs
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
  - .spec/contracts/admin.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-06, the **operations dashboard metrics** (the admin profile is BE-M6-06a, PR #125). Read .spec/contracts/admin.md section 2.3 and question A2 (merged in PR #64).
  - Why this is not blocked: counting rows needs no new rule. It reads JOB_ORDER, JOB_ASSIGNMENT and DISPUTE_TICKET read-only (contract question A6: no read port exists yet; the same read-only approach as the other M6 tickets) and the only threshold, Admin.DisputeNearSlaHours (default 6), is one options class so the team can change it in one place. The definitions of A2 are the contract's recommended defaults and are flagged in the PR: the leader has not confirmed them.
  - Endpoint (AdminOnly): GET /api/admin/dashboard -> { generatedAt, orders: { today, thisWeek }, shifts: { inProgress, completedToday }, disputes: { open, nearSla } } and nothing else (G-7: no extra metric). Definitions: orders by JOB_ORDER.created_at in the current day and ISO week (Monday start) of Asia/Ho_Chi_Minh; shifts in progress = assignments CHECKED_IN, IN_PROGRESS, AWAITING_ACCEPTANCE; completed today = COMPLETED with completed_at in the current Ho Chi Minh day; disputes open = OPEN + IN_REVIEW; near SLA = open with sla_due_at within the configured hours (overdue included).
  - Tests (names + output pasted): the day/week windows around midnight and Sunday/Monday in Asia/Ho_Chi_Minh (including UTC dates that are already the next local day), every status counted or not, near-SLA boundary, only the three groups in the response, controller policy/route; SQL Server: counts computed against deltas of the real tables (rows seeded and removed afterwards). Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Regenerate .spec/contracts/openapi.json; document in Backend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR. Do NOT tick BE-M6-06; add a note.

## #144 [ticket] BE-M6-08 Cross-module acceptance test of the M6 money chain (M6 - Anh)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Tests/Ratings/**
  - Backend/Tests/Disputes/**
  - Backend/Tests/Payouts/**
  - Backend/Tests/Admin/**
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-08 "Test: cửa sổ đánh giá, 1 đánh giá/chiều, hạn khiếu nại 24 h, 40 % vắng mặt, payout đúng 80 %/tổng hợp & chạy lại không trùng" (needs BE-M6-01..07; 01-06 and 09 are on main, 07 is PR #141 and is not needed by these scenarios).
  - Why this is not blocked: it adds tests only, no production code. Each criterion is already covered inside the ticket that built it; this ticket (1) adds ONE cross-module test on the local SQL Server that runs the money chain with the real services and checks that the modules agree with each other, and (2) writes a traceability table (criterion -> test names) in the PR so the leader sees every criterion proven.
  - The chain test (Backend/Tests/Admin/M6AcceptanceTests.cs): a freelancer with two completed jobs and one customer-absence report (real AbsenceReportService: 40 % fee 104000 of 260000, 60 % refund 156000), a resolved dispute against the freelancer with compensation 50000 (real DisputeVerdictService), a second freelancer (80 % net, rounding per assignment) and an agency with two jobs (one aggregated item); then the real PayoutBatchService builds the month: the absence fee is paid with no commission, the penalty from the verdict is deducted, a rebuild changes nothing (same batch id, same items, no assignment counted twice), confirm closes it, a second build is 409. Money moved through IRefundService is asserted too (156000 and 50000). Months far in the future (2088) so no real data is touched; every seeded row is removed afterwards. Run WITHOUT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.
  - Document the test in Backend/ARCHITECTURE.md; add a note on BE-M6-08 in the plan (do not tick before review). sh harness/verify.sh L3 PASSES; paste the last line and a log excerpt in the PR.

## #146 [ticket] WEB-M6-04 Admin operations dashboard page (M6 - Anh)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/src/features/admin/**
  - Frontend/src/components/status/**
  - Frontend/src/hooks/**
  - Frontend/src/lib/format.ts
  - Frontend/tests/**
  - Frontend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task WEB-M6-04 (operations dashboard). Contract: .spec/contracts/admin.md 2.3 (endpoint GET /api/admin/dashboard is PR #143, BE-M6-06b, not merged yet) and .spec/contracts/disputes.md 2.2 (the near-SLA list reuses GET /api/admin/disputes?nearSla=true&pageSize=5, on main). Design: Figma 182:2 (frames 182:45/88/131 are the loading, empty and error states), file h7Tmwu071XyOZRUVoAn37o.
  - Why this is not blocked: gate G3 (the contracts) is merged and the endpoints are built (the disputes queue on main, the dashboard in PR #143), so the page calls the real API through apiClient (WEB-BASE-03) with hand-written types from the contract (no OpenAPI client before G4). It touches only its own feature folder, routes.tsx of the admin feature (owned by M6) and small shared pieces outside src/app. Nobody edits src/app/**.
  - The page /admin/dashboard: three stat cards (Đơn: hôm nay / tuần này; Ca làm: đang làm / hoàn tất hôm nay; Tranh chấp tồn: chưa xử lý / sắp quá SLA, the last in red when above 0), then "Tranh chấp sắp quá hạn" with a table (mã khiếu nại #TC-<id>, khách, thợ liên quan with type and agency, SLA còn lại as 01h 45p or "Quá hạn", link "Xem chi tiết →" to /admin/disputes/<id>) and a button "Xem tất cả khiếu nại" to /admin/disputes. States: loading skeleton, empty ("Không có tranh chấp sắp quá hạn"), error with retry; no metric beyond the three groups (G-7). The page is at /admin/dashboard and first in the sidebar (the /admin home placeholder lives in the frozen src/app/router.tsx, which this ticket does not touch).
  - Shared pieces added for the other M6 pages: src/lib/format.ts (VND, local date-time in Asia/Ho_Chi_Minh, SLA countdown, all pure), src/components/status/ (StatCard, StatusBadge) and src/hooks/use-resource.ts (load, error, reload).
  - Tests (output pasted): npm test covers the pure functions (money with the Vietnamese thousands separator and rounding, local time across midnight and the year end, countdown at 0, negative and over a day, the mapping of the response to card values and the red state); npm run lint, npm run build pass. Browser check against a contract stub (the real backend does not start on main: DispatchOfferEngine is unregistered, M3), screenshots described in the PR, including the three states and a 401 on an expired token.
  - Update Frontend/ARCHITECTURE.md (shared pieces and the dashboard). sh harness/verify.sh L3 PASSES; paste the last line and log excerpt. Do NOT tick WEB-M6-04 until reviewed; add a note.

## #148 [ticket] WEB-M6-02 Admin absence approval page (M6 - Anh)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/src/features/admin/**
  - Frontend/src/components/status/**
  - Frontend/src/hooks/**
  - Frontend/src/lib/format.ts
  - Frontend/tests/**
  - Frontend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task WEB-M6-02 (customer-absence approval with calls and GPS). Contract: .spec/contracts/admin.md 2.2 (the four api/admin/absence-reports endpoints are on main since PR #135). Design: Figma 19:3 ("Biên bản vắng mặt 15 phút"), file h7Tmwu071XyOZRUVoAn37o. **Stacked on PR #147** (WEB-M6-04, issue #146), which adds src/lib/format.ts, src/components/status and src/hooks/use-resource.ts that this page uses; this branch is based on the #146 branch and the PR targets it.
  - Why this is not blocked: gate G3 (contracts) is merged and the endpoints exist, so the page calls the real API through apiClient with hand-written types from the contract. It touches only the admin feature folder (owned by M6) and shared pieces outside src/app.
  - The page /admin/absence-reports (nav "Biên bản vắng mặt"): tabs Chờ duyệt / Đã duyệt / Từ chối (status = PENDING / APPROVED / REJECTED), a paged table (order code, worker with Freelancer / Agency · name, customer, reported at in Asia/Ho_Chi_Minh, GPS verified with the distance, number of calls, minutes waited, the 40 % fee, status) and a detail panel for the selected row: worker, agency, customer, door photo when there is one, GPS (verified or not, distance, device coordinates), calls, waiting time, the money split (worker keeps the fee, customer gets the refund), the blockReasons in words (GPS_NOT_VERIFIED, CALLS_BELOW_MINIMUM, WAIT_BELOW_MINIMUM, ABSENCE_NOT_REPORTED). Per-call details of the Figma are not in the contract (question A5), so only the count is shown.
  - Actions (only for PENDING): "Duyệt bồi hoàn 40%" is disabled while canApprove is false and says why; clicking it opens a confirmation line with both amounts ("Thợ nhận X, hoàn Y cho khách") and only "Xác nhận" calls POST .../approve; "Bác yêu cầu" asks for a reason of 1-255 characters (checked before sending, the 400 field message shown otherwise) and calls POST .../reject. A 409 shows its message and, for an approval, the blockReasons it carries; a 502 says the refund was refused and nothing changed; a double click sends one request; after success the list and the panel reload and the row leaves the "Chờ duyệt" tab.
  - Tests (output pasted): npm test covers the pure logic (status labels and tones, block-reason wording, the row mapping, the reason validation at 0, 1, 255 and 256 characters and with spaces only, the money split text, how a 409 with blockReasons, a 400 and a 502 become messages); npm run lint and npm run build pass. Browser check against a contract stub (the real backend does not start on main), including every state and each error status.
  - Update Frontend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and log excerpt. Do NOT tick WEB-M6-02 until reviewed; add a note.

## #149 [ticket] WEB-M6-01 Admin dispute console: queue, case file and verdict (M6 - Anh)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/src/features/disputes/**
  - Frontend/src/components/status/**
  - Frontend/src/hooks/**
  - Frontend/src/lib/format.ts
  - Frontend/tests/**
  - Frontend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task WEB-M6-01 (dispute console: Before/After photo reconciliation, acceptance record, verdict). Contract: .spec/contracts/disputes.md 2.2-2.3 (queue, case file, take and resolve are on main since PRs #131 and #133). Design: Figma 66:2, file h7Tmwu071XyOZRUVoAn37o. **Stacked on PR #147** (WEB-M6-04, issue #146), whose src/lib/format.ts, src/components/status and src/hooks/use-resource.ts it uses; the branch is based on the #146 branch and the PR targets it.
  - Why this is not blocked: gate G3 (contracts) is merged and the endpoints exist, so the pages call the real API through apiClient with hand-written types from the contract. It touches only the disputes feature folder (owned by M6) and shared pieces outside src/app; the shared BeforeAfterViewer (WEB-BASE-04) is reused, not changed.
  - Queue /admin/disputes (nav "Khiếu nại tranh chấp"): chips for priority (Tất cả / Ưu tiên cao / Trung bình / Thấp) and for status (Đang mở / Đã xử lý), a paged table (mã #TC-id, khách, thợ liên quan with type and agency, phân loại with the "Auto-Cancelled Dispute" tag for ABSENT_FEE, ưu tiên, SLA còn lại or "Quá hạn", trạng thái, "Xem chi tiết →"), states loading / empty / error with retry.
  - Case page /admin/disputes/:id: summary; the shift timeline (check-in with GPS, after photos with the VoL score, the dispute filing, completion) and an honest "chưa có checklist" while the API returns null; the customer's description and evidence links; the before/after photos in BeforeAfterViewer (one tab per assignment when the order has several) with the VoL score of each photo; "Nhận xử lý" for an OPEN ticket (POST .../take).
  - Verdict form (only while OPEN or IN_REVIEW): fault party FREELANCER / AGENCY / CUSTOMER / "Không bên nào (bác bỏ)" offered only when the order has such a worker (a freelancer for FREELANCER, an agency worker for AGENCY); compensation in whole VND, only for FREELANCER or AGENCY, with a preview of the refund; "khoá tài khoản" only for FREELANCER with the honest note that the lock is requested and recorded, not yet performed (M4); a note of 1-255 characters; client checks mirror the contract (note, amount whole and not negative, lock only with FREELANCER, amount only with FREELANCER/AGENCY) and the server's 400 field messages are shown under the field; a confirmation step states the effect in words; a double click sends one request; 409 (decided) reloads the case, 502 says the refund was refused and nothing changed. A decided ticket shows its verdict read-only.
  - Differences between the Figma and the merged contract are written in the PR (the Figma offers a "đền bù buổi dọn" and SLA point choices of -2/-5/-10; the contract fixes the refund in money through IRefundService and the -5 of Q09): the contract wins.
  - Tests (output pasted): npm test covers the pure logic (priority/status/category labels, the fault options per order, the verdict validation for each rule and boundary at 0/1/255/256 characters and amounts, the request body, the refund preview, timeline and photo mapping, error mapping for 400/404/409/502); npm run lint and npm run build pass. Browser check against a contract stub (the real backend does not start on main), with the queue, the case page, take, each verdict path and each error status.
  - Update Frontend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and log excerpt. Do NOT tick WEB-M6-01 until reviewed; add a note.

## #152 [ticket] WEB-M6-03 Admin payout batch pages (M6 - Anh)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/src/features/payouts/**
  - Frontend/src/components/status/**
  - Frontend/src/hooks/**
  - Frontend/src/lib/format.ts
  - Frontend/tests/**
  - Frontend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task WEB-M6-03 (payout batch: view, approve, export file, disburse). Contract: .spec/contracts/payouts.md 2.1-2.4 (batch build, read, export and confirm are on main since PRs #137 and #139). There is no Figma frame for this screen (the plan lists none), so it follows the layout language of the other Admin pages and the shared DataTable. **Stacked on PR #147** (WEB-M6-04, issue #146), whose src/lib/format.ts, src/components/status and src/hooks/use-resource.ts it uses; this branch is based on the #146 branch and the PR targets it.
  - Why this is not blocked: gate G3 (contracts) is merged and the endpoints exist, so the pages call the real API through apiClient with hand-written types from the contract. It touches only the payouts feature folder (owned by M6).
  - List /admin/payout-batches (nav "Kỳ payout"): batches newest month first (month, status Nháp / Đã giải ngân, items, total, confirmed at), paged, with a "Dựng kỳ" control: a month chosen from the finished months only (the contract refuses the running month) and a button that calls POST /api/admin/payout-batches; a 201 (new) or 200 (rebuilt) opens the batch, a 409 (closed) and a 400 are explained.
  - Detail /admin/payout-batches/:batchId: header (month, status, total, items, who and when for a closed batch); the warnings of payees without a bank account in an amber box; the items in a paged DataTable with the payee-type chips (Tất cả / Freelancer / Agency) and the columns payee, jobs, gross, commission, penalty, net, bank, account, status; export buttons for the three files of the contract (freelancer, agency-summary, agency-detail) that download the .xlsx with the Admin's token (the file is not JSON, so it is fetched as a blob and saved with the server's file name; an error answer keeps the envelope and its message is shown); for a DRAFT batch "Dựng lại" (rebuild) and "Xác nhận giải ngân" with a confirmation that states the month, the number of payees and the total and says plainly that no bank is called from the system (decision G-1): the Admin records that the transfers were made from the exported file. A CLOSED batch is read-only. A double click sends one request; 409 (closed, empty or the month is not over) is explained and the batch reloads.
  - Tests (output pasted): npm test covers the pure logic (the finished months around the Asia/Ho_Chi_Minh month boundary and the year end, status and payee labels, row mapping with money, the confirmation text, error mapping for 400/404/409, the file name and the Content-Disposition parsing, the error read from a non-OK file response); npm run lint and npm run build pass. Browser check against a contract stub (the real backend does not start on main), including every state, a file download and each error status.
  - Update Frontend/ARCHITECTURE.md. sh harness/verify.sh L3 PASSES; paste the last line and log excerpt. Do NOT tick WEB-M6-03 until reviewed; add a note.

## #154 [ticket] MOB-M6-01 + MOB-M6-02 Two-way rating screen (M6 - Anh)
- status: done
- area: mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/ratings/**
  - Mobile/test/features/ratings/**
  - Mobile/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md tasks **MOB-M6-01** (customer rates the worker) and **MOB-M6-02** (worker rates the customer), one ticket because they are the same screen with two roles. Contract: .spec/contracts/ratings.md sections 1, 2.1, 2.2 (the four endpoints are on main since PR #123). Decision Q14 (48 h window, fixed criteria, internal-only worker rating, no editing).
  - Why this is not blocked: gate G3 (contracts) is merged and the endpoints exist, so the screen uses the shared ApiClient (MOB-BASE) with models written from the contract. It touches only Mobile/lib/features/ratings/ (M6's folder); lib/app/** stays frozen.
  - Screen (route /ratings, arguments RatingScreenArgs(role, assignmentId, ratedName?); whoever opens it after a completed job passes the arguments, and without them the screen says there is no job to rate): loads GET .../assignments/{id}/rating-window for the role (customers/me or workers/me); while canRate it shows the countdown ("Còn 47 giờ 12 phút"), overall stars 1-5, the fixed criteria of the role (customer: punctuality, cleaningQuality, attitude; worker: cooperation, workingConditions), each 1-5, an optional comment up to 500 characters with a counter, and a send button; for the other reasons it says plainly why (NOT_COMPLETED, WINDOW_CLOSED, ALREADY_RATED). A worker rating is labelled "chỉ dùng nội bộ". The send button is disabled until every score is chosen, one request per tap, and the answer 201 shows a thank-you state; 400 field messages show under their field, 404 "không tìm thấy ca làm", 409 explains (already rated, window closed, not completed) and reloads the window, a network error offers a retry. Ratings cannot be edited after sending and the screen says so before the tap.
  - Tests (output pasted): flutter test covers the pure logic (countdown text at 0, under a minute, over a day, the reason messages, validation of stars and every criterion incl. missing and out of range, the body with the fixed keys per role, the comment limit at 500/501 with spaces), the service against a local HttpServer (paths per role, the body, the envelope, 400/404/409 mapping), and widget tests of each state (loading, open, each closed reason, validation, submit success, each error, double tap sends once, the worker label); flutter analyze reports 0 issues; sh harness/verify.sh L3 passes.
  - Update Mobile/ARCHITECTURE.md. Do NOT tick MOB-M6-01/02 until reviewed; add a note. No package is added.

## #156 [ticket] MOB-M6-04 Worker earnings and payout history (M6 - Anh)
- status: done
- area: mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/payouts/**
  - Mobile/test/features/payouts/**
  - Mobile/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task **MOB-M6-04** (worker sees real take-home income). Contract: .spec/contracts/payouts.md section 2.5 (GET /api/workers/me/earnings?month=YYYY-MM, GET /api/workers/me/payouts?page=&pageSize=); decisions Q04 (monthly batch), Q11 (80% net for Freelancer), G-2 (whole VND), G-3 (Asia/Ho_Chi_Minh display), open question P6 (no wallet: the figure is labelled "Thu nhập tháng này", never "Số dư khả dụng").
  - Why this is startable now: the contract is merged (G3) and the screen only needs the shared ApiClient plus models written from the contract. It touches only Mobile/lib/features/payouts/ (M6's folder); lib/app/** stays frozen. The two endpoints are in open PR #141 (BE-M6-07), not yet on main; the screen is tested against a local fake server and is NOT run against the real backend.
  - Screen replaces the placeholder PayoutScreen (routes /payouts and /payouts/history stay): month selector (previous/next, no future month, default current month in Asia/Ho_Chi_Minh) loads earnings; shows Thu nhập tháng này (net), gross, commission, penalty, job count, the payout status label (NOT_BUILT "Chưa chốt đợt", PENDING "Chờ chuyển khoản", TRANSFERRED "Đã chuyển khoản") and the jobs list (completed date in Asia/Ho_Chi_Minh, order id, net, gross/commission, an "Phí vắng mặt" tag when absenceFee); empty month has an empty state. 403 (agency staff) says plainly that the agency, not the worker, is paid; 400 / 404 / network errors have a message and a retry. /payouts/history lists payouts pages (period month, net, status, transferred date) with "load more".
  - VND is formatted with thousands separators and đ, whole numbers only; no money arithmetic on the device (every figure is taken from the server).
  - Tests (output pasted): flutter test covers the pure logic (month keys and navigation incl. year boundary and no future month, Ho Chi Minh date/time formatting across midnight UTC, VND format, status labels, error mapping), the service against a local HttpServer (paths, month / paging query, envelope, 400/403 mapping), and widget tests of each state (loading, data, empty, each error with retry, month navigation, history paging); flutter analyze reports 0 issues; sh harness/verify.sh L3 passes.
  - Update Mobile/ARCHITECTURE.md. Do NOT tick MOB-M6-04 until reviewed; add a note. No package is added.

## #158 [ticket] MOB-M6-03 Dispute filing and own dispute list (M6 - Anh)
- status: done
- area: mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/disputes/**
  - Mobile/test/features/disputes/**
  - Mobile/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task **MOB-M6-03** (customer and worker file a dispute with evidence photos, within 24 h). Contract: .spec/contracts/disputes.md 2.1 (POST /api/{customers|workers}/me/disputes, GET .../disputes), open questions D1 (categories), D3 (SLA 48 h), D4 (24 h window), D5 (one ticket per order). Backend is on main since PR #120/#121 (BE-M6-02).
  - **Known blocker, stated up front, not worked around:** the request needs evidenceUrls (at least one, contract 2.1) but the contract says "no upload endpoint exists yet" and the app has no photo picker package (pubspec.yaml). This ticket must not invent an upload endpoint or add a package. So the evidence step is written against an IEvidenceUploader seam; the production implementation reports "photo upload is not available yet" and the send button stays disabled with that explanation, while tests use a fake uploader. Once a leader-approved upload endpoint and picker exist, only the production uploader changes.
  - Screens (all under Mobile/lib/features/disputes/; route arguments DisputeScreenArgs(role, orderId?); without them a note says disputes are opened from a job): /disputes lists the caller's own disputes (GET .../disputes, newest first) with status, category, SLA and, for RESOLVED, the verdict (fault party, compensation in VND) and for DISMISSED a plain message; empty state; a "Khiếu nại ca này" button when orderId is given. /disputes/create is the form: category (D1: QUALITY, ATTITUDE, PROPERTY_DAMAGE, ABSENT_FEE for the customer only, OTHER), description 1-1000 characters with a counter, 1-10 evidence photos through the uploader, a statement of the 24 h window, one request per tap; 201 shows the SLA due time returned by the server; 400 field messages show under their fields, 404 (not on the order), 409 (outside the 24 h window / nothing to dispute / a dispute already exists for the order, D5) and network errors have their own text.
  - Tests (output pasted): flutter test covers the pure logic (category lists per role, label mapping, validation incl. description 1000/1001, photo count 0/1/10/11, error mapping per status), the service against a local HttpServer (paths per role, body, envelope, 400/404/409), and widget tests of the list states, the verdict display, the form validation, success, each error, double tap sends once, and the disabled-with-explanation state when the uploader is unavailable; flutter analyze reports 0 issues; sh harness/verify.sh L3 passes.
  - Update Mobile/ARCHITECTURE.md. Do NOT tick MOB-M6-03 (it stays open until the upload endpoint exists and a photo can really be attached); add a note. No package is added.

## #160 [ticket] BE-M6-01c Docs: real IWorkerReputation is no longer 'not here' (M6 - Anh)
- status: done
- area: docs
- blocked-by: -
- allowed:
  - .spec/contracts/ratings.md
  - Backend/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: .spec/plan/M6-ratings-disputes-payouts.md task BE-M6-01 (the IWorkerReputation half, ticket #126 / PR #127 merged) and the follow-up that ticket promised ("updating the IWorkerReputation block of ratings.md is deferred until PR #123 is merged"; #123 is merged).
  - Documentation only, no code. On main the docs still say the real IWorkerReputation does not exist, which is false (Backend/Infrastructure/Modules/Ratings/EfWorkerReputation.cs, WorkerReputationModule, Application/Features/Ratings/ReputationFormula.cs, Tests/Ratings/WorkerReputationTests.cs):
  - - .spec/contracts/ratings.md status line (line 3) and section 3: state that the real implementation exists and which definitions it applies (RatingAvg of CUSTOMER ratings, CompletedJobs, SuccessRate = completed / (completed + CANCELLED_BY_WORKER)), that those are the recommended defaults of question M3 and still unconfirmed by the leader.
  - - Backend/ARCHITECTURE.md: the port table rows of IWorkerReputation and IAuditLog (both have a real implementation that replaces the Fake) and the "Not here" bullet at the end of section 7.3.
  - - .spec/plan/M6-ratings-disputes-payouts.md BE-M6-01 line: add the IWorkerReputation note (ticket #126, PR #127). Do NOT tick it (question M3 is unanswered).
  - Every statement is checked against the code with a grep/read before it is written (output in the PR). sh harness/verify.sh L3 passes.

## #162 [ticket] MOB-M3-01 Worker job offer acceptance screen with 30s countdown
- status: done
- area: Mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/dispatch/**
  - Mobile/test/features/dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Card shows district, duration, net earnings after commission, and 30s countdown.
  - Accept and Decline invoke the corresponding dispatch service methods.
  - Timeout / expiration handles gracefully.
  - Flutter tests in Mobile/test/features/dispatch/ pass cleanly with zero lint warnings.
  - L3 verification passes.

## #164 [ticket] MOB-M3-02 Field arrival check-in with GPS <=100m and fallback verification
- status: done
- area: Mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/dispatch/**
  - Mobile/test/features/dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Screen allows worker to check in upon arrival at the work location with current GPS coordinates.
  - Validates GPS tolerance <= 100m according to BR-04; when within 100m, check-in succeeds immediately (POST /api/dispatch/assignments/{assignmentId}/check-in).
  - When GPS distance is > 100m, provides alternative verification options:
  - - Take/submit photo of house plate / apartment number (POST /api/dispatch/assignments/{assignmentId}/check-in/plate-photo).
  - - Customer confirmation notification on interface.
  - Includes location wrapper integration for GPS coordinates.
  - Flutter tests in Mobile/test/features/dispatch/ pass cleanly with zero lint warnings.
  - L3 verification passes.

## #166 [ticket] MOB-M3-03 Customer absent reporting with 15m wait and 2-call guard
- status: done
- area: Mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/dispatch/**
  - Mobile/test/features/dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Screen/dialog allows worker to view wait time after check-in and logged phone call attempts.
  - Enforces BR-05 & Q10: "Khách vắng mặt" action is only enabled when elapsed wait time >= 15 minutes and call_attempts >= 2.
  - Action to log system calls (POST /api/dispatch/assignments/{assignmentId}/calls/log).
  - Submitting absent report invokes POST /api/dispatch/assignments/{assignmentId}/absent and displays compensation summary (40% compensation fee).
  - Flutter tests in Mobile/test/features/dispatch/ pass cleanly with zero lint warnings.
  - L3 verification passes.

## #168 [ticket] MOB-M3-04 Force majeure incident reporting with camera and GPS
- status: done
- area: Mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/dispatch/**
  - Mobile/test/features/dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Screen allows worker to report a force majeure incident (BR-10) with incident type, description, camera photo capture, and GPS location.
  - Submits incident report (POST /api/dispatch/assignments/{assignmentId}/incidents).
  - Confirms penalty waiver (isPenaltyExempt: true) and displays auto re-dispatch status/countdown (5-minute window).
  - Updates worker when replacement is being dispatched.
  - Flutter tests in Mobile/test/features/dispatch/ pass cleanly with zero lint warnings.
  - L3 verification passes.

## #170 [ticket] WEB-M3-01 Admin: dispatch monitoring and incident list (read-only)
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/src/features/dispatch/**
  - Frontend/tests/dispatch/**
  - .spec/plan/M3-dispatch-field.md
- acceptance:
  - Admin page /admin/dispatch for monitoring real-time dispatch and incident log (read-only) matching .spec/contracts/dispatch.md §3.3 (GET /api/dispatch/admin/incidents).
  - Read-only incident list showing: incidentId, assignmentId, workerId, incidentType, description, photoEvidenceUrl, coordinates (lat/lng), reportedAt, penalty exemption status, re-dispatch status (SEARCHING/REASSIGNED), substitute deadline.
  - Clean loading, empty, and error states using Nordic UI / Tailwind styles.
  - Unit tests in Frontend/tests/dispatch/** covering data transformation, status rendering, and mock API handling.
  - Feature module registered in Frontend/src/features/dispatch/routes.tsx with nav item 'Giám sát điều phối' under section 'ĐIỀU PHỐI & HIỆN TRƯỜNG'.
  - npm test, npm run lint, npm run build PASS.
  - Verify L3 PASS.

## #172 [ticket] BE-M4-00 Contract Workers
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Plan reference: .spec/plan/M4-workers-quality.md task BE-M4-00. Read .spec/spec.md §2.2, §2.6, §2.8, §4.2, BR-06/07/08, .spec/decisions.md Q03, Q05, Q05b, Q11.
  - Write .spec/contracts/workers.md documenting all HTTP endpoints, DTOs, query parameters, error responses and domain events for the Workers module:
  - - Worker registration & profile (Freelancer STI registration, registration token handling per Q20)
  - - eKYC submission & status (CCCD 2 sides + selfie, confidence score >= 85% auto approve, manual review queue per Q05)
  - - Block Slots management (morning 08-12, afternoon 13-17, evening 17:30-20:30 weekly roster, idempotent toggling)
  - - Image quality & VoL check-in photos (BR-06, 3-5 photos per phase, VoL threshold >= 100.0, angle matching between Before & After)
  - - Acceptance & Completion (BR-07, customer confirmation, redo request 15-30m, 80% payout calculation per Q11, JobCompleted event)
  - - Extension handling ("Làm lần 2", BR-08, listening to ExtensionPaid, accept/decline, ExtensionDeclined event)
  - Conform to ApiResponse envelope conventions (success, data, error), camelCase properties, UTC ISO-8601 timestamps, role-based authorization policies (WorkerOnly, CustomerOnly, AdminOnly).
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.
  - Tick BE-M4-00 in .spec/plan/M4-workers-quality.md with evidence: <log or PR#> in the same PR.

## #174 [ticket] BE-M4-05a Variance of Laplacian image sharpness algorithm
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Plan reference: .spec/plan/M4-workers-quality.md task BE-M4-05a. Read .spec/decisions.md Q03 (Photo sharpness, Variance of Laplacian, BR-06).
  - Implement pure algorithm / service for Variance of Laplacian image blur score calculation:
  - - Convert image to grayscale matrix
  - - Resize to width 640 px (Vol.ResizeWidthPx, keeping aspect ratio)
  - - Convolve with 3x3 Laplacian kernel
  - - Calculate variance of Laplacian output values as vol_score
  - - Accept photo if vol_score >= Vol.Threshold (default 100.0 from BusinessRules)
  - Unit tests in Backend/Tests/Workers verifying:
  - - Sharp image produces score >= 100.0
  - - Blurry image produces score < 100.0
  - - Edge cases (small images, solid color images with variance 0) behave deterministically
  - sh harness/verify.sh L3 PASSES; paste the last line and log path in the PR.
  - Tick BE-M4-05a in .spec/plan/M4-workers-quality.md with evidence: <log or PR#> in the same PR.

## #176 [ticket] BE-M4-01 Worker profile endpoints and STI management (M4 - Đạt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M4-workers-quality.md task BE-M4-01. Read .spec/contracts/workers.md §2.1, .spec/decisions.md Q20 (Registration tokens), Q21 C3 (IWorkerProfileQuery), §6 (WORKER STI).
  - Implement Worker registration and profile management endpoints & MediatR handlers:
  - - POST /api/workers/register: Registration token verification, creates WORKER entity (worker_type = FREELANCER, agency_id = null, work_status = IDLE, kyc_status = PENDING). Returns HTTP 201.
  - - GET /api/workers/me: (WorkerOnly) Returns current worker profile.
  - - PATCH /api/workers/me: (WorkerOnly) Updates worker profile details (fullName, avatarUrl, bio).
  - - GET /api/workers/{id}: (AdminOnly / CustomerOnly) Public worker summary view.
  - Implement real IWorkerProfileQuery port in Workers: GetWorkerSummaryAsync(workerId) and ExistsAsync(workerId) querying WORKER EF repository.
  - Unit and endpoint tests in Backend/Tests/Workers/ verifying registration, authentication claims, profile updates, duplicate CCCD/phone protection, and IWorkerProfileQuery.
  - Regenerate .spec/contracts/openapi.json snapshot.
  - sh harness/verify.sh L3 PASSES; paste the last line and log path in the PR.
  - Tick BE-M4-01 in .spec/plan/M4-workers-quality.md with evidence: <log or PR#> in the same PR.

## #178 [ticket] BE-M4-02 eKYC submission and auto-approval logic (M4 - Đạt)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: .spec/plan/M4-workers-quality.md task BE-M4-02. Read .spec/contracts/workers.md §2.2, .spec/decisions.md Q05 (eKYC fake provider, confidence 92.00%, auto approve threshold 85.00%).
  - Implement eKYC submission & status endpoints and MediatR handlers:
  - - POST /api/workers/me/ekyc (WorkerOnly): Accepts frontCccdUrl, backCccdUrl, selfieUrl. Evaluates submission via IEkycProvider.
  - - confidenceScore >= Ekyc.AutoApproveConfidence (85.00%) -> sets kycStatus = APPROVED, transitions WorkStatus PENDING -> IDLE.
  - - confidenceScore < 85.00% -> sets kycStatus = MANUAL_REVIEW.
  - - Returns EkycResultResponse.
  - - GET /api/workers/me/ekyc/status (WorkerOnly): Returns worker's current eKYC status details.
  - Implement EkycProvider in Backend/Infrastructure/Modules/Workers/ implementing IEkycProvider port with configurable confidence score.
  - Unit and endpoint tests in Backend/Tests/Workers/ verifying auto-approval (confidence >= 85%), manual review queue routing (confidence < 85%), and state transitions.
  - Regenerate .spec/contracts/openapi.json.
  - sh harness/verify.sh L3 PASSES; paste the last line and log path in the PR.
  - Tick BE-M4-02 in .spec/plan/M4-workers-quality.md with evidence: <log or PR#> in the same PR.

## #180 [ticket] BE-M4-04 Admin eKYC sample audit
- status: done
- area: -
- blocked-by: 178
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
  - .spec/contracts/openapi.json
- acceptance:
  - Admin can list eKYC manual review queue (GET /api/admin/workers/ekyc-queue) including workers requiring audit.
  - Implements sample audit sampling rule: a new Freelancer's first 5 (FullAuditFirstJobs) completed jobs are audited at 100%; afterwards a random sample of 20% (AuditRate) is audited per Q05.
  - Admin can review and manually approve or reject worker eKYC (POST /api/admin/workers/{id}/ekyc-review).
  - When eKYC is approved by Admin, worker status changes to IDLE (if previously PENDING or MANUAL_REVIEW).
  - When eKYC is rejected/revoked by Admin, worker status is locked (LOCKED) or set to rejected state with reason.
  - All unit and integration tests pass.
  - verify L3 passes cleanly.

## #181 [ticket] BE-M2-00 Contract Booking + Payments (M2 - Viên)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Booking/**
  - Backend/Application/Features/Payments/**
  - Backend/WebAPI/Controllers/Booking/**
  - Backend/WebAPI/Controllers/Payments/**
  - Backend/Infrastructure/Modules/Booking/**
  - Backend/Infrastructure/Modules/Payments/**
  - Backend/Tests/Booking/**
  - Backend/Tests/Payments/**
  - Backend/Domain/Entities/*.Booking.cs
  - Backend/Domain/Entities/*.Payments.cs
  - Mobile/lib/features/booking/**
  - Mobile/lib/features/payments/**
  - Mobile/test/features/booking/**
  - Mobile/test/features/payments/**
  - Frontend/src/features/booking/**
  - Frontend/src/features/payments/**
  - .spec/plan/M2-*.md
  - .spec/contracts/booking.md
  - .spec/contracts/payments.md
- acceptance:
  - Plan reference: .spec/plan/M2-booking-payments.md task BE-M2-00. Read .spec/spec.md §1.2, §2.5, §4.1, BR-01/02/03/08/10 and .spec/decisions.md Q01, Q04, Q04b, Q07, Q13, Q15, G-5 first.
  - Write .spec/contracts/booking.md documenting HTTP endpoints, DTOs, query parameters, error responses and domain events of the Booking module:
  - - Order creation (segment Economy/Premium, own address, Block Slot morning 08-12 / afternoon 13-17 / evening 17:30-20:30, services, note; Premium booked >= 4 h ahead per Q13; <= 80 m2 one worker, > 80 m2 two parallel assignments per BR-01/02)
  - - Price preview and price locked into JOB_ORDER.total_amount from PRICE_RULE (Q01)
  - - Order status and progress, customer order history
  - - Customer cancellation before a worker is assigned (Q15)
  - - "Lam lan 2" extension (BR-08, new QR, ExtensionPaid)
  - - Admin price edit with mandatory reason + audit log, read-only price change history (Q01, G-5)
  - - The order status list shared with M1 (existing JobOrderStatus enum, no new status invented)
  - Write .spec/contracts/payments.md: dynamic QR + PaymentTransaction (Pay-per-Job 100 %), IPN (signature check, idempotent replay), payment status polling + SignalR (Q07), reconciliation and 15-minute QR expiry (Q04), 100 % refund (BR-03, BR-10, dispute verdict). MoMo sandbox only, no real money; VietQR deferred (Q04b); MoMo field names and signature are NOT guessed (left to BE-M2-06 with the official docs).
  - Questions not answered by spec.md or decisions.md are listed in an open-questions section for the leader, not guessed.
  - Conform to identity.md §1 conventions (ApiResponse envelope, camelCase, UTC ISO-8601, role policies, 404-for-not-owned).
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.
  - Tick BE-M2-00 in .spec/plan/M2-booking-payments.md with evidence: <log or PR#> in the same PR.

## #184 [ticket] BE-M4-05 Block Slots availability roster
- status: done
- area: -
- blocked-by: 178
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
  - .spec/contracts/openapi.json
- acceptance:
  - Implement GET /api/workers/me/slots (WorkerOnly) returning worker's availability slots for a date range (startDate to endDate).
  - Implement POST /api/workers/me/slots/toggle (WorkerOnly) enabling/disabling availability for a given slotDate and shiftCode (SHIFT_MORNING, SHIFT_AFTERNOON, SHIFT_EVENING).
  - Enforces shift times: SHIFT_MORNING (08:00-12:00), SHIFT_AFTERNOON (13:00-17:00), SHIFT_EVENING (17:30-20:30).
  - Handlers handle UNIQUE(worker_id, slot_date, shift_code) database constraint gracefully; race conditions / double click requests are fully idempotent and do not fail with duplicate row errors.
  - Unit and concurrency integration tests pass.
  - verify L3 passes cleanly.

## #185 [ticket] BE-M2-01 Shift rule BR-01/BR-02 pure function (M2 - Viên)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Booking/**
  - Backend/Tests/Booking/**
  - .spec/plan/M2-*.md
- acceptance:
  - Plan reference: .spec/plan/M2-booking-payments.md task BE-M2-01. Read .spec/spec.md §1.2, BR-01, BR-02 and .spec/decisions.md Q01, G-4 first. Draft contract: .spec/contracts/booking.md §1.3 (PR #183).
  - A pure function in Backend/Application/Features/Booking (no DB, no I/O, no clock) that, from an address total area in m2, returns the shift plan: total area <= Area.StandardMaxM2 (80) -> 1 worker; > 80 -> exactly 2 parallel Job Assignments (BR-02, Q01: never 3+); each shift lasts at most Shift.MaxHours (4).
  - Thresholds come from BusinessRules (Area.StandardMaxM2, Shift.MaxHours); no number hard-coded in the function (G-4).
  - Area <= 0 is rejected (ArgumentOutOfRangeException).
  - Unit tests in Backend/Tests/Booking: boundaries 80.00 -> 1 worker, 80.01 -> 2 workers, small area (e.g. 30) -> 1, large area (e.g. 250) -> 2 (not 3+), 0 and negative rejected, max hours = 4, a changed BusinessRules threshold is honored.
  - No endpoint, no package, no entity or schema change (AGENTS.md rule 5). The BR-02 fallback (one worker, two consecutive shifts) belongs to Dispatch (BE-M3-08) and is out of scope.
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR.
  - Tick BE-M2-01 in .spec/plan/M2-booking-payments.md with evidence in the same PR.

## #187 [ticket] BE-M4-06 Upload Before/After photos and VoL quality verification
- status: done
- area: -
- blocked-by: 178
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
  - .spec/contracts/openapi.json
- acceptance:
  - Implement POST /api/workers/assignments/{assignmentId}/photos (WorkerOnly) to upload and verify Before/After photos.
  - Implement GET /api/workers/assignments/{assignmentId}/photos (WorkerOnly / CustomerOnly / AdminOnly) returning uploaded photos for an assignment.
  - Enforce VoL threshold check (Vol.Threshold default 100.0) via IImageQualityService.
  - Rejected photos (volScore < 100.0) are saved with isAccepted = false for audit, and return HTTP 400 prompting worker to retake photo.
  - Enforce phase bounds (Min 3, Max 5 accepted photos per phase) and AFTER photo angle matching: AFTER photo angleNo must match an accepted BEFORE photo angleNo.
  - Unit and integration tests pass.
  - verify L3 passes cleanly.

## #189 [ticket] BE-M4-07 Real IWorkerAvailabilityQuery implementation
- status: done
- area: -
- blocked-by: 184
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
  - .spec/contracts/openapi.json
- acceptance:
  - Implement real WorkerAvailabilityQuery in Backend/Infrastructure/Modules/Workers/ implementing IWorkerAvailabilityQuery.
  - Filters candidate workers by worker_type = FREELANCER, kyc_status = APPROVED, work_status = IDLE, slot_date = Date, shift_code = ShiftCode, slot_status = AVAILABLE.
  - Bounding box pre-filtering on latitude/longitude coordinates to utilize database indexes efficiently before calculating exact Haversine distance via IGeoService.
  - Orders available candidates by distance ascending and limits results to query.Limit.
  - Registers IWorkerAvailabilityQuery -> WorkerAvailabilityQuery in WorkersModule.
  - Unit and integration tests pass.
  - verify L3 passes cleanly.

## #192 [ticket] BE-M2-02 Price calculation from PRICE_RULE (M2 - Viên)
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Booking/**
  - Backend/Infrastructure/Modules/Booking/**
  - Backend/Tests/Booking/**
  - .spec/plan/M2-*.md
- acceptance:
  - Plan reference: .spec/plan/M2-booking-payments.md task BE-M2-02. Read .spec/decisions.md Q01, G-2, G-4 and the draft contract .spec/contracts/booking.md §1.3 (PR #183) first. The worker count (ShiftPlanner, BE-M2-01, PR #191, not merged yet) is an INPUT here; BE-M2-03 combines both, so this ticket does not depend on #185.
  - Area bracket per Q01 from the address total area: UP_TO_30 (<= 30), FROM_31_TO_80 (> 30 and <= 80), OVER_80 (> 80). The bracket codes are the PRICE_RULE.area_bracket values seeded by BASE-10 (Backend/Infrastructure/Persistence/DefaultDataSeeder.cs).
  - Pure price calculation: unit price = active PRICE_RULE row for (service_tier, area_bracket); required workers is a parameter (1 or 2, BR-01/02; anything else rejected); total = unit price x required workers, whole VND via Vnd.Round (G-2). Nothing is hard-coded: prices are read from PRICE_RULE only (Q01).
  - A Booking repository reads the active PRICE_RULE row (EF, existing AppDbContext.PriceRules; registered by a Booking IModule). A missing or inactive row is an error, never a guessed price.
  - A Booking pricing service combines both: (serviceTier, totalAreaM2, requiredWorkers) -> quote (areaBracket, requiredWorkers, unitPrice, totalAmount). This is what BE-M2-03 will freeze into JOB_ORDER.total_amount. No endpoint in this ticket (the contract is not approved yet).
  - Tests in Backend/Tests/Booking:
  - - unit tests of all 6 tier x bracket combinations with the Q01 default prices;
  - - bracket boundaries 30.00 / 30.01 and 80.00 / 80.01;
  - - total = unit x 2 for OVER_80;
  - - missing rule rejected;
  - - a DB-backed test that the repository returns the seeded row and ignores inactive rows (skips when SQL Server is unavailable, like the existing DB tests).
  - No package, endpoint, entity or schema change (AGENTS.md rule 5).
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR. Tick BE-M2-02 in .spec/plan/M2-booking-payments.md with evidence in the same PR.

## #194 [ticket] P3.02 Backend Core: GPS Check-in and VoL Image Processing
- status: done
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Dispatch/**
  - Backend/WebAPI/Controllers/Dispatch/**
  - Backend/Infrastructure/Modules/Dispatch/**
  - Backend/Tests/Dispatch/**
  - Backend/Application/Features/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
- acceptance:
  - Implement the worker GPS check-in endpoint using the existing FieldCheckInService, address lookup, 100 m tolerance, check-in log, and assignment transition; out-of-range results require fallback and must not mark the assignment checked in.
  - Keep worker authentication, assignment ownership, eligible state, request coordinate validation, and error responses consistent with the existing contracts.
  - Assess uploaded photos from actual IFileStorage bytes through the existing IImageQualityService and Q03 VoL threshold; persist rejected blurry photos as not accepted and return the existing validation error.
  - Fix the reported backend compilation errors by using existing DTO, entity, repository, DI, and unit-of-work contracts; do not add speculative APIs or entity properties.
  - Add focused Dispatch and Workers tests covering GPS check-in success/out-of-range and photo quality accept/reject behavior.
  - ./harness/run.ps1 verify L3 passes, including scope-check.

## #197 [ticket] BE-M2-10 Order progress and customer history queries (M2 - Viên)
- status: in-progress
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Booking/**
  - Backend/Infrastructure/Modules/Booking/**
  - Backend/Tests/Booking/**
  - .spec/plan/M2-*.md
- acceptance:
  - Plan reference: .spec/plan/M2-booking-payments.md task BE-M2-10. Read .spec/spec.md section 5.2 and the draft contract .spec/contracts/booking.md (PR #183) first. Needs only G1 (done).
  - Query layer only, NO endpoint, controller or DTO exposed over HTTP (contract #183 is not approved yet, AGENTS.md rule 5).
  - Order progress query: all JOB_ASSIGNMENT rows of an order, WHERE order_id = :id, reading only Job_Assignment (0 JOIN, spec 5.2). Exposes the assignment status per row.
  - Customer history query: JOB_ASSIGNMENT rows WHERE customer_id = :id (denormalized column JobAssignment.CustomerId), 0 JOIN, newest first.
  - Booking-owned read interface + EF implementation registered by the existing BookingModule (Backend/Infrastructure/Modules/Booking/BookingModule.cs, from BE-M2-02). No schema, package, entity or index change.
  - Tests in Backend/Tests/Booking: progress returns only the rows of that order; history returns only that customer's rows and not another customer's; empty result is an empty list; the generated SQL/query touches only the Job_Assignment table (no Include/Join); DB-backed ones skip when SQL Server is unavailable, like the existing DB tests.
  - sh harness/verify.sh L3 passes; paste the last line and log path in the PR. Tick BE-M2-10 in .spec/plan/M2-booking-payments.md with evidence in the same PR.

## #198 [ticket] BE-M4-08 Nghiệm thu ca làm (BR-07): submit completion, accept job, payout 80% & JobCompleted
- status: done
- area: -
- blocked-by: 187
- allowed:
  - Backend/Application/Features/Workers/**
  - Backend/WebAPI/Controllers/Workers/**
  - Backend/Infrastructure/Modules/Workers/**
  - Backend/Tests/Workers/**
  - Backend/Domain/Entities/*.Workers.cs
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - Frontend/src/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
  - .spec/contracts/openapi.json
- acceptance:
  - Worker can submit completion for an assignment (POST /api/workers/assignments/{id}/submit-completion).
  - Customer can request redo (POST /api/customers/assignments/{id}/request-redo).
  - Customer can confirm job completion (POST /api/customers/assignments/{id}/accept).
  - On accept, status becomes COMPLETED, calculates payoutAmount (Freelancer 80%, Agency per package commission, rounded per G-2 via Vnd helper).
  - Worker workStatus returns to IDLE (for Freelancers).
  - Emits JobCompleted event.
  - All unit and integration tests pass.
  - verify L3 passes cleanly.

## #200 [ticket] MOB-M4-01 eKYC Mobile: chụp CCCD 2 mặt + selfie và gửi xác thực
- status: done
- area: -
- blocked-by: 178
- allowed:
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Flutter mobile screen/flow for eKYC in Mobile/lib/features/workers/:
  - - Step 1: Capture or select front CCCD photo.
  - - Step 2: Capture or select back CCCD photo.
  - - Step 3: Capture or select selfie photo.
  - - Preview step before submitting.
  - - Submits to POST /api/workers/me/ekyc with frontCccdUrl, backCccdUrl, selfieUrl.
  - - Displays result feedback (APPROVED -> active account, MANUAL_REVIEW -> pending queue).
  - Unit/widget test in Mobile/test/features/workers/ekyc_screen_test.dart passes.
  - verify L3 passes cleanly.

## #202 [ticket] MOB-M4-02 Lịch rảnh Block Slots Mobile: lưới tuần bật/tắt Sáng/Chiều/Tối
- status: done
- area: -
- blocked-by: 184
- allowed:
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Flutter mobile screen in Mobile/lib/features/workers/screens/worker_slots_screen.dart:
  - - Weekly grid view displaying days of the week and 3 shifts: Sáng (08:00-12:00), Chiều (13:00-17:00), Tối (17:30-20:30).
  - - Fetches availability slots from GET /api/workers/me/slots?startDate=...&endDate=....
  - - Toggles availability slot on tap via POST /api/workers/me/slots/toggle with slotDate, shiftCode, isActive.
  - - Idempotent UI updating slot state smoothly.
  - Unit/widget test in Mobile/test/features/workers/worker_slots_screen_test.dart passes.
  - verify L3 passes cleanly.

## #204 [ticket] M6 checklist: tick merged tasks with evidence (M6 - Anh)
- status: in-progress
- area: docs
- blocked-by: -
- allowed:
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: AGENTS.md rule 5 of "Lệnh phân tích dự án" (after a task is done, tick its line in the slot's own checklist with evidence). The leader merged PRs #123, #127, #131, #133, #135, #137, #139, #141, #143, #145, #147, #150, #151, #153, #155, #157, #159, #161 (checked with gh pr view <n> --json state,mergedAt).
  - Tick ([x]) with PR numbers as evidence, after checking each merged file exists on main: BE-M6-01, 03, 04, 05, 06, 07, 08, 09; WEB-M6-01..04; MOB-M6-01/02/04. Rewrite their stale "chờ review / chưa tick" notes. The recommended defaults the leader has not confirmed (M3, A2, P2-P7, D1-D9) stay listed as open questions in the line, not hidden.
  - Do NOT tick BE-M6-02 (the worker lock through M4 does not exist on main: the verdict only records lockWorker in the audit log) and MOB-M6-03 (a photo cannot be attached: no upload endpoint for dispute evidence, no picker package). Update their notes to say merged and what is still open.
  - sh harness/verify.sh L3 passes.

## #205 [ticket] MOB-M4-03 Màn thi công Mobile: checklist công việc + bấm giờ ca (≤4h)
- status: done
- area: -
- blocked-by: 198
- allowed:
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Flutter mobile screen in Mobile/lib/features/workers/screens/worker_execution_screen.dart:
  - - Execution checklist with interactive checkboxes (e.g., Dọn dẹp phòng khách, Lau cửa kính, Dọn nhà bếp, Thu gom rác).
  - - Shift timer widget tracking elapsed work duration (≤ 4 hours = 240 minutes) with live countdown and progress bar.
  - - Navigation / CTA to upload Before/After photos and submit job completion.
  - Unit/widget test in Mobile/test/features/workers/worker_execution_screen_test.dart passes.
  - verify L3 passes cleanly.

## #208 [ticket] MOB-M4-04 Chụp ảnh Before/After Mobile: kiểm VoL độ nét & khung hướng dẫn góc
- status: done
- area: -
- blocked-by: 187
- allowed:
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Flutter mobile screen in Mobile/lib/features/workers/screens/worker_photos_screen.dart:
  - - Phase switcher (BEFORE / AFTER) and angle selector (Góc 1..5) with angle guide overlay.
  - - VoL sharpness calculator / API checker against threshold 100.0.
  - - Retake prompt on blurred image (volScore < 100.0).
  - - Angle consistency check (AFTER angles must match accepted BEFORE angles).
  - - List view of uploaded photos per phase with status badges.
  - Unit/widget test in Mobile/test/features/workers/worker_photos_screen_test.dart passes.
  - verify L3 passes cleanly.

## #209 [ticket] BE-M6-02c Dispute verdict really locks the freelancer (M6 - Anh)
- status: in-progress
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Disputes/**
  - Backend/Infrastructure/Modules/Disputes/**
  - Backend/Tests/Disputes/**
  - Backend/ARCHITECTURE.md
  - .spec/contracts/disputes.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: BE-M6-02 "khoá thợ" (the part left open by ticket #132). The leader gave no further answer and set tonight as the deadline, so this ticket applies the recommended default and states it in the PR.
  - Today a verdict with lockWorker = true only writes an audit row (worker_lock_requested); the worker stays usable. Make it real: inside the same transaction as the verdict, set WORKER.work_status = LOCKED for the FREELANCER workers of the order's assignments (one conditional UPDATE ... WHERE worker_type = 'FREELANCER' AND work_status <> 'LOCKED', no read-modify-write), through a method on the Disputes-owned IDisputeRepository (same pattern as IPayoutPenaltySource: no new M1 port, no edit to an M4 file), and write one audit row per locked worker (entity_type = WORKER, field_name = work_status, new_value = LOCKED).
  - A worker already LOCKED is not touched and gets no audit row; an agency worker is never locked (already rejected with 400 unless FREELANCER fault); lockWorker = false or a dismissal changes nothing.
  - Tests (output pasted): fake-repository unit tests (locks only the freelancers of the order, once, in the transaction; none when false; audit rows) and a SQL Server test that seeds a worker and checks the work_status really changes and an already-locked worker is skipped.
  - Update .spec/contracts/disputes.md (effect of lockWorker) and Backend/ARCHITECTURE.md (how it is done and its known limit: M4's job completion sets a worker back to IDLE, which would undo the lock if the worker finishes a running job after the verdict; reading the lock in M4 is M4's). sh harness/verify.sh L3: report honestly if main's unrelated tests fail.

## #211 [ticket] BE-M6-02d Dispute evidence photo upload and file serving (M6 - Anh)
- status: in-progress
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Disputes/**
  - Backend/Infrastructure/Modules/Disputes/**
  - Backend/WebAPI/Controllers/Disputes/**
  - Backend/Tests/Disputes/**
  - Backend/ARCHITECTURE.md
  - .spec/contracts/disputes.md
  - .spec/contracts/openapi.json
- acceptance:
  - Plan reference: MOB-M6-03 / BE-M6-02: "tạo khiếu nại kèm ảnh bằng chứng". The contract says no upload endpoint exists and evidenceUrls accepts any text; the leader gave no answer and set tonight as the deadline, so this ticket adds the smallest upload that makes a dispute's photo real, and writes it into the contract first (same PR).
  - POST /api/customers/me/disputes/evidence and POST /api/workers/me/disputes/evidence (policies CustomerOnly / WorkerOnly), JSON body { "fileName", "contentType", "contentBase64" } (JSON because the mobile ApiClient is JSON-only and M4's photo upload is also JSON) -> 201 data: { path, url, sizeBytes }. Rules: contentType one of image/jpeg, image/png, image/webp; decoded size 1 byte to 5 MB; valid base64; fileName 1-100 characters, only the file name part is used. 400 with a field map otherwise. The file is stored through the existing IFileStorage (the port's own summary names "dispute evidence") in folder dispute-evidence, file name prefixed c{customerId}- / w{workerId}-; the returned url is what the caller puts into evidenceUrls.
  - GET /files/dispute-evidence/{fileName} (anonymous, only that folder, only image content types, no path traversal) serves a stored evidence file by its unguessable name (a GUID inside the stored name), so the Admin console's <img> can show it without a bearer header (browsers do not send one for images). Anything else is a 404.
  - Tests (output pasted): service validation (empty, wrong type, too big, bad base64, long name, a name with a path) and success (stored under the folder, prefix, size); both controllers (401 without a user id, 201 mapping); the file endpoint (serves a stored file with the right content type, 404 for an unknown name, traversal and a non-image extension); OpenApiSnapshotTests pass after regenerating .spec/contracts/openapi.json.
  - Update .spec/contracts/disputes.md (new section 2.1a, D6-style note that the endpoint was added without leader approval) and Backend/ARCHITECTURE.md. sh harness/verify.sh L3: report honestly if unrelated tests fail.

## #214 [ticket] MOB-M6-03b Dispute evidence photo upload from the app (M6 - Anh)
- status: in-progress
- area: mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/disputes/**
  - Mobile/test/features/disputes/**
  - Mobile/ARCHITECTURE.md
  - .spec/plan/M6-ratings-disputes-payouts.md
- acceptance:
  - Plan reference: MOB-M6-03 (dispute with evidence photos). Follows ticket #159 (form behind IEvidenceUploader, production uploader unavailable) and the backend ticket #211 (PR #213: POST .../disputes/evidence, contract disputes.md 2.1a). The leader gave no answer and set tonight as the deadline.
  - Add ApiEvidenceUploader (in Mobile/lib/features/disputes/services/): takes the photo from the shared ICameraWrapper (Mobile/lib/core/device/camera_wrapper.dart, used by M3/M4 too), detects image/jpeg, image/png or image/webp from the first bytes, sends { fileName, contentType, contentBase64 } with the shared ApiClient to /api/{customers|workers}/me/disputes/evidence and returns the url. A cancelled capture returns null; an unreadable photo (no bytes, unknown format) or a server refusal (400) is an ApiException with a message the form shows, before any request when the bytes are unusable. It is now the default of the dispute screens, so the "photos are not available" state disappears from the real app. No package is added.
  - Known limit stated in the PR: the repository has no real camera yet (CameraWrapper returns a placeholder with no bytes unless a test injects an image), so on a device the add-photo button will show "không đọc được ảnh" until the app wires a real camera; the same holds for M3/M4's screens.
  - Tests (output pasted): the uploader against a local HttpServer (path per role, JSON body with the right type and base64, returned url, cancel, empty bytes with no request, unknown format, 400), and a screen test that the default form offers "Thêm ảnh". flutter analyze 0 issues, flutter test passes, sh harness/verify.sh L3 result reported honestly.
  - Update Mobile/ARCHITECTURE.md; in the plan, MOB-M6-03 stays unticked until the leader's PRs merge (note only).

## #216 [ticket] MOB-M4-05 Khách xem ảnh & xác nhận nghiệm thu Mobile
- status: done
- area: -
- blocked-by: #208
- allowed:
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Screen allows customer to view Before/After photo comparisons for each angle.
  - Confirm button calls POST /api/customers/assignments/{id}/accept and updates status to COMPLETED with gross/payout breakdown.
  - Request redo button opens modal/section for reason + optional photo URL and calls POST /api/customers/assignments/{id}/request-redo.
  - Widget test passes in Mobile/test/features/workers/customer_acceptance_screen_test.dart.
  - verify L3 passes.

## #217 [ticket] BE-M6-03b Absence notifications to Admin, customer and worker (M6 - Anh)
- status: in-progress
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Admin/**
  - Backend/Infrastructure/Modules/Admin/**
  - Backend/Tests/Admin/**
  - Backend/ARCHITECTURE.md
  - .spec/contracts/admin.md
- acceptance:
  - Plan reference: BE-M6-03 ("thông báo cho Admin/khách chưa làm", left open by ticket #134) and the overview §5 event catalogue (CustomerAbsentReported M3→M6, CustomerAbsentApproved M6→M2,M3,M4). INotificationService (BASE-12, M1) is real; nobody sends these messages yet. The leader gave no answer and set tonight as the deadline, so this ticket sends the obvious messages and states it.
  - Two MediatR handlers in the Admin feature: on CustomerAbsentReported notify every active Admin (UserRole.Admin, topic absence.reported, body with the order id and a pointer to the approval queue); on CustomerAbsentApproved notify the order's customer (topic absence.approved: the 40 % fee and the 60 % refund, in whole VND) and the worker (the compensation). Two new read methods on IAbsenceRepository (active admin ids, customer id of an order). Notifications are best effort: a sending or reading failure is logged and never turns the already committed approval into an error.
  - Tests (output pasted): unit tests with fakes (admins notified once each with the right topic/data; customer and worker messages with the amounts; an order without customer, no admin, and a failing notifier do not throw) and a SQL Server test for the two repository methods. Document in Backend/ARCHITECTURE.md and .spec/contracts/admin.md. sh harness/verify.sh L3: report honestly if unrelated tests fail.

## #219 [ticket] BE-M6-08b Serialise the SQL Server tests that share one database (M6 - Anh)
- status: in-progress
- area: backend
- blocked-by: -
- allowed:
  - Backend/Tests/Admin/**
  - Backend/ARCHITECTURE.md
- acceptance:
  - Plan reference: BE-M6-08 (tests that must be repeatable). Problem with evidence: about 30 test classes (Admin, Customers, Disputes, Identity, Payouts, Ratings, Workers, Persistence) write to and count rows of the SAME local SQL Server database while xUnit runs classes in parallel. AdminDashboardTests.Real_database_every_count_follows_the_definitions_of_A2_on_the_real_tables (mine) failed with Expected: 4  Actual: 5 in 3 of 4 full runs on 2026-10-09 (.harness/evidence/20261009-155836-L3.log), and passed alone.
  - Add one assembly attribute [assembly: CollectionBehavior(DisableTestParallelization = true)] in Backend/Tests/Admin/SharedDatabase.cs, with a comment saying why. Report the time of a full run before and after (the suite runs in about 5-8 s today).
  - Tests (output pasted): five consecutive full runs show AdminDashboardTests passing every time (the other failures on main belong to other slots and are listed). Note in Backend/ARCHITECTURE.md (testing section).

## #220 [ticket] BE-M2-03 Create JobOrder Economy/Premium (M2 - Viên)
- status: ready
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Booking/**
  - Backend/WebAPI/Controllers/Booking/**
  - Backend/Infrastructure/Modules/Booking/**
  - Backend/Tests/Booking/**
  - .spec/plan/M2-*.md
- acceptance:
  - Plan reference: .spec/plan/M2-booking-payments.md task BE-M2-03. Contract: .spec/contracts/booking.md section 3.3 (POST /api/booking/orders), rules 1-7, in that order; nothing is written when a rule fails. Read decisions Q13, G-2, G-3.
  - STATUS: NOT READY. Waiting for the leader's answer on contract questions B1, B2, B3, B7, B8 (booking.md section 6). Proposed answers (the contract's recommended defaults) are in the first comment of this issue; do not start until the leader confirms them and the Scope exceptions linked there are done.
  - Reuse BE-M2-01 ShiftPlanner (requiredWorkers) and BE-M2-02 IPricingService (unitPrice, totalAmount); total frozen in JOB_ORDER.total_amount at creation (Q01).
  - Validation per contract rule 1 (400, O5). Address ownership and totalAreaM2 only through a port, never CUSTOMER_ADDRESS (B2). Shift start in the future (409 SHIFT_IN_PAST); PREMIUM shift start >= now + Premium.MinLeadHours (409 PREMIUM_LEAD_TIME); PREMIUM calls IAgencyCapacityService.TryReserveAsync, null -> 409 FULLY_BOOKED and no order. Times via IClock only (G-3).
  - JOB_ORDER inserted as PENDING_PAYMENT with created_at = updated_at = now (UTC).
  - Tests in Backend/Tests/Booking: each rule and its boundary (shift start exactly now + 4 h, 80.00 vs 80.01 m2 giving 1 vs 2 workers), address of another customer -> 404, FULLY_BOOKED creates no order, price frozen at creation, order_code unique.
  - No package, schema or shared-file change (AGENTS.md rule 5). Verification: the harness verify L3 passes; paste the last line and log path in the PR. Tick BE-M2-03 in .spec/plan/M2-booking-payments.md with evidence in the same PR.

## #221 [ticket] BE-M2-03 Create JobOrder Economy/Premium (M2 - Viên)
- status: ready
- area: backend
- blocked-by: -
- allowed:
  - Backend/Application/Features/Booking/**
  - Backend/WebAPI/Controllers/Booking/**
  - Backend/Infrastructure/Modules/Booking/**
  - Backend/Tests/Booking/**
  - .spec/plan/M2-*.md
- acceptance:
  - Plan reference: .spec/plan/M2-booking-payments.md task BE-M2-03. Contract: .spec/contracts/booking.md section 3.3 (POST /api/booking/orders), rules 1-7, in that order; nothing is written when a rule fails. Read decisions Q13, G-2, G-3.
  - STATUS: NOT READY. The leader has NOT answered contract questions B1, B2, B3, B7, B8 (booking.md section 6) yet; do not start this ticket until the leader confirms them in a comment on this issue. PROPOSED answers (the recommended default of each row, not decided). Meaning here: B1 shift codes SHIFT_MORNING / SHIFT_AFTERNOON / SHIFT_EVENING (as workers.md 2.3); B2 new read port ICustomerAddressQuery.GetOwnedAsync(customerId, addressId) -> { addressId, totalAreaM2, latitude, longitude } or null, Fake first; B3 Economy: shift start only has to be in the future, booking horizon 14 days for both tiers as the new key Booking.MaxDaysAhead (14); B7 order_code = "GV" + yyMMdd (local date) + 6 random uppercase alphanumerics, retry on UNIQUE clash; B8 the reservation is keyed by orderId (new parameter of IAgencyCapacityService), Booking stores nothing.
  - Order of work for rules 6-7 given B8: the order id must exist before the reservation, so insert JOB_ORDER and call TryReserveAsync(orderId, ...) in one transaction; TryReserveAsync null -> roll back, 409 FULLY_BOOKED, no order left behind. Needs the leader's confirmation in this issue if it conflicts with the contract's rule order.
  - If the leader accepts the proposal, these Scope exceptions are needed before the matching code (to be filed after the answer): the port ICustomerAddressQuery + Fake (B2), the Booking.MaxDaysAhead key and its decisions.md entry (B3), the orderId parameter of IAgencyCapacityService + its Fake and callers (B8). B1 alignment of the Dispatch codes SANG/CHIEU/TOI is a separate fix ticket of M3, not this ticket.
  - Reuse BE-M2-01 ShiftPlanner (requiredWorkers) and BE-M2-02 IPricingService (unitPrice, totalAmount); total frozen in JOB_ORDER.total_amount at creation (Q01).
  - Validation per contract rule 1 (400, O5). Address ownership and totalAreaM2 only through a port, never CUSTOMER_ADDRESS (B2). Shift start in the future (409 SHIFT_IN_PAST); PREMIUM shift start >= now + Premium.MinLeadHours (409 PREMIUM_LEAD_TIME); PREMIUM calls IAgencyCapacityService.TryReserveAsync, null -> 409 FULLY_BOOKED and no order. Times via IClock only (G-3).
  - JOB_ORDER inserted as PENDING_PAYMENT with created_at = updated_at = now (UTC).
  - Tests in Backend/Tests/Booking: each rule and its boundary (shift start exactly now + 4 h, 80.00 vs 80.01 m2 giving 1 vs 2 workers), address of another customer -> 404, FULLY_BOOKED creates no order, price frozen at creation, order_code unique.
  - No package, schema or shared-file change (AGENTS.md rule 5). Verification: the harness verify L3 passes; paste the last line and log path in the PR. Tick BE-M2-03 in .spec/plan/M2-booking-payments.md with evidence in the same PR.

## #223 [ticket] MOB-M6-05 Entry points to rating and dispute from the worker's jobs (M6 - Anh)
- status: in-progress
- area: mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/payouts/**
  - Mobile/test/features/payouts/**
  - Mobile/ARCHITECTURE.md
- acceptance:
  - Plan reference: MOB-M6-01/02/03/04. Problem with evidence: grep -rn "RatingScreenArgs\|DisputeScreenArgs" Mobile/lib finds nothing outside features/ratings and features/disputes: no screen opens /ratings or /disputes, so the rating and dispute screens cannot be reached. The booking and tracking screens that should open them belong to M2 (MOB-M2-04/06, not built; booking_screen.dart is a placeholder) and M4 (MOB-M4-03/05). The leader gave no answer and set tonight as the deadline, so the one list of finished jobs that M6 owns and that exists on main, the worker's earnings (/payouts), gets the entry points.
  - On each job tile of PayoutScreen (not on an absence-fee job: it has no completed work to rate and the absence fee is the customer's to dispute): "Đánh giá khách" opens /ratings with RatingScreenArgs(role: worker, assignmentId) and "Khiếu nại" opens /disputes with DisputeScreenArgs(role: worker, orderId). PayoutScreen takes optional onRate / onDispute callbacks (tests); by default they push the named routes with those arguments. No route or package is added; lib/app/** untouched.
  - Tests (output pasted): the callbacks receive the right job; the default navigation pushes /ratings and /disputes with the right arguments; an absence-fee job shows neither button. flutter analyze on the touched folders 0 issues, flutter test test/features/payouts passes.
  - Update Mobile/ARCHITECTURE.md (and state in it that the customer side still needs M2's screens to call the same two routes).

## #225 [ticket] BE-M6-02e Docs: DisputeResolved consumers must not refund or penalise again (M6 - Anh)
- status: in-progress
- area: docs
- blocked-by: -
- allowed:
  - .spec/contracts/disputes.md
  - Backend/ARCHITECTURE.md
- acceptance:
  - Plan reference: BE-M6-02 (open point "double effect with M2/M5"). DisputeVerdictService already applies the money effects of a verdict BEFORE publishing DisputeResolved: IRefundService.RefundAsync for the customer (when compensationAmount > 0) and ISlaPenaltyService.ApplyAsync for an upheld AGENCY quality dispute, inside the verdict's transaction (Application/Features/Disputes/Services/DisputeVerdictService.cs). The contract text says the consumers are "M5 ISlaPenaltyService bookkeeping, M2 refund bookkeeping", and Domain/Events/DisputeResolved.cs says the event "triggers escrow penalties (M5) and/or refunds (M2)": a reader can implement a handler that refunds or penalises a second time. The leader gave no answer and set tonight as the deadline.
  - Documentation only: state in .spec/contracts/disputes.md (event paragraph) and Backend/ARCHITECTURE.md (verdict section) that the money effects are already applied when the event is published, so a consumer must not call IRefundService or ISlaPenaltyService for it; the event is for bookkeeping, notifications and read models, keyed on DisputeId + AssignmentId. No code changes.
  - sh harness/verify.sh L3: report honestly if unrelated tests fail.

## #228 [ticket] MOB-M4-06 Thợ: nhận/từ chối Làm lần 2 Mobile
- status: done
- area: mobile
- blocked-by: -
- allowed:
  - Mobile/lib/features/workers/**
  - Mobile/test/features/workers/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Plan reference: MOB-M4-06. Contract: .spec/contracts/workers.md §2.6 (POST /api/workers/assignments/{assignmentId}/extension/respond).
  - Worker extension response UI / dialog on Mobile: displays extension request details (extra hours, extra amount in VND, new end time).
  - Accept button sends POST /api/workers/assignments/{assignmentId}/extension/respond with { accept: true }, updates status to ACCEPTED.
  - Decline button sends POST /api/workers/assignments/{assignmentId}/extension/respond with { accept: false }, updates status to DECLINED.
  - Widget / unit tests in Mobile/test/features/workers/ for extension screen/dialog.
  - flutter analyze 0 issues, flutter test passes, sh harness/verify.sh L3 passes.

## #229 [ticket] WEB-M4-01 Admin: hàng đợi hậu kiểm eKYC Web
- status: done
- area: frontend
- blocked-by: -
- allowed:
  - Frontend/src/features/workers/**
  - Frontend/src/features/admin/**
  - Frontend/tests/**
  - .spec/plan/M4-*.md
  - .spec/contracts/workers.md
- acceptance:
  - Plan reference: WEB-M4-01. Contract: .spec/contracts/workers.md §2.2 (GET /api/admin/workers/ekyc-queue, POST /api/admin/workers/{id}/ekyc-review).
  - Admin eKYC manual review queue screen / page on Web (desktop 1440x900):
  - - Table of pending eKYC requests showing worker name, phone, national ID (CCCD), confidence score, submission timestamp, and action buttons.
  - - Image comparison modal / drawer for reviewing front CCCD, back CCCD, and selfie photos side-by-side.
  - - Approve action sends POST /api/admin/workers/{id}/ekyc-review with { approved: true }, updates queue.
  - - Reject action opens modal for rejection reason and sends POST /api/admin/workers/{id}/ekyc-review with { approved: false, rejectionReason }, updates queue.
  - Component & integration tests in Frontend/tests/.
  - npm run lint, tsc -b PASS, sh harness/verify.sh L3 passes.

## #231 [ticket] BE-M5-00 Contract Agencies + Skills (M5 - Hoàng)
- status: in-progress
- area: shared
- blocked-by: None
- allowed:
  - Backend/Application/Features/{Agencies,Skills}/**
  - Backend/WebAPI/Controllers/{Agencies,Skills}/**
  - Backend/Infrastructure/Modules/{Agencies,Skills}/**
  - Backend/Tests/{Agencies,Skills}/**
  - Backend/Domain/Entities/*.{Agencies,Skills}.cs
  - Mobile/lib/features/agencies/**
  - Mobile/test/features/agencies/**
  - Frontend/src/features/{agencies,skills}/**
  - .spec/plan/M5-*.md
  - .spec/contracts/agencies.md
  - .spec/contracts/skills.md
- acceptance:
  - Plan reference: .spec/plan/M5-agencies-b2b.md task BE-M5-00. Read .spec/decisions.md and .spec/plan/00-overview.md sections 3-5 first.
  - Create .spec/contracts/agencies.md and .spec/contracts/skills.md documenting proposed endpoints, roles/policies, DTOs, validation and error responses for Agency registration, subscription packages, worker import, shift roster, escrow, Skills catalog, and the Agency dashboard.
  - Every rule, value, and field must come from .spec/spec.md, .spec/decisions.md, or an existing approved contract. Record anything unresolved as an open question for the leader; do not invent API or schema details. Follow the ApiResponse, camelCase, UTC, and ownership conventions in .spec/contracts/identity.md.
  - Contract approval by the leader is the acceptance condition for BE-M5-00 and opens gate G3 for Agencies and Skills. No backend, frontend, or mobile implementation in this ticket.
  - Run sh harness/verify.sh L3; include the final line and evidence log path in the PR. Tick BE-M5-00 in .spec/plan/M5-agencies-b2b.md with evidence in the same PR.

## #235 [ticket] BE-M5-06a Premium Capacity Algorithm (Agency)
- status: in-progress
- area: -
- blocked-by: -
- allowed:
  - Backend/Application/Features/{Agencies,Skills}/**
  - Backend/WebAPI/Controllers/{Agencies,Skills}/**
  - Backend/Infrastructure/Modules/{Agencies,Skills}/**
  - Backend/Tests/{Agencies,Skills}/**
  - Backend/Domain/Entities/*.{Agencies,Skills}.cs
  - Mobile/lib/features/agencies/**
  - Mobile/test/features/agencies/**
  - Frontend/src/features/{agencies,skills}/**
  - .spec/plan/M5-*.md
  - .spec/contracts/{agencies,skills}.md
- acceptance:

## #236 [ticket] BE-M5-02 Subscription packages: Free (3–5 thợ, hoa hồng 20 %) vs Pro (thu phí tháng/quý, mở Shift Roster Dashboard, báo cáo, nhãn Verified Partner, tăng quota, ưu tiên điều phối)
- status: in-progress
- area: -
- blocked-by: -
- allowed:
- acceptance:

## #237 [ticket] BE-M5-01 Đăng ký Agency (ĐKKD, MST, đại diện pháp luật); chỉ thành "đạt chuẩn" khi đủ ký quỹ tối thiểu
- status: in-progress
- area: -
- blocked-by: -
- allowed:
- acceptance:

## #238 [ticket] BE-M5-07 Escrow wallet: top-up (webhook), escrow_deposit_balance, penalty log, ISlaPenaltyService (SLA deduction + compensation; per decisions Q09; deduction order: customer refund → rescue cost → platform fee), no negative balance (if insufficient, deduct to zero, record shortage, set SUSPENDED); every balance change = 1 ESCROW_TRANSACTION append-only (SC-4); disputes within 48h, Admin reverses via REVERSAL transaction
- status: ready
- area: -
- blocked-by: -
- allowed:
- acceptance:

## #239 [ticket] BE-M5-07 Escrow wallet: top-up (webhook), escrow_deposit_balance, penalty log, ISlaPenaltyService (SLA deduction + compensation; per decisions Q09; deduction order: customer refund -> rescue cost -> platform fee), no negative balance (if insufficient, deduct to zero, record shortage, set SUSPENDED); every balance change = 1 ESCROW_TRANSACTION append-only (SC-4); disputes within 48h, Admin reverses via REVERSAL transaction
- status: in-progress
- area: -
- blocked-by: -
- allowed:
- acceptance:

## #240 [ticket] BE-M5-04 Danh mục Skill (Admin quản trị) + WorkerSkill (chọn từ danh mục, số năm kinh nghiệm, trạng thái thẩm định)
- status: ready
- area: -
- blocked-by: -
- allowed:
- acceptance:

## #241 [ticket] BE-M5-05 Shift Roster: Agency phân ca tuần cho thợ
- status: in-progress
- area: -
- blocked-by: -
- allowed:
- acceptance:

## #242 [ticket] BE-M5-10 Test: quota, capacity race conditions, escrow balance not negative, bulk import error reporting per line, Pro/Free feature access
- status: ready
- area: -
- blocked-by: -
- allowed:
- acceptance:

## #243 [ticket] BE-M5-09 Cửa sổ 30 phút (Pha 2): Agency tự đổi thợ nội bộ trong hạn, hết hạn/≤2 h → trả quyền cho M3
- status: ready
- area: -
- blocked-by: -
- allowed:
- acceptance:

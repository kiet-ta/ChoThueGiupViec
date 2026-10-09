# ARCHITECTURE.md — CommonService

> Guide for AI agents (Claude, Antigravity, Codex, ...) and developers. Read this before changing code.

## 1. Overview
- ASP.NET Core Web API, **.NET 10** (`net10.0`), Nullable + ImplicitUsings enabled.
- Single project (`CommonService.csproj`), layered by **folders/namespaces** following **Clean Architecture + CQRS (MediatR)**.
- Purpose: a reusable "common service" template (users, orders, payment via Stripe, caching, email, standard response/error middleware).
- Packages: `MediatR 13`, `Stripe.net 48`, `Swashbuckle.AspNetCore 9`, `Microsoft.AspNetCore.OpenApi`.
- Root namespace: `CommonService`. Port (dev): http `5004`, https `7204`. Swagger is enabled in Development only.

## 2. Folder structure & responsibilities

```
Program.cs                 Composition root: AddControllers, AddApplication, AddInfrastructure, Swagger, middleware
Domain/                    Pure business model. NO dependency on other layers
  Entities/                Customer, Worker, JobOrder, JobAssignment, etc. (27 domain entities)
  ValueObjects/            Email, Vnd (immutable, validated)
  Events/                  Domain events (OrderPaid, JobAssigned, etc.)
  Services/                Domain services - logic spanning multiple entities
Application/               Use cases. Depends on Domain only
  Features/<Feature>/      Commands/, Queries/, Handlers/ (MediatR) e.g. Features/<Module>/
  Behaviors/               MediatR pipeline: Validation, Logging, Performance
  Interfaces/              IRepositories/ (IRepository, IUnitOfWork), IServices/ (ICacheService, IEmailService), Ports/
  Services/                Application services (EmailService)
  Common/                  Models (ApiResponse<T>), Helpers (PaginatedList), Constants
  Exceptions/              NotFound, Validation, ForbiddenAccess, BusinessRuleViolation
  DependencyInjection.cs   AddApplication(): MediatR + ValidationBehavior
Infrastructure/            Implements ports. Depends on Application (+Domain)
  Persistence/             AppDbContext, UnitOfWork, Repositories/EfRepository
  Services/                InMemoryCacheService (default), RedisCacheService (optional)
  DependencyInjection.cs   AddInfrastructure(config): db, cache, unit of work, modules, fakes
Middleware/                ErrorHandling, RequestResponseLogging, ResponseWrapper
WebAPI/Controllers/        Thin controllers: ValuesController, module controllers
Tests/                     CommonService.Tests.csproj (xUnit), e2e/, integration/, GoldenRule.md (testing philosophy)
Properties/launchSettings.json
```

### 2.1 Module folder convention (MVP5, 12 modules)
Modules: `Identity, Customers, Booking, Payments, Dispatch, Workers, Agencies, Skills, Ratings, Disputes, Payouts, Admin`.
Each module owns exactly these four folders (exclusive: nobody else edits them):

| Layer | Path |
|---|---|
| Application (commands/queries/handlers/validators) | `Application/Features/<Module>/` |
| Controllers (thin) | `WebAPI/Controllers/<Module>/` |
| Infrastructure (EF config, repos, adapters) | `Infrastructure/Modules/<Module>/` |
| Tests | `Tests/<Module>/` |

Domain behaviour of a module is added as `partial class` files named `Domain/Entities/<Entity>.<Module>.cs`.
Frontend/Mobile counterparts: `Frontend/src/features/<module>/`, `Mobile/lib/features/<module>/`.
Empty folders are kept with a `.gitkeep` (delete it when the first real file lands). The demo `Application/Features/Users` is template code, removed by BASE-09.
Plan and ownership: `.spec/plan/00-overview.md` section 3.

### 2.3 Module registration (`IModule`)
Adding a module needs **no edit** to `Program.cs` or to a shared `DependencyInjection.cs`:
- A module is a **public** class with a public parameterless constructor implementing `CommonService.Infrastructure.Modularity.IModule`. Convention: `Infrastructure/Modules/<Module>/<Module>Module.cs`.
- `ConfigureServices(services, configuration)`: register the module's services, options, hosted/startup tasks (for example the dev admin seeder) and, later, its EF configurations. `MapEndpoints(endpoints)` (optional): map non-controller endpoints such as a SignalR hub.
- `Infrastructure/DependencyInjection.cs` calls `AddModules(configuration, <this assembly>)` once, **last**; `Program.cs` calls `MapModuleEndpoints()` once after `MapControllers()`. Both calls are written once in BASE-02 and are frozen.
- The scan finds public, concrete `IModule` types only. Modules are applied in ordinal name order (deterministic); a duplicate `Name` or a missing public parameterless constructor fails at startup with a clear message.
- Already automatic, nothing to register: **controllers** (MVC scans the application assembly), **MediatR handlers** (`AddMediatR` scans the assembly in `Application/DependencyInjection.cs`), **validators** (`ValidationBehavior` validates the DataAnnotations on each request; there is no validator class to register).
- Tests: `Backend/Tests/Modularity/ModuleLoaderTests.cs` (a fake module in the test assembly resolves its service without touching `Program.cs`).

### 2.4 Domain model (BASE-06): 27 entities, 3 state machines
- **27 tables** = 22 of `GiupViec_Physical_DB_MVP5.drawio` + 5 of `.spec/decisions.md` section 3 (`PRICE_RULE`, `ADMIN_AUDIT_LOG`, `ESCROW_TRANSACTION`, `OTP_CODE`, `REFRESH_TOKEN`), plus the added columns of SC-1 (`PARTNER_AGENCY` password/guarantee) and SC-8 (`failed_login_count`, `locked_until` on `ADMIN` and `PARTNER_AGENCY`). `TWO_WAY_RATING` UNIQUE (SC-5) is a persistence rule for BASE-07. The drawio title says "20 bảng" but the file contains 22 table cells (counted by script); the plan says 22.
- `Domain/Entities/<Entity>.cs`: `public partial class`, one scalar property per column (generated once from the drawio, then maintained by hand). **No navigation properties** (0-JOIN queries, PRD 5.2). Behaviour of a module is added in `<Entity>.<Module>.cs` partial files.
- Types: `INT/BIGINT/TINYINT` -> `int/long/byte`, `DECIMAL` -> `decimal` (money = `DECIMAL(18,2)`, whole VND after rounding, decisions G-2), `DATETIME2` -> `DateTime` (UTC, G-3), `DATE/TIME` -> `DateOnly/TimeOnly`, `BIT` -> `bool`. `CustomerAddress.TotalAreaM2` is the computed `floor_area_m2 * num_floors`.
- `Worker` is Single Table Inheritance: `WorkerType` and `AgencyId` have private setters and are set only by `Worker.CreateFreelancer` / `Worker.CreateAgencyStaff`, which enforces the drawio CHECK (freelancer has no agency, agency staff has one).
- **State machines** (`Domain/StateMachines`, explicit allow-lists; anything not listed throws `InvalidStateTransitionException`): `JobOrderStateMachine` (order_status), `JobAssignmentStateMachine` (assignment_status, the flat execution node), `WorkerStateMachine` (work_status). The status properties have private setters; use `entity.TransitionTo(next)`. A new entity starts in the first enum member. Tests list the expected transitions independently of the code and check every (from, to) pair (`Backend/Tests/Domain/StateMachineTests.cs`).
- Enum members are PascalCase; the database spelling is UPPER_SNAKE_CASE (`CheckedIn` -> `CHECKED_IN`) through `DbEnum`, which the EF value converters of BASE-07 must use. A test checks that every enum value fits its `VARCHAR(n)` column.

**Columns left as `string` on purpose.** The PRD, the drawio and `decisions.md` do not list their values, and inventing them is forbidden (AGENTS.md rule 5). The owning module pins the values in its `*-00` contract, then turns the column into an enum:
`PRICE_RULE.area_bracket`, `JOB_ORDER.shift_code`, `BOOKING_SLOT.shift_code/slot_source/slot_status`, `JOB_ORDER_EXTENSION.worker_decision/ext_status`, `PAYMENT_TRANSACTION.gateway`, `CHECK_IN_LOG.fallback_method`, `JOB_PHOTO.photo_phase`, `TWO_WAY_RATING.rater_role`, `INCIDENT_LOG.incident_type/redispatch_status`, `DISPUTE_TICKET.raised_by/category/dispute_status`, `WORKER.kyc_status`, `PARTNER_AGENCY.agency_status`, `PARTNER_SUBSCRIPTION.sub_status`, `SUBSCRIPTION_PACKAGE.tier/billing_cycle`, `PAYOUT_BATCH.batch_status`, `PAYOUT_ITEM.item_status`, `ADMIN.admin_role` (entity `AdminAccount`), `SKILL.category`.

**Enum values that are M1 proposals** (not stated verbatim by the sources; the owning module may rename them in its contract before any code uses them): `WorkStatus.Pending/Locked` (only `IDLE` is in the PRD; `BUSY` is defined by D6), `PaymentPurpose.Order`, and every transition of the three machines beyond the paths quoted in the XML comments of the machine classes.

**Decisions of 2026-10-04 (D1-D7, recorded in `.spec/decisions.md` by DECISIONS-02):**
- D1 Offers are persisted: an assignment is created as `OFFERED` and `JOB_ASSIGNMENT.accepted_at` is nullable (SC-9).
- D2 The unique index `Job_Assignment(slot_id)` excludes `CANCELLED`, `CANCELLED_BY_WORKER` and `REASSIGNED` (a worker who cancels gets the slot back; the violation still counts, Q15). Built in BASE-07.
- D3 `DISPUTE_TICKET.fault_party` is the enum `FaultParty` = `FREELANCER | AGENCY | CUSTOMER`, null = nobody at fault. "WORKER" in decisions Q10/Q12 means FREELANCER or AGENCY.
- D4 The ADMIN table is the entity `AdminAccount` (avoids the clash with the module namespace `...Admin`).
- D5 Currency amounts stay `decimal` in entities; **all rounding goes through `Domain/ValueObjects/Vnd.cs`** (`Vnd.Round`, `Vnd.Commission`, `Vnd.Net`, decisions G-2). The template demo value objects were removed in BASE-09.
- D6 `WorkStatus.Busy` = on site, from check-in until the assignment is COMPLETED or an absence is approved. A future assignment does not change `work_status`; double booking is prevented by the slot UNIQUE.
- D7 The order status follows its assignments through `JobOrder.SyncWithAssignments`: ASSIGNED when `required_workers` assignments are accepted, COMPLETED when that many are COMPLETED, back to DISPATCHING when a seat is lost (only the missing seat is re-dispatched). ABSENT is left to the Admin approval flow. Tests: `Backend/Tests/Domain/JobOrderProgressTests.cs`.

**Still persistence work for BASE-07:** the drawio CHECKs on `PAYMENT_TRANSACTION` (exactly one of order/extension/subscription) and `PAYOUT_ITEM` (payee type vs worker/agency) become database CHECK constraints; `DbEnum` value converters; UTC enforcement on every `DateTime` (G-3).

The demo template classes were removed in BASE-09; all domain logic uses the 27 real MVP5 entities and modules.

### 2.5 Cross-module ports (BASE-03)
Modules never call each other: they use a **port** (interface + DTOs in `Application/Interfaces/Ports/`, namespace `CommonService.Application.Interfaces.Ports`) or a domain event. Every port has an in-memory **Fake** in `Infrastructure/Fakes/` registered by `AddFakePorts()` with `TryAdd`. `Infrastructure/DependencyInjection.cs` calls `AddModules(...)` first and `AddFakePorts()` **last**, so a real implementation registered by a module (`IModule.ConfigureServices`) wins and a port nobody implemented yet falls back to its Fake. Changing a port needs a "Scope exception" issue for M1.

| Port | Implementer (real) | Consumers | Fake |
|---|---|---|---|
| `IPaymentGateway` (QR, verify IPN, query status, refund) | M2 (MoMo sandbox, Q04) | M2, M5 | `FakePaymentGateway` (IPN keys `gatewayTxnRef`, `amount`, `status`, `signature=valid`) |
| `IRefundService` | M2 | M3 | `FakeRefundService` |
| `IAgencyCapacityService` (check, atomic reserve, release) | M5 | M2, M3 | `FakeAgencyCapacityService` |
| `ISlaPenaltyService` (SLA points + escrow, Q09) | M5 | M3, M6 | `FakeSlaPenaltyService` |
| `IWorkerAvailabilityQuery` | M4 | M3 | `FakeWorkerAvailabilityQuery` |
| `IWorkerReputation` | M6 | M3 | `FakeWorkerReputation` (replaced by the real `EfWorkerReputation`, section 7.2a) |
| `IWorkerProfileQuery` (Q21 C3) | M4 | M1 (favorite workers) | `FakeWorkerProfileQuery` |
| `IAuditLog` (append-only, G-5) | M6 | M2, M5, M6 | `FakeAuditLog` (replaced by the real `EfAuditLog`, section 7.6) |
| `IEkycProvider` (Fake only, Q05) | M4 | M4 | `FakeEkycProvider` (confidence 92.00) |
| `IImageQualityService` (VoL, Q03) | M4 | M4 | `FakeImageQualityService` |
| `INotificationService` (SignalR, Q07) | M1 (BASE-12) | all | `FakeNotificationService` |
| `IOtpSender` (Q06) | M1 (SMS deferred, Q06b) | M1 | `FakeOtpSender` (**refused outside Development**: constructor throws, and `OtpSenderStartupGuard` resolves it at host start so the startup fails) |
| `IPasswordHasher` | M1 (BASE-10) | M1 | `FakePasswordHasher` (unsalted SHA-256, tests only) |
| `IFileStorage` | M1 (BASE-11) | M4, M6 | `FakeFileStorage` |
| `IClock` (single UTC <-> Asia/Ho_Chi_Minh conversion, G-3) | M1 (BASE-11) | all | `FakeClock` (UTC+7, pinnable) |
| `ICurrentUser` | M1 (BE-M1-02) | all | `FakeCurrentUser` |
| `IGeoService` | M1 (BASE-11) | all | `FakeGeoService` (haversine) |

Tests: `Backend/Tests/Ports/FakePortTests.cs`.

### 2.6 Domain events catalog (BASE-04)
Cross-module asynchronous communication and side effects go through MediatR notifications (`Domain/Events/`, namespace `CommonService.Domain.Events`). Handlers are discovered automatically via MediatR assembly scanning (`Application/DependencyInjection.cs`). Modules communicate without direct references:

| Event | Producer | Consumers | Payload Fields | Description |
|---|---|---|---|---|
| `OrderPaid` | M2 | M3 | `long OrderId`, `int CustomerId`, `decimal Amount`, `string ShiftCode`, `DateTime ScheduledDate`, `int RequiredWorkers` | Order paid; begins matching. |
| `OrderCancelled` | M2 | M2 | `long OrderId`, `int CustomerId`, `string Reason`, `DateTime CancelledAtUtc` | Order cancelled. |
| `OrderRefunded` | M2 | M2 | `long OrderId`, `int CustomerId`, `decimal Amount`, `string Reason`, `DateTime RefundedAtUtc` | Customer refunded. |
| `JobAssigned` | M3 | M3 | `long AssignmentId`, `long OrderId`, `int WorkerId`, `int? AgencyId`, `int SlotId`, `DateTime AssignedAtUtc` | Worker assigned to job seat. |
| `AssignmentFailed` | M3 | M2 | `long OrderId`, `string Reason`, `DateTime FailedAtUtc` | Dispatch exhausted; triggers 100% refund. |
| `WorkerCheckedIn` | M3 | M3 | `long AssignmentId`, `long OrderId`, `int WorkerId`, `DateTime CheckedInAtUtc`, `double DistanceMeters` | Worker GPS check-in verified. |
| `CustomerAbsentReported` | M3 | M6 | `long AssignmentId`, `long OrderId`, `int WorkerId`, `DateTime ReportedAtUtc` | Worker reported customer absent after 15m. |
| `CustomerAbsentApproved` | M6 | M2, M3, M4 | `long AssignmentId`, `long OrderId`, `int WorkerId`, `decimal CompensationAmount`, `decimal RefundAmount`, `DateTime ApprovedAtUtc` | Admin approved absence (worker 40%, customer refunded 60%). |
| `IncidentReported` | M3 | M3 | `long IncidentId`, `long AssignmentId`, `long OrderId`, `int WorkerId`, `string IncidentType`, `string Description`, `DateTime ReportedAtUtc` | On-site incident reported. |
| `JobCompleted` | M4 | M6, M5 | `long AssignmentId`, `long OrderId`, `int WorkerId`, `int? AgencyId`, `decimal PayoutAmount`, `DateTime CompletedAtUtc` | Work accepted; opens rating window & payout item. |
| `ExtensionPaid` | M2 | M4 | `long ExtensionId`, `long OrderId`, `int WorkerId`, `int ExtraHours`, `decimal ExtraAmount`, `DateTime PaidAtUtc` | Extra time paid by customer. |
| `ExtensionDeclined` | M4 | M3 | `long ExtensionId`, `long OrderId`, `int WorkerId`, `string Reason`, `DateTime DeclinedAtUtc` | Worker declined extension. |
| `SubscriptionActivated` | M5 | M5 | `long SubId`, `int AgencyId`, `int PackageId`, `string PackageCode`, `DateTime StartDateUtc`, `DateTime EndDateUtc` | Agency subscription activated. |
| `DisputeResolved` | M6 | M5, M2 | `int DisputeId`, `long AssignmentId`, `FaultParty? FaultParty`, `decimal CustomerRefundAmount`, `string ResolutionNotes`, `DateTime ResolvedAtUtc` | Dispute ticket resolved. |
| `RatingSubmitted` | M6 | M6 | `long RatingId`, `long AssignmentId`, `string RaterRole`, `int Stars`, `DateTime CreatedAtUtc` | 2-way rating submitted. |
| `PayoutBatchClosed` | M6 | M6 | `int BatchId`, `string PeriodMonth`, `decimal TotalAmount`, `int ItemCount`, `DateTime ClosedAtUtc` | Monthly payout batch closed. |

Tests: `Backend/Tests/Events/DomainEventTests.cs`.

### 2.2 Frozen shared files
After gate G0 these are **not edited by hand** by anyone except the M1 owner (modules self-register through `IModule`, BASE-02):
`Program.cs`, `Application/DependencyInjection.cs`, `Infrastructure/DependencyInjection.cs`, the DbContext, `Migrations/**`, `appsettings*.json`, `Mobile/lib/app/**`, `Frontend/src/app/**`.
Schema changes are made only by M1 (open a "Scope exception" issue). Modules talk to each other only through ports or domain events defined by M1 (BASE-03/04).

Layer docs already exist: `Domain/Domain.md`, `Application/Application.md`, `Application/Features/Features.md`, `Infrastructure/Infrastructure.md`, `Middleware/Middleware.md`, `Tests/GoldenRule.md`, `BasicIntegrationTemplate.md` (template excluded from compile, see csproj), `document.md`.

## 3. Dependency rule
```
WebAPI → Application → Domain
Infrastructure → Application → Domain
```
Dependencies point **inward**. Domain knows nothing about Application/Infrastructure/ASP.NET. Application defines interfaces; Infrastructure implements them; DI wires them in `Program.cs`.

## 4. Request flow
```
HTTP → ErrorHandlingMiddleware → RequestResponseLoggingMiddleware → ResponseWrapperMiddleware
     → Controller → IMediator.Send(Command/Query)
     → Pipeline behaviors (Validation → ...) → Handler → IRepository / IService (Infrastructure)
     → Domain entities/value objects → result → ApiResponse<T> → JSON
```
- Responses are wrapped as `ApiResponse<T> { Success, Message, Data }`.
- Application exceptions are mapped to HTTP status codes in `ErrorHandlingMiddleware`.
- Middleware order in `Program.cs` matters; keep error handling first.

## 5. Conventions for adding code
**New feature (e.g. Orders):**
1. Domain: entity/value object/event in `Domain/`.
2. Application: `Features/Orders/Commands|Queries/*.cs` (`IRequest<T>`) + `Handlers/*Handler.cs`; add repo interface to `Interfaces/IRepositories`.
3. Infrastructure: implement repo in `Infrastructure/Persistances`, register in `Infrastructure/DependencyInjection.cs` (`AddScoped`).
4. WebAPI: thin controller that only calls `IMediator`; no business logic in controllers.
5. Validation: FluentValidation-style validators picked up by `ValidationBehavior` (add package if missing).

**Rules**
- Don't reference Infrastructure from Application/Domain.
- Use value objects (e.g. `Email`, `Vnd`) instead of primitives for domain concepts.
- Throw Application exceptions (`NotFoundException`, ...) rather than returning status codes from handlers.
- Cache via `ICacheService` (swap InMemory ↔ Redis in Infrastructure DI; Redis needs `ConnectionStrings:Redis`).
- Tests: follow `Tests/GoldenRule.md` — use cases are tested with mocks/stubs, never real DB/external APIs; don't test across domains.
- Namespaces mirror folders (`CommonService.Application.Features...`). Keep new code consistent with existing style; preserve existing comments/docs.

### 5.1 Repository and UnitOfWork conventions (BASE-08)
- **Unit of Work**: `IUnitOfWork` (`Application/Interfaces/IRepositories/IUnitOfWork.cs`) encapsulates atomic transaction boundaries and `SaveChangesAsync`. Implemented by `UnitOfWork` (`Infrastructure/Persistence/UnitOfWork.cs`) backed by `AppDbContext`.
- **Repository abstraction**: `IRepository<TEntity, TId>` (`Application/Interfaces/IRepositories/IRepository.cs`) provides aggregate root data access primitives (`GetByIdAsync`, `ListAllAsync`, `AddAsync`, `Update`, `Delete`). Base implementation `EfRepository<TEntity, TId>` lives in `Infrastructure/Persistence/Repositories/EfRepository.cs`.
- **Module repositories**: Modules declare custom aggregate repository interfaces inside their own feature folder (`Application/Features/<Module>/`), implement them inside their own infrastructure folder (`Infrastructure/Modules/<Module>/`), and register them in their own `IModule.ConfigureServices`. Shared files are not modified.
  - Example: `ICustomerRepository` in `Application/Features/Customers/ICustomerRepository.cs`, implemented by `CustomerRepository` in `Infrastructure/Modules/Customers/CustomerRepository.cs`, registered by `CustomersModule`.
- **Atomicity**: Changes across one or multiple repositories are saved atomically via `IUnitOfWork.SaveChangesAsync()` or transaction blocks via `IUnitOfWork.ExecuteInTransactionAsync()` / `BeginTransactionAsync()` / `CommitTransactionAsync()`.
- Tests: `Backend/Tests/Persistence/RepositoryAndUnitOfWorkTests.cs`.

## 6. Configuration & secrets (IMPORTANT)
- `appsettings.json` contains **placeholders only** (`Stripe:PublishableKey = pk_test_xxx`, `Stripe:SecretKey = sk_test_xxx`). **Never commit real keys.**
- Provide real values via:
  - `dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."` (local dev), or
  - environment variables: `Stripe__SecretKey`, `Stripe__PublishableKey`, or
  - an untracked `.env` / `appsettings.*.local.json` (both gitignored).
- Agents must not print, hardcode, or commit secrets, connection strings, tokens or certificates.

## 7. Build / run / test
```
dotnet restore
dotnet build
dotnet run            # http://localhost:5004 , Swagger at /swagger (Development)
dotnet test            # runs Tests/CommonService.Tests.csproj (xUnit); CommonService.csproj excludes Tests/** from compilation
```
- **Tests run one after the other** (`[assembly: CollectionBehavior(DisableTestParallelization = true)]` in `Tests/Admin/SharedDatabase.cs`, BE-M6-08b): about 30 test classes write to and count rows of the same local SQL Server database, so a count such as "open disputes" saw rows another class had seeded at that moment. The full suite took 9-11 s in parallel and 19-33 s serially on the author's machine.

### 7.1 Local database (SQL Server, BASE-07)
- Provider: Microsoft SQL Server (Windows default instance: `localhost`, database: `ChoThueGiupViec`).
- `AppDbContext` (`Infrastructure/Persistence/AppDbContext.cs`) implements all 27 tables of the MVP schema with `IEntityTypeConfiguration<T>` per entity.
- Connection string in `appsettings.Development.json`:
  `Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;`
- Connection string placeholder in `appsettings.json`:
  `Server=YOUR_SERVER;Database=ChoThueGiupViec;Trusted_Connection=True;TrustServerCertificate=True;`
- EF Core CLI commands inside `Backend/`:
  ```bash
  dotnet ef database update
  dotnet ef migrations add <MigrationName> --output-dir Infrastructure/Persistence/Migrations
  dotnet ef migrations remove
  ```
- Persistence tests: `Tests/Persistence/AppDbContextModelTests.cs` (EF Core model metadata & conventions) and `Tests/Persistence/SqlServerPersistenceTests.cs` (live SQL Server schema, unique constraints, filtered indexes, CHECK constraints, UTC DateTime converter).
- **Silent skips (check this before trusting a green run):** every DB-backed test starts with `IsSqlServerAvailable()`, which swallows all exceptions and then returns early, counted as a pass. It skips when SQL Server or the database `ChoThueGiupViec` is missing (`dotnet ef database update` creates it) **and whenever `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` is set**, because `Microsoft.Data.SqlClient` throws `NotSupportedException` in invariant globalization mode. Run the suite without that variable to execute them.

### 7.2 Data seeding and password hashing (BASE-10)
- `Pbkdf2PasswordHasher` (`Infrastructure/Modules/Identity/Pbkdf2PasswordHasher.cs`) implements `IPasswordHasher` using PBKDF2 with HMAC-SHA256 (100,000 iterations, 16-byte salt, 32-byte subkey, format `pbkdf2$sha256$...`). Registered via `IdentityModule : IModule` and takes precedence over `FakePasswordHasher`.
- `AdminSeeder` (`Infrastructure/Modules/Identity/AdminSeeder.cs`): runs on application startup strictly in **Development** only (overview §10). Seeds dev admin (`admin@dev.local`, role `SUPER_ADMIN`) from `Seed:Admin:*` config with hashed password (never logged). Idempotent: does nothing if account exists.
- `DefaultDataSeeder` (`Infrastructure/Persistence/DefaultDataSeeder.cs`): seeds required reference data in **all** environments:
  - 6 `PRICE_RULE` rows (`decisions.md` §2 Q01)
  - 3 `SUBSCRIPTION_PACKAGE` rows (`FREE`, `PRO_MONTHLY`, `PRO_QUARTERLY`, `decisions.md` §2 Q08)
  - 4 sample `SKILL` rows (catalog standard `CLEAN_BASIC`, `DEEP_CLEAN`, `IRONING`, `COOKING`)
- `DatabaseSeederHostedService` (`Infrastructure/Modules/Identity/DatabaseSeederHostedService.cs`): registers as `IHostedService` in `IdentityModule` to execute the seeders on startup without touching `Program.cs`. Tests: `Tests/Persistence/DatabaseSeederTests.cs` and `Tests/Identity/Pbkdf2PasswordHasherTests.cs`.

### 7.2a Worker reputation (`IWorkerReputation`, BE-M6-01b)
- Real implementation `Infrastructure/Modules/Ratings/EfWorkerReputation.cs`, registered by `WorkerReputationModule` (its own `IModule`), so it wins over `FakeWorkerReputation`. The port is unchanged: `Task<WorkerReputationDto?> GetAsync(int workerId, ct)` returning `WorkerReputationDto(WorkerId, RatingAvg, CompletedJobs, SuccessRate)`, `null` for an unknown worker. Read-only, `AsNoTracking`, three queries at most.
- **Definitions live in `Application/Features/Ratings/ReputationFormula.cs`** (change only there): `RatingAvg` = average stars of the worker's `CUSTOMER` ratings, 2 decimals, half away from zero, 0 without ratings (worker-to-customer ratings are not counted); `CompletedJobs` = assignments in `COMPLETED`; `SuccessRate` = `completed / (completed + CANCELLED_BY_WORKER)`, 3 decimals, 0 when there is no such job. Customer-caused `ABSENT`, `INCIDENT`, `REASSIGNED`, a system `CANCELLED`, `OFFERED` and the running states do not count against the worker. This follows the port's comment and the recommended default of contract `ratings.md` question M3; it is an assumption the team can change in that one method.
- Lifetime: scoped (shares the request's `AppDbContext`); a singleton must not take `IWorkerReputation` (same rule as `IAuditLog`).
- Tests: `Tests/Ratings/WorkerReputationTests.cs` (the DB test seeds a worker with every assignment status and ratings of both sides and removes them; run without `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`).

### 7.3 Shared infrastructure and helpers (BASE-11)
- `SystemClock` (`Infrastructure/Services/SystemClock.cs`): implements `IClock` (decisions G-3), converting UTC to/from Asia/Ho_Chi_Minh (UTC+7) across both Windows and IANA timezone providers.
- `ClaimsCurrentUser` (`Infrastructure/Services/ClaimsCurrentUser.cs`): implements `ICurrentUser`, resolving authenticated `UserId` and `UserRole` (`Customer`, `Worker`, `Partner`, `Admin`) from HttpContext claims.
- `HaversineGeoService` (`Infrastructure/Services/HaversineGeoService.cs`): implements `IGeoService` calculating great-circle distances in meters (Earth radius 6,371,000 m).
- `LocalFileStorage` (`Infrastructure/Services/LocalFileStorage.cs`): implements `IFileStorage` storing uploaded binaries locally with path traversal protection.
- `PaginatedList<T>` (`Application/Common/Helpers/PaginatedList.cs`): reusable pagination collection and factory.
- `ProblemDetailsHelper` (`Application/Common/Helpers/ProblemDetailsHelper.cs`): RFC 7807 problem details generator and bidirectional conversion to/from `ApiResponse`.
- `CacheIdempotencyService` (`Infrastructure/Services/CacheIdempotencyService.cs`) and `IdempotencyHelper` (`Application/Common/Helpers/IdempotencyHelper.cs`): implements `IIdempotencyService` backed by `ICacheService` to prevent double-click and webhook replay attacks.
- Middleware:
  - `ErrorHandlingMiddleware`: maps exceptions (`ValidationException` -> 400 with field errors, `NotFoundException` -> 404, `ForbiddenAccessException` -> 403, `BusinessRuleViolationException` -> 409, unhandled -> 500 with sanitized message) to camelCase `ApiResponse<object>` or RFC 7807 `application/problem+json` when requested.
  - `ResponseWrapperMiddleware`: wraps 2xx responses into `ApiResponse<T>.Ok`, non-2xx responses into `ApiResponse<T>.Fail`, normalizes to camelCase, skips Swagger/health/hubs, and prevents double-wrapping.
- Tests: `Tests/Infrastructure/*Tests.cs` (567 total tests passing).

### 7.4a Super-Freelancer (BE-M6-09c, decisions Q12 and G-5, contract `admin.md` 2.5)
- `POST` and `DELETE /api/admin/workers/{workerId}/super-freelancer` (`WebAPI/Controllers/Admin/AdminSuperFreelancerController.cs`, policy `AdminOnly`, body `{ "reason" }`). Approve needs rating average >= 4.80 and completed jobs >= 50 (both from `IWorkerReputation`, 4.80 / 50 from `BusinessRules.SuperFreelancer`), `kyc_status` approved, and no dispute with `fault_party = FREELANCER` resolved in the last 180 days for an order of the worker; a refusal is a 409 whose `data.failedCriteria` lists every failing criterion (`RATING_BELOW_MINIMUM`, `COMPLETED_JOBS_BELOW_MINIMUM`, `KYC_NOT_APPROVED`, `UPHELD_DISPUTE_RECENT`; an agency worker is `NOT_FREELANCER`). Approving a super worker or revoking a non-super one is an idempotent 200 that writes nothing.
- **One unit of work:** the service changes the tracked `Worker` and calls `IAuditLog.WriteAsync` before one `SaveChangesAsync`, so the flag and its `ADMIN_AUDIT_LOG` row (`entity_type = WORKER`, `field_name = is_super_freelancer`, `false`/`true`, the reason) commit together.
- **Auto revoke:** `RatingSubmittedSuperFreelancerHandler` (MediatR) reacts to CUSTOMER ratings only, finds the worker through the assignment (the event has no worker id) and, when the worker's average is below `RevokeBelowRating` (4.70; exactly 4.70 keeps the flag), clears the flag with a `SYSTEM` audit row (no admin id, reason `rating below 4.70`). The average comes from `IWorkerReputation`, so it is correct only once the real implementation (BE-M6-01b) is registered; with the Fake a null reputation does nothing.
- **Assumptions:** `WORKER.kyc_status` has no defined values yet; `SuperFreelancerService.KycApprovedStatus = "APPROVED"` (case-insensitive) is the single place to align with M4. The service reads and writes `WORKER` (public `IsSuperFreelancer` setter) directly, as the plan assigns the flag to M6; no command port was added (contract question A7).
- **Format numbers with the invariant culture:** the audit reason is built with `CultureInfo.InvariantCulture`; the first version printed `4,70` on a machine with comma decimals, caught by a test.
- **Schema fact to remember:** `DISPUTE_TICKET` has a UNIQUE index on `order_id` (`DisputeTicketConfiguration.cs:17`), so an order can have only one dispute ticket in total; this matters for the Disputes module (contract question D5 assumed one per role).
- Tests: `Tests/Admin/SuperFreelancerTests.cs` (DB tests seed an admin, customer, order(s), workers, slots, assignments and dispute tickets and remove them).

### 7.4 Realtime and notification channel (SignalR, BASE-12, decisions Q07)
- **SignalR Hub** (`Infrastructure/Realtime/NotificationHub.cs`): mapped at `/hubs/notifications` via `RealtimeModule : IModule`.
  - Strong-typed client: `INotificationClient.ReceiveNotification(NotificationMessageDto notification)`.
  - Group naming:
    - User group: `{role}:{userId}` (e.g. `customer:101`, `worker:55`, `admin:1`). Connections automatically join their group on connect when authenticated via JWT claims (`sub`, `role`).
    - Topic group: `topic:{topic}` (e.g. `topic:payment.status`, `topic:job.tracking`). Clients join via `JoinTopic(topic)`.
  - Hub methods: `JoinUserGroup(role, userId)`, `LeaveUserGroup(role, userId)`, `JoinTopic(topic)`, `LeaveTopic(topic)`.
- **Service** (`Infrastructure/Realtime/SignalRNotificationService.cs`): implements `INotificationService` (dispatches to user and topic groups, and records into `INotificationStore`). Registered via `RealtimeModule` and takes precedence over `FakeNotificationService`.
- **Polling Fallback Contract** (`WebAPI/Controllers/NotificationsController.cs`):
  - `GET /api/notifications/poll`:
    - Query params: `role` (optional if authenticated), `userId` (optional if authenticated), `since` (ISO-8601 UTC timestamp, optional), `limit` (1-100, default 50).
    - Response 200: `ApiResponse<IReadOnlyList<NotificationMessageDto>>`.
    - Response 400: missing `role`/`userId` when unauthenticated.
- **FCM Push Notification**: DEFERRED per decision Q07b (not implemented in MVP).
- Tests: `Tests/Infrastructure/SignalRNotificationTests.cs` (574 total tests passing).

### 7.4b Disputes: filing, queue, case file, take (BE-M6-02a, contract `disputes.md`, PRD 4.3)
- **Endpoints:** `POST/GET api/customers/me/disputes` (`CustomerOnly`), `POST/GET api/workers/me/disputes` (`WorkerOnly`) with `GET .../{disputeId}`, and for the Admin (`AdminOnly`) `GET api/admin/disputes` (queue), `GET api/admin/disputes/{disputeId}` (case file) and `POST api/admin/disputes/{disputeId}/take`. Ids come from `ICurrentUser`. The verdict (`POST .../resolve`) is section 7.4c.
- **One ticket per order:** `DISPUTE_TICKET` has a UNIQUE index on `order_id`, so the first filing by either side wins and a second one is a 409; the database also decides two simultaneous filings (`EfDisputeRepository.TryAddAsync` turns SQL error 2601/2627 into `false`). The contract's earlier "one per order and raiser" was corrected.
- **Filing rules (`DisputeFilingService`):** the caller must be on the order (customer owns it / worker has an assignment on it) else 404; the order needs an assignment in `AWAITING_ACCEPTANCE`, `COMPLETED` or `ABSENT` and `now <= max(completed_at, shift end) + 24 h` (shift end = slot date + end time in Asia/Ho_Chi_Minh, converted by `IClock`); category in `QUALITY, ATTITUDE, PROPERTY_DAMAGE, ABSENT_FEE, OTHER`; description 1-1000 chars; 1-10 evidence entries of 1-500 chars (stored as a JSON array; no upload endpoint exists yet, so they are plain text). Status `OPEN`, `sla_due_at = created_at + 48 h`.
- **Thresholds in one place:** `DisputeOptions` (file window 24 h, SLA 48 h, priority bands 6 h / 24 h, near-SLA 6 h; override in the `Disputes` configuration section) and `DisputeConstants` (statuses, categories). They are the recommended defaults of `disputes.md` questions D1/D3/D4, not leader decisions.
- **Queue:** default status `OPEN,IN_REVIEW`, ordered by `sla_due_at`; `priority` HIGH/MEDIUM/LOW is derived from the time left (a decided ticket is LOW) and never stored; `nearSla=true` keeps unresolved tickets due within 6 h, overdue included (`slaSecondsRemaining` is negative then); `pageSize` 1-100 (over 100 is a 400). Names (customer, workers, agency) come from read-only joins on the shared tables (contract questions A6/D6: no read port exists yet).
- **Case file:** the ticket, its summary, a shift timeline (`CHECK_IN`, `PHOTO_AFTER`, `CUSTOMER_DISPUTED`, `CHECK_OUT`) and the photos from `CHECK_IN_LOG` / `JOB_PHOTO`; the checklist has no data source and is `null`.
- **Take:** `OPEN` to `IN_REVIEW`, the admin id goes to `resolved_by` as the handler (contract wording); 409 when not open.
- Tests: `Tests/Disputes/DisputeServiceTests.cs` (in-memory) and `Tests/Disputes/DisputeEndpointTests.cs` (controllers; SQL Server flow, 6-way race and window checks that seed and remove their own rows; run without `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`).

- **Evidence photos (BE-M6-02d, contract `disputes.md` 2.1a):** `POST api/{customers|workers}/me/disputes/evidence` (`DisputeEvidenceService`, JSON with base64, jpeg/png/webp, at most 5 MB, the first bytes must match the declared type) stores the photo through `IFileStorage` in the folder `dispute-evidence` and returns the `url` to put in `evidenceUrls`; `GET /files/dispute-evidence/{fileName}` (`DisputeEvidenceFilesController`, anonymous, image names only, `nosniff`) serves it back so the Admin console can use `<img>`. Added without a leader answer; `LocalFileStorage` keeps only the last folder segment, so the folder is one level and the owner is a file-name prefix (`c{customerId}-`, `w{workerId}-`).

### 7.4c Disputes: the verdict (BE-M6-02b, contract `disputes.md` 2.3)
- **Endpoint:** `POST api/admin/disputes/{disputeId}/resolve` (`AdminOnly`), body `{ faultParty, compensationAmount, lockWorker, note }`; `faultParty` `FREELANCER`/`AGENCY`/`CUSTOMER` decides the ticket (`RESOLVED`), `null` dismisses it (`DISMISSED`). Service: `DisputeVerdictService`.
- **No double effect:** the refund (`IRefundService`) and the SLA penalty (`ISlaPenaltyService`) of a verdict are applied by the verdict service itself, before `DisputeResolved` is published once per assignment. A handler of that event in M2, M5 or elsewhere must not call those ports again for it; it is for bookkeeping, notifications and read models, keyed on `DisputeId` + `AssignmentId`.
- **One unit of work (`IUnitOfWork.ExecuteInTransactionAsync`):** the ticket is decided by ONE conditional `UPDATE ... WHERE dispute_status IN ('OPEN','IN_REVIEW')` (`EfDisputeRepository.TryResolveAsync`); a loser of two simultaneous verdicts affects 0 rows and gets a 409 before any refund runs. Then `IRefundService` (once, for the whole amount, when it is above 0), `ISlaPenaltyService` (`QUALITY_COMPLAINT`, once per agency with its share of the amount; not for an `ABSENT_FEE` dispute, decision D9) and `IAuditLog` (one `DISPUTE_TICKET` / `dispute_status` row with the note, plus a `worker_lock_requested` row when `lockWorker`). A refused refund throws inside the unit of work, so everything is rolled back and the answer is 502.
- **Event:** `DisputeResolved` (the existing per-assignment record) is published after the commit once per assignment of the order; the amount is split over the blamed assignments by `gross_amount` in whole VND with the remainder on the last (`DisputeVerdictService.Allocate`).
- **Gaps on purpose:** the worker lock is M4's (BE-M4-01) and the event has no lock flag, so only the request is audited; the payout deduction is read from the resolved ticket by BE-M6-04 (no new table). Rescue cost of the SLA request is 0 because a dispute does not carry one.
- Tests: `Tests/Disputes/DisputeVerdictTests.cs` (in-memory rules and effects; SQL Server: one-transaction verdict, rollback on a refused refund, 6-way race, dismissal; no `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`).

### 7.5b Admin: absence approval (BE-M6-03, contract `admin.md` 2.2, decision Q10)
- **Endpoints (`AdminOnly`):** `GET api/admin/absence-reports?status=&page=&pageSize=` (default `PENDING`, oldest report first, `pageSize` 1-100), `GET .../{assignmentId}`, `POST .../{assignmentId}/approve`, `POST .../{assignmentId}/reject` (`{ reason }`). Service `AbsenceReportService`, repository `EfAbsenceRepository`.
- **No new table (question A3):** a report is a `CHECK_IN_LOG` row with `customer_absent_at` (written by M3, `CustomerAbsentService`). PENDING = assignment not `ABSENT` and no rejection row; APPROVED = assignment `ABSENT`; REJECTED = an `ADMIN_AUDIT_LOG` row (`JOB_ASSIGNMENT`, `absence_report`, `REJECTED`). The queue reads `CHECK_IN_LOG`, `JOB_ASSIGNMENT`, `JOB_ORDER` and the name columns read-only (question A6: no read port yet).
- **Approve** is refused with 409 and `blockReasons` unless GPS is verified, `call_attempts >= Absence.MinCallAttempts` (2), `now - checked_in_at >= Absence.MinWaitMinutes` (15) and `customer_absent_at` is set (all from `BusinessRules.Absence`); a decided report or an assignment that is no longer `CHECKED_IN` is a 409 too. In one unit of work: one conditional `UPDATE` (`CHECKED_IN` to `ABSENT`, `absence_fee_amount = Vnd.Round(gross x 0.40)`), `IRefundService` for `gross - fee`, one audit row; then `CustomerAbsentApproved` is published. A refused refund is a 502 and everything is rolled back.
- **Reject** needs a reason of 1-255 characters and only writes the audit row (question A4: the assignment is left alone).
- **Notifications (BE-M6-03b):** `CustomerAbsentReportedNotificationHandler` tells every active Admin (`IAbsenceRepository.GetActiveAdminIdsAsync`, topic `absence.reported`); `CustomerAbsentApprovedNotificationHandler` tells the worker (the 40 % compensation) and the order's customer (`GetCustomerIdOfOrderAsync`: fee and 60 % refund; topic `absence.approved`). Both are MediatR handlers in `Application/Features/Admin/Services/AbsenceNotificationHandlers.cs`, best effort: a failure is logged and never reaches the code that published the event, so a committed approval stays a success. Message texts are plain Vietnamese with the amounts in whole VND; the `data` map carries `assignmentId`, `orderId` (and the amounts). The 24 h dispute window is not mentioned in the message (the client shows it).
- **Not done here:** the worker returns to `IDLE` through the Workers module reacting to the event (decision Q22 D6).
- Tests: `Tests/Admin/AbsenceReportTests.cs` (in-memory rules; SQL Server: approve flow with queue statuses, blocked approval, rejection, rollback on a refused refund, 6-way race; run without `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`).

### 7.5c Payouts: the monthly batch (BE-M6-04, contract `payouts.md` 2.1, 2.2, 2.4)
- **Endpoints (`AdminOnly`):** `POST api/admin/payout-batches` (`{ periodMonth }`; 201 new, 200 rebuilt DRAFT, 400 bad or unfinished month, 409 CLOSED), `GET api/admin/payout-batches`, `GET api/admin/payout-batches/{batchId}` (items, `payeeType` filter, `warnings` for payees without a bank account) and `POST .../{batchId}/confirm` (`CLOSED`, items `TRANSFERRED`, `PayoutBatchClosed`). No bank is called (decision G-1).
- **Calculation is a pure function** (`PayoutCalculator`): a COMPLETED assignment pays `gross - Vnd.Commission(gross, frozen rate)` rounded per assignment; an approved absence fee (`ABSENT`, dated by `updated_at`) is paid in full with no commission (Q10); one item per freelancer worker, one per agency; `net = gross - commission - penalty`, never below 0. `SplitByWeight` splits an amount in whole VND (G-2).
- **Penalties without a new column:** pending = deductions decided before the month end minus what earlier CLOSED batches already took, capped by the month's payable amount; the rest stays pending, so a rebuild gives the same numbers. The deductions come from `IPayoutPenaltySource` (Payouts owns the interface, `Infrastructure/Modules/Disputes/EfPayoutPenaltySource.cs` implements it over resolved `DISPUTE_TICKET` rows, registered in `PayoutsModule` until the Disputes module registers its own services).
- **Concurrency:** `EfPayoutRepository.TryLockPeriodAsync` takes a transaction-scoped `sp_getapplock` per month so simultaneous builds run one after the other; assignments are attached with `UPDATE ... WHERE payout_item_id IS NULL` and a lost claim throws `PayoutClaimLostException` (409, rolled back); `confirm` is one conditional `UPDATE ... WHERE batch_status = 'DRAFT'`.
- **Time:** the month is read in Asia/Ho_Chi_Minh (G-3) and queried in UTC; the dates are given `Kind = Utc` because the persistence converters reject any other kind.
- Tests: `Tests/Payouts/PayoutCalculatorTests.cs` (arithmetic), `PayoutBatchServiceTests.cs` (rules, controller) and `PayoutBatchDatabaseTests.cs` (SQL Server: month boundaries to the second, penalty carry over three months, agency payee, 4-way parallel build, CLOSED immutability; they use months in 2093-2098 and remove their rows; run without `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`).

### 7.5d Payouts: bank transfer export (BE-M6-05, contract `payouts.md` 2.3, decision Q18)
- **Endpoint (`AdminOnly`):** `GET api/admin/payout-batches/{batchId}/export?type=freelancer|agency-summary|agency-detail` answers the `.xlsx` file (`FileContentResult`, `Content-Disposition: attachment`), the only response of the API that is not the JSON envelope; 400 unknown type and 404 missing batch keep the envelope. Service `PayoutExportService`, controller `AdminPayoutExportController`.
- **No package:** `XlsxWriter` writes the workbook parts (content types, relationships, workbook, styles, one sheet) with `System.IO.Compression` and `System.Xml`; text is `inlineStr` (leading zeros of an account number survive), decimals are numbers, ids plain numbers, XML special characters escaped and control characters dropped; the same input gives the same bytes. Tests read the file back part by part (`XlsxTestReader`); it has not been opened in Excel or LibreOffice in this environment.
- **Columns:** Q18 for `freelancer`; P5 defaults for `agency-summary` and `agency-detail`, the latter with `agency_name` in front. Payees with net 0 are left out of the two transfer files; payees without a bank account stay listed.
- **Storage:** every export goes through `IFileStorage` (folder `payouts`), `PAYOUT_BATCH.export_file_url` points at the latest one and the previous file of the batch is deleted. Bank account numbers live in these files: they are not served as static files (no `UseStaticFiles` in this project).
- Tests: `Tests/Payouts/XlsxWriterTests.cs`, `PayoutExportTests.cs` (rows and columns per type, storage replacement, controller, and one SQL Server test that exports a real batch).

### 7.5g M6 cross-module acceptance test (BE-M6-08)
- `Tests/Admin/M6AcceptanceTests.cs` runs the money chain of the M6 modules with the **real** services on the local SQL Server: an absence report approved through `AbsenceReportService` (40 % fee 104000 of 260000, 60 % refund 156000 through `IRefundService`), a dispute decided against a freelancer through `DisputeVerdictService` (compensation 50000), then `PayoutBatchService` builds the month. It checks that the modules agree: the absence fee is paid with no commission, the verdict becomes the payout deduction, the second freelancer gets 80 % with rounding per assignment, an agency's two jobs are one aggregated item, a rerun gives the same batch and items with no assignment counted twice, and a CLOSED month cannot be built again.
- It uses the months of 2088 so no real data is touched, three clocks (absence on 10 March, verdict on 20 March, batch on 5 April) and removes every row it seeds (audit rows, check-in log, tickets, batch, items, assignments, people). Each criterion of BE-M6-08 is also covered inside the ticket that built it; the PR of BE-M6-08 lists the test names.
### 7.5e Payouts: the freelancer's income (BE-M6-07, contract `payouts.md` 2.5, decision Q11)
- **Endpoints (`WorkerOnly`):** `GET api/workers/me/earnings?month=YYYY-MM` (default the current month in Asia/Ho_Chi_Minh; 400 bad or future month; 403 for agency staff, whose agency is paid) and `GET api/workers/me/payouts?page=&pageSize=` (the worker's own items of CLOSED batches, newest month first). Service `WorkerEarningsService`, controller `WorkerEarningsController`.
- **Own repository:** `IWorkerEarningsRepository` / `EfWorkerEarningsRepository` are scoped to one worker id in every method; they were kept apart from `IPayoutRepository` so the batch interfaces stay as they are. The worker id comes from `ICurrentUser`, never from the request.
- **Same arithmetic as the batch** (`PayoutCalculator`): jobs and approved absence fees of the month with the frozen commission rate, pending penalty capped by the month. `payoutStatus` is NOT_BUILT / PENDING (DRAFT) / TRANSFERRED (CLOSED). A DRAFT item is not trusted (it may be stale) so the month is recomputed; a CLOSED batch's stored item wins so the screen equals the money transferred.
- Tests: `Tests/Payouts/WorkerEarningsTests.cs` (in-memory rules and controller; one SQL Server test with two freelancers and an agency staff member: month boundaries to the second, ownership, DRAFT then CLOSED, history; rows removed afterwards).

### 7.5 OpenAPI and Swagger snapshot (BASE-13)
- **Configuration** (`Infrastructure/Swagger/SwaggerConfiguration.cs`):
  - Deterministic `operationId` format: `{Controller}_{Action}` for code generators.
  - Bearer JWT security scheme: header `Authorization: Bearer <token>`.
  - API Info: Cho Thue Giup Viec API (v1).
- **Snapshot Contract** (`.spec/contracts/openapi.json`): exported static snapshot used by Web/Mobile clients to generate API SDKs.
- **Contract Verification Test** (`Tests/Contracts/OpenApiSnapshotTests.cs`):
  - Verifies live OpenAPI document matches `.spec/contracts/openapi.json`.
  - Verifies all operations possess valid operationIds.
  - Verifies Bearer security scheme definition.
  - Fails whenever the live document drifts from the contract snapshot.
- Tests: `Tests/Contracts/OpenApiSnapshotTests.cs` (577 total tests passing).

### 7.5a Admin profile (BE-M6-06a, contract `admin.md` 2.1)
- `GET /api/admin/me` (`WebAPI/Controllers/Admin/AdminProfileController.cs`, policy `AdminOnly`, GET only, no parameters): `{ adminId, email, fullName, adminRole, isActive, createdAt }` of the **caller**; the id is `ICurrentUser.UserId`, never a URL or body value. `password_hash`, `failed_login_count` and `locked_until` are not in the DTO (a test serialises the DTO and looks for them). 404 only when the row was removed.
- `EfAdminProfileReader` reads `ADMIN` with `AsNoTracking`; the module registers it with `AdminModule`. No admin management endpoint exists in the MVP (contract question A1, recommended default); the operations dashboard is section 7.5f.
- Tests: `Tests/Admin/AdminProfileTests.cs` (the DB test inserts and deletes its own `ADMIN` row; run without `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`: in that mode `SqlClient` throws and the DB test returns early as a silent pass).

### 7.5f Admin: operations dashboard (BE-M6-06b, contract `admin.md` 2.3, question A2)
- **Endpoint:** `GET api/admin/dashboard` (`AdminOnly`) answers exactly `{ generatedAt, orders: { today, thisWeek }, shifts: { inProgress, completedToday }, disputes: { open, nearSla } }` (decision G-7: no other metric; a test lists the DTO properties). Service `AdminDashboardService`, repository `EfAdminDashboardRepository` (six counts, one after the other: a `DbContext` cannot run queries in parallel).
- **Definitions are the contract's recommended defaults, not leader decisions:** orders by `JOB_ORDER.created_at` in the current day and ISO week (Monday start) of Asia/Ho_Chi_Minh; shifts in progress = assignments `CHECKED_IN`, `IN_PROGRESS`, `AWAITING_ACCEPTANCE`; completed today = `COMPLETED` with `completed_at` today; disputes open = `OPEN` + `IN_REVIEW`; near SLA = open with `sla_due_at <= now + Admin:DisputeNearSlaHours` (default 6, `AdminDashboardOptions`, overdue included).
- **Time zone:** `AdminDashboardService.WindowFor` turns the local day and week into UTC instants through `IClock` (decision G-3); Sunday 23:59:59 local is still the old week and Monday 00:00 local is the new one even while the UTC date is still Sunday. The dates are given `Kind = Utc` because the persistence converters reject any other kind.
- **Read-only access to other modules' tables** (`JOB_ORDER`, `JOB_ASSIGNMENT`, `DISPUTE_TICKET`) because no read port exists (contract question A6).
- Tests: `Tests/Admin/AdminDashboardTests.cs` (window edges, mapping, DTO shape, controller, and one SQL Server test that seeds the edges of the day and week in 2092 and compares against the baseline counts; rows removed afterwards).

### 7.6 Audit log (`IAuditLog`, BE-M6-09a, decisions G-5 / SC-3)
- The real implementation is `Infrastructure/Modules/Admin/EfAuditLog.cs`, registered by `AdminModule` (`IModule`), so it wins over `FakeAuditLog`. The port is unchanged: `Task WriteAsync(AuditEntry entry, CancellationToken ct)`.
- **Same unit of work:** `WriteAsync` only adds the `ADMIN_AUDIT_LOG` row to the scoped `AppDbContext`; it never calls `SaveChanges`. A module therefore calls it **before** its own `SaveChangesAsync` / `IUnitOfWork` commit (inside `ExecuteInTransactionAsync` when it has one), and the change and its audit row commit or roll back together.
  ```csharp
  await _audit.WriteAsync(new AuditEntry(AuditActorType.Admin, adminId, "PRICE_RULE", rule.RuleId.ToString(),
      "unit_price", oldPrice.ToString(), newPrice.ToString(), reason), ct);
  await _uow.SaveChangesAsync(ct);   // saves the price change and the audit row together
  ```
- **Lifetime:** `EfAuditLog` is **scoped** (it shares the request's `AppDbContext`), while `FakeAuditLog` was a singleton. A consumer of `IAuditLog` must therefore be scoped or transient; a singleton that takes it fails with "Cannot consume scoped service" when scope validation is on (Development). A test pins this (`A_singleton_cannot_take_IAuditLog_because_it_shares_the_scoped_DbContext`).
- **Append-only:** the port and the class have no update/delete method (a test checks the method names). The database itself still allows an UPDATE/DELETE by SQL; blocking that needs a schema-level rule (trigger or permissions), which is an M1 schema change and not part of this ticket.
- **Rules enforced:** `changed_at` comes from `IClock` (UTC); `entityType` <= 30, `entityId` <= 40, `fieldName` <= 50, `oldValue`/`newValue` <= 500, `reason` <= 255 characters, rejected with `ArgumentException` instead of being truncated; an `ADMIN` entry needs `adminId` and a non-blank `reason`; a `SYSTEM` entry must have `adminId = null`.
- **Read endpoint (BE-M6-09b):** `GET /api/admin/audit-logs` (`WebAPI/Controllers/Admin/AdminAuditLogsController.cs`, policy `AdminOnly`, GET only). Query: `entityType`, `entityId`, `actorType` (`ADMIN`/`SYSTEM`, case-insensitive), `from` (inclusive) and `to` (exclusive) as ISO-8601 (a value without an offset is UTC; `from` must be earlier than `to`), `page` (default 1) and `pageSize` (default 20, 1-100: a larger value is a 400, not a silent cap). Filters combine with AND; result `{ items, page, pageSize, total }`, newest first (`changed_at`, then `log_id`, descending); bad values are a 400 with `data.errors` (decision O5). The query uses `AsNoTracking` and the repository (`IAuditLogReadRepository`) has no write method.
- Tests: `Tests/Admin/EfAuditLogTests.cs` and `Tests/Admin/AuditLogReadTests.cs`; the transaction test (commit keeps the row, rollback leaves none) runs only when the local SQL Server database exists (`dotnet ef database update`, section 7.1).

### 7.7 Ratings module (BE-M6-01a, contract `ratings.md`, decisions Q14 / SC-5)
- **Endpoints:** `GET .../rating-window` and `POST .../rating` under `api/customers/me/assignments/{assignmentId}` (policy `CustomerOnly`, customer rates the worker) and `api/workers/me/assignments/{assignmentId}` (policy `WorkerOnly`, worker rates the customer). The caller id comes from `ICurrentUser`, never from the URL or body; an assignment that is not the caller's is a 404, never a reason.
- **Rules (`Application/Features/Ratings/Services/RatingService.cs`):** stars 1-5; criteria are exactly `punctuality, cleaningQuality, attitude` (customer) or `cooperation, workingConditions` (worker), each 1-5, camelCase in the API and snake_case in `criteria_json` (`cleaning_quality`, `working_conditions`); comment at most 500 characters; the assignment must be `COMPLETED` and `now <= completed_at + Rating.WindowHours` (48, `BusinessRules`; the exact closing instant is still open); one rating per `(assignment_id, rater_role)` with `rater_role` `CUSTOMER` / `WORKER`. Body validation runs first (400, `data.errors`), then ownership (404), then the state (409: `NOT_COMPLETED`, `WINDOW_CLOSED`, `ALREADY_RATED`).
- **Race:** `EfRatingRepository.TryAddAsync` saves on its own and turns the unique-index violation (SQL error 2601/2627) into `false`, so of simultaneous requests exactly one wins and the rest get 409. A test fires six at once against the local database.
- **Event:** `RatingSubmitted` (no worker id in the record) is published after the row is saved; a consumer that fails is logged and does not fail the request.
- **Reads `JOB_ASSIGNMENT`** (the flat shared node) with `AsNoTracking` and never changes it. The worker-to-customer rating is internal (Q14): no endpoint returns it to the customer or lists it back to the worker; a test pins that the service has no other method.
- **Not here:** the sync of `WORKER.rating_avg` (M4's table). The real `IWorkerReputation` exists since BE-M6-01b (section 7.2a); its `successRate` definition is the recommended default of contract question M3, still unconfirmed by the leader.
- **Testing note:** the DB-backed tests return early (a silent pass) when SQL Server or the database is missing **and also when `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` is set**, because `Microsoft.Data.SqlClient` throws in invariant globalization mode. Do not set that variable when you want those tests to run.
- Tests: `Tests/Ratings/RatingServiceTests.cs` (rules, in-memory repository) and `Tests/Ratings/RatingEndpointTests.cs` (controllers, repository, unique-index race).

## 8. Known gaps / TODO
- No authentication (`UseAuthorization` only, until Identity module BE-M1-02).



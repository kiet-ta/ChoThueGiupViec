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
| `IWorkerReputation` | M6 | M3 | `FakeWorkerReputation` |
| `IWorkerProfileQuery` (Q21 C3) | M4 | M1 (favorite workers) | `FakeWorkerProfileQuery` |
| `IAuditLog` (append-only, G-5) | M6 | M2, M5, M6 | `FakeAuditLog` |
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

## 8. Known gaps / TODO
- No authentication (`UseAuthorization` only, until Identity module BE-M1-02).



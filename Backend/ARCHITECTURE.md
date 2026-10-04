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
  Entities/                User, Customer, Order, OrderItem, BankAccount
  ValueObjects/            Address, Email, Money (immutable, validated)
  Events/                  Domain events (OrderCreatedEvent)
  Services/                Domain services (ShippingCalculator) - logic spanning multiple entities
Application/               Use cases. Depends on Domain only
  Features/<Feature>/      Commands/, Queries/, Handlers/ (MediatR) e.g. Users/CreateUserCommand
  Behaviors/               MediatR pipeline: Validation, Logging, Performance
  Interfaces/              IRepositories/ (IUserRepository), IServices/ (ICacheService, IEmailService)  -> ports
  Services/                Application services (EmailService)
  Common/                  Models (ApiResponse<T>), Helpers (PaginatedList), Constants
  Exceptions/              NotFound, Validation, ForbiddenAccess, BusinessRuleViolation
  DependencyInjection.cs   AddApplication(): MediatR + ValidationBehavior
Infrastructure/            Implements ports. Depends on Application (+Domain)
  Persistances/            UserRepository, OrderRepository  (folder name has this spelling)
  Services/                InMemoryCacheService (default), RedisCacheService (optional)
  DependencyInjection.cs   AddInfrastructure(config): cache, repositories, services
Middleware/                ErrorHandling, RequestResponseLogging, ResponseWrapper
WebAPI/Controllers/        Thin controllers: UserController, PaymentIntent (Stripe), ValuesController
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

### 2.4 Domain model (BASE-06): 27 entities, 3 state machines
- **27 tables** = 22 of `GiupViec_Physical_DB_MVP5.drawio` + 5 of `.spec/decisions.md` section 3 (`PRICE_RULE`, `ADMIN_AUDIT_LOG`, `ESCROW_TRANSACTION`, `OTP_CODE`, `REFRESH_TOKEN`), plus the added columns of SC-1 (`PARTNER_AGENCY` password/guarantee) and SC-8 (`failed_login_count`, `locked_until` on `ADMIN` and `PARTNER_AGENCY`). `TWO_WAY_RATING` UNIQUE (SC-5) is a persistence rule for BASE-07. The drawio title says "20 bảng" but the file contains 22 table cells (counted by script); the plan says 22.
- `Domain/Entities/<Entity>.cs`: `public partial class`, one scalar property per column (generated once from the drawio, then maintained by hand). **No navigation properties** (0-JOIN queries, PRD 5.2). Behaviour of a module is added in `<Entity>.<Module>.cs` partial files.
- Types: `INT/BIGINT/TINYINT` -> `int/long/byte`, `DECIMAL` -> `decimal` (money = `DECIMAL(18,2)`, whole VND after rounding, decisions G-2), `DATETIME2` -> `DateTime` (UTC, G-3), `DATE/TIME` -> `DateOnly/TimeOnly`, `BIT` -> `bool`. `CustomerAddress.TotalAreaM2` is the computed `floor_area_m2 * num_floors`.
- `Worker` is Single Table Inheritance: `WorkerType` and `AgencyId` have private setters and are set only by `Worker.CreateFreelancer` / `Worker.CreateAgencyStaff`, which enforces the drawio CHECK (freelancer has no agency, agency staff has one).
- **State machines** (`Domain/StateMachines`, explicit allow-lists; anything not listed throws `InvalidStateTransitionException`): `JobOrderStateMachine` (order_status), `JobAssignmentStateMachine` (assignment_status, the flat execution node), `WorkerStateMachine` (work_status). The status properties have private setters; use `entity.TransitionTo(next)`. A new entity starts in the first enum member. Tests list the expected transitions independently of the code and check every (from, to) pair (`Backend/Tests/Domain/StateMachineTests.cs`).
- Enum members are PascalCase; the database spelling is UPPER_SNAKE_CASE (`CheckedIn` -> `CHECKED_IN`) through `DbEnum`, which the EF value converters of BASE-07 must use. A test checks that every enum value fits its `VARCHAR(n)` column.

**Columns left as `string` on purpose.** The PRD, the drawio and `decisions.md` do not list their values, and inventing them is forbidden (AGENTS.md rule 5). The owning module pins the values in its `*-00` contract, then turns the column into an enum:
`PRICE_RULE.area_bracket`, `JOB_ORDER.shift_code`, `BOOKING_SLOT.shift_code/slot_source/slot_status`, `JOB_ORDER_EXTENSION.worker_decision/ext_status`, `PAYMENT_TRANSACTION.gateway`, `CHECK_IN_LOG.fallback_method`, `JOB_PHOTO.photo_phase`, `TWO_WAY_RATING.rater_role`, `INCIDENT_LOG.incident_type/redispatch_status`, `DISPUTE_TICKET.raised_by/category/dispute_status/fault_party`, `WORKER.kyc_status`, `PARTNER_AGENCY.agency_status`, `PARTNER_SUBSCRIPTION.sub_status`, `SUBSCRIPTION_PACKAGE.tier/billing_cycle`, `PAYOUT_BATCH.batch_status`, `PAYOUT_ITEM.item_status`, `ADMIN.admin_role`, `SKILL.category`.

**Enum values that are M1 proposals** (not stated verbatim by the sources; the owning module may rename them in its contract before any code uses them): `WorkStatus.Pending/Busy/Locked` (only `IDLE` is in the PRD), `PaymentPurpose.Order`, and every transition of the three machines beyond the paths quoted in the XML comments of the machine classes.

**Conflicts between sources, for the leader (not resolved here):**
1. `JOB_ASSIGNMENT.accepted_at` is `NOT NULL` in the drawio, but overview section 6 lists an `OFFERED` assignment state (an offer exists before anyone accepts). Either `accepted_at` becomes nullable or `OFFERED` is never persisted.
2. The drawio unique index `Job_Assignment(slot_id) WHERE assignment_status NOT IN ('CANCELLED','REASSIGNED')` does not mention the new `CANCELLED_BY_WORKER` (decisions Q15); BASE-07 must add it to the filter or a cancelled-by-worker slot stays blocked.
3. `DISPUTE_TICKET.fault_party` is `FREELANCER | AGENCY` in the drawio notes but `WORKER` in decisions Q10/Q12.
4. `Money` and `Address` (existing value objects) are **not** reused: `Address` has only street/city while `CUSTOMER_ADDRESS` has line/district/city, and `Money` carries a currency string where the schema is VND-only. Entity money fields are `decimal`.
5. The drawio CHECKs on `PAYMENT_TRANSACTION` (exactly one of order/extension/subscription) and `PAYOUT_ITEM` (payee type vs worker/agency) are persistence constraints: BASE-07 adds them as database CHECKs.

The demo template classes (`User`, `Order`, `OrderItem`, `BankAccount`) stay until BASE-09; the empty demo `Customer` was replaced by the real entity.

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
- Use value objects (`Money`, `Email`, `Address`) instead of primitives for domain concepts.
- Throw Application exceptions (`NotFoundException`, ...) rather than returning status codes from handlers.
- Cache via `ICacheService` (swap InMemory ↔ Redis in Infrastructure DI; Redis needs `ConnectionStrings:Redis`).
- Tests: follow `Tests/GoldenRule.md` — use cases are tested with mocks/stubs, never real DB/external APIs; don't test across domains.
- Namespaces mirror folders (`CommonService.Application.Features...`). Keep new code consistent with existing style; preserve existing comments/docs.

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
CI: `../.github/workflows/dotnet.yml (repo root, working-directory: Backend)` (restore → build → test on push/PR to `main`, .NET 10).

## 8. Known gaps / TODO
- No database/EF Core yet; repositories are in-memory/stub.
- `PaymentIntent` controller creates a fixed-amount Stripe intent (demo); route has typo `create-paymen-intent`.
- No authentication (`UseAuthorization` only).


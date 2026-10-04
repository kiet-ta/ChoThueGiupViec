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
Tests/                     e2e/, integration/, GoldenRule.md (testing philosophy)
Properties/launchSettings.json
```

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
dotnet test
```
CI: `.github/workflows/dotnet.yml` (restore → build → test on push/PR to `main`, .NET 10).

## 8. Known gaps / TODO
- No database/EF Core yet; repositories are in-memory/stub.
- `PaymentIntent` controller creates a fixed-amount Stripe intent (demo); route has typo `create-paymen-intent`.
- No authentication (`UseAuthorization` only); no test project yet (only docs under `Tests/`).

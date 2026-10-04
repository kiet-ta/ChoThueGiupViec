# View this link:
[View Application Folder](https://mermaid.live/edit#pako:eNp9VF1vmzAU_SuWpb7RlCS0CWiq1BKQkFatS7Y8jOzBhRviydjImK4f6n-fjUPnLFl5wef6nnM_4RUXogQc4S0Tv4sdkQp9u91wpJ-2e6gkaXboXkILXBFFBc9dgD6TZ5Do5j67-J79tCzzxHksuJKCMZAtukAJLxtBuWr3PsDLDf8nyE3TMFrYGM7ZhnC00zwFojqdxacHeR2Luia8bD30tQNJQR_WhNGSKCFbh7XKVyAfaWFZrv5gd5yzPFtCI1qqRZ6Nf8YVyC059Frn-0haxTilrNN9-Wv7oNaFqAnluX0dVZjkCVdUUZurFuzgy8MvKJQu7qaqJFREHWSySB515EGuBwfXy-Fq2TH4aAYZ30rSKtkVpsH5ITzK0_Qof2_UPt0kRbGQ4KEFaRqQo9HIbWzdsGEQPYB6WCVL1lkyD8Wk2GkFUIVLTp70EDhh-XBAZiw68Xe63kMPpZSZLbiDtiUV3HYn6j070zlyrjtqmNYWo_Pza3cJrdndFONgG3n67rBdQ7DU3rlg7YLV4LhyHbMemOZavO5xOvju53ky5cQm2q_B3nvZm5KBbWSPqjHj-I_g0O9TRWIPV5KWONIm8HANUktqiF8NeYPVTs94gyN9LGFLOqY2eMPfNK0h_IcQ9cCUoqt2ONoS1mrUNfojggUler71u1XqIYKMRccVjubBvBfB0St-wtHUH03Dy3Ayu5zOJ8HY9wMPP-Poaj6aBFdhMJmE4TQM5m8efumj-qP5LPTHfhiOZ34wC4MrD0NpNvnO_hD7_-LbH_EFofI)
---

## 🏗 Cấu trúc Application

```
src/
 ├── Application/
 │    ├── Features/        # Nơi để CQRS theo domain feature (User, Order, Product…)
 │    ├── Common/          # Chứa helper, base class, interface dùng chung
 │    ├── Behaviors/       # Pipeline Behaviors (ex: Logging, Validation, Performance)
 │    ├── Interfaces/      # Định nghĩa contract (repo, service, unit of work)
 │    ├── Exceptions/      # Custom exception của Application
 │    ├── Services/        # Logic chung: EmailService, DateTimeService…
 │    └── DependencyInjection.cs # Nơi đăng ký DI cho Application
```

---

## 📌 Giải thích chi tiết từng phần

### 1. **Features/**

* Theo **CQRS** (Command, Query, Event, Dto, Mapping…).
* Chia theo **domain business** (User, Product, Order…).
* Đây là trái tim chính của Application.

---

### 2. **Common/**

* Chứa các class **dùng chung** cho toàn Application.
  Ví dụ:

```csharp
public abstract class BaseCommand<TResponse> : IRequest<TResponse>
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
```

* Có thể để **Result<T>**, **PaginatedList<T>**, **BaseEntity** (nếu không muốn để trong Domain).

---

### 3. **Behaviors/**
* Dùng với **MediatR Pipeline Behavior** để inject logic **cross-cutting** (áp dụng cho mọi Command/Query).

* Ví dụ:
  * **ValidationBehavior** → tự động chạy FluentValidation cho mỗi Command/Query. (không còn dùng FluentValidation)
  * **LoggingBehavior** → log request/response.
  * **PerformanceBehavior** → đo time chạy.

```csharp
public class ValidationBehavior<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = _validators.Select(v => v.Validate(context))
                                  .SelectMany(r => r.Errors)
                                  .Where(f => f != null)
                                  .ToList();
        if (failures.Any())
            throw new ValidationException(failures);
        
        return await next();
    }
}
```

---

### 4. **Interfaces/**

* Định nghĩa **hợp đồng** (contract) cho các service ngoài (infrastructure) mà Application phụ thuộc.
* Application **chỉ biết interface**, còn triển khai cụ thể sẽ để ở **Infrastructure**.
  Ví dụ:

```csharp
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
```

---

### 5. **Exceptions/**

* Tạo **custom exception** để xử lý logic business.
* Giúp phân biệt với exception hệ thống.
  Ví dụ:

```csharp
public class NotFoundException : Exception
{
    public NotFoundException(string name, object key) 
        : base($"{name} with key {key} was not found.") {}
}
```

---

### 6. **Services/**

* Chứa các service cung cấp **logic dùng chung** trong Application (không phải business chính).
  Ví dụ:

  * `IDateTimeService` → để testable (mock thời gian).
  * `ICurrentUserService` → lấy thông tin user từ context.
  * `IEmailService` → gửi email (implementation nằm ở Infrastructure).

---

### 7. **DependencyInjection.cs**

* File extension để **đăng ký DI** (services, validators, behaviors) cho Application.

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }
}
```

---

👉 Tóm gọn:

* **Features/** → CQRS theo domain.
* **Common/** → tiện ích dùng chung.
* **Behaviors/** → cross-cutting (validation, logging…).
* **Interfaces/** → contract với Infrastructure.
* **Exceptions/** → business exception.
* **Services/** → service hỗ trợ Application.
* **DependencyInjection.cs** → đăng ký Application layer vào DI container.

---

## Caching
- ### [Interface Cache Service Flow](https://mermaid.live/edit#pako:eNq1VMtu2zAQ_JUFTzagGJL1cEygBuoYaHtokSbppdCFlVY2AT5UikrtGv73kHrEbW3kVp2o1czO7JDikRS6REJJgz9bVAVuONsaJnMF7qmZsbzgNVMW7gRHZa_UtbJGC4Hm8tsjmmde4BUSK3ZXypt1rvpqr3azWp3bU_j49HQPD95oY2HyAS18a9DAvdEVFzgdmK8Exx4MUCcoBDiGJwz4SevWn8rpKDlgvaZ3Rz38fXNQxSQnHkqjeUzrnpuTQY2JYRjY8SGczoQv3fxp4AFtaxT4RjA0gUlltASJUpvD9Ewejfwz_dBhdHDWOg_sGV1wHt3UWjVehDV26I7CFXq7kjfNW36_aAuVblV56Wu12qwpfG3RHP6a54zcrK_NfgG7SPzxrcQDeGaixQBwX3NzeJfK_5dZI_QvNAH8aC0U3lvpwjCgcG_BcjmeNfTxkIBsDS8Jtca5IxKNZP6VHD0oJ3aH0tmnbllixVphc5Krk6O5M_9dazkyjW63O0Ir5nYpIG1dMjv-jq8Qp4jmzu2LJTSZx10PQo9kT2gWz7IkTOdptMxuw3iZBeRAaBSFs3SRxovbdBmlSZYkp4D87lTD2TKKs3TpGMl8EYeha4clt9p87u-E7mo4vQDrjVV2)

## khái niệm các đoạn code đang dùng:
 - CancellationToken là một cơ chế trong .NET giúp hủy tác vụ bất đồng bộ (async) một cách có kiểm soát, thay vì để nó chạy đến hết hoặc phải “kill” thô bạo.
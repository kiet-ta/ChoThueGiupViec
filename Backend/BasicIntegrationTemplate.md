# CommonService Operational Flows

## 1. Request Processing Flow

```mermaid
flowchart TD
    A[HTTP Request] --> B[ErrorHandlingMiddleware]
    B --> C[RequestResponseLoggingMiddleware]
    C --> D[Controller]
    D --> E[MediatR Handler]
    E --> F[Domain Service]
    F --> G[Repository]
    G --> H[Database/Cache]
    H --> I[Response]
    I --> J[ResponseWrapperMiddleware]
    J --> K[HTTP Response]
```

## 2. CQRS Command Flow

```mermaid
flowchart LR
    A[Command] --> B[CommandHandler]
    B --> C[Domain Service]
    C --> D[Repository]
    D --> E[Database]
    E --> F[Cache Invalidation]
    F --> G[Response]
```

## 3. Query Flow with Caching

```mermaid
flowchart TD
    A[Query] --> B[QueryHandler]
    B --> C{Cache Hit?}
    C -->|Yes| D[Return Cached Data]
    C -->|No| E[Repository]
    E --> F[Database]
    F --> G[Cache Data]
    G --> H[Return Data]
```

## 4. Error Handling Flow

```mermaid
flowchart TD
    A[Exception Thrown] --> B[ErrorHandlingMiddleware]
    B --> C{Exception Type}
    C -->|ValidationException| D[400 Bad Request]
    C -->|NotFoundException| E[404 Not Found]
    C -->|UnauthorizedException| F[401 Unauthorized]
    C -->|Other| G[500 Internal Server Error]
    D --> H[Logged Response]
    E --> H
    F --> H
    G --> H
```

## 5. Service Integration Patterns

### Email Service Flow
```mermaid
flowchart LR
    A[Email Request] --> B[IEmailService]
    B --> C[Email Provider]
    C --> D[SMTP/SendGrid/etc]
    D --> E[Delivery Status]
    E --> F[Logging]
```

### Caching Strategy
```mermaid
flowchart TD
    A[Data Request] --> B{Cache Available?}
    B -->|Yes| C[Return from Cache]
    B -->|No| D[Fetch from Source]
    D --> E[Store in Cache]
    E --> F[Return Data]
    G[Cache Expiry] --> H[Remove from Cache]
```

## 6. Repository Pattern Implementation

```mermaid
flowchart LR
    A[Controller] --> B[IRepository]
    B --> C[Repository Implementation]
    C --> D[Entity Framework]
    D --> E[Database]
    C --> F[Caching Layer]
```

## Key Features:

1. **Middleware Pipeline**: Xử lý lỗi, logging, và response wrapping tự động
2. **CQRS Ready**: Sẵn sàng cho Command/Query separation với MediatR
3. **Caching Strategy**: Hỗ trợ cả In-Memory và Redis caching
4. **Repository Pattern**: Abstraction layer cho data access
5. **Clean Architecture**: Tách biệt rõ ràng các concerns
6. **Service Integration**: Email, SMS, Payment services
7. **Error Handling**: Centralized exception handling
8. **API Documentation**: Swagger/OpenAPI integration

## Usage Examples:

### 1. Adding a new Command
```csharp
public class CreateCustomerCommand : IRequest<Customer>
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, Customer>
{
    private readonly ICustomerRepository _repository;
    
    public CreateCustomerCommandHandler(ICustomerRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<Customer> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = new Customer { PhoneNumber = request.PhoneNumber };
        await _repository.AddAsync(customer, cancellationToken);
        return customer;
    }
}
```

### 2. Using Cache Service
```csharp
public class CustomerService
{
    private readonly ICacheService _cache;
    private readonly ICustomerRepository _repository;
    
    public async Task<Customer?> GetCustomerAsync(int id, CancellationToken ct)
    {
        var cacheKey = $"customer_{id}";
        var cached = await _cache.GetAsync<Customer>(cacheKey);
        
        if (cached != null)
            return cached;
            
        var customer = await _repository.GetByIdAsync(id, ct);
        if (customer != null)
            await _cache.SetAsync(cacheKey, customer, TimeSpan.FromMinutes(30));
        
        return customer;
    }
}
```
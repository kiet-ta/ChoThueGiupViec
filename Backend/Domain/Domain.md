# Golden Rule

- Framework-independent (ASP.NET, EF Core).

- Infrastructure-independent (SQL, Mongo, API).

- Contains only pure business logic.

- The “cleaner” the domain is → the easier the system is to maintain, test, and migrate.

# What’s Inside the Domain Layer?

In a real-world project, the **Domain Layer** usually includes:

## Entities
- Represent business objects with an **ID**.
- Used when an object needs to exist independently and be identified by its ID.

## Value Objects
- Represent a **value**, without an ID.
- Used to group logic around a specific value and avoid “magic strings/numbers”.

## Aggregates & Aggregate Roots
- Combine **entities** and **value objects** into a consistent group.
- The **Aggregate Root** is the only entry point to interact with that group.

## Domain Services (if needed)
- Hold complex business logic that doesn’t fit inside a single entity or value object.
- Example: calculating matching scores or geo-dispatching across workers.

## Domain Events (for large systems)
- Notify that “something happened” in the domain.
- Used to publish events for the Application Layer to handle (e.g., send email, log activity, update other states).

Absolutely! Here's your content translated into clear, simple English and formatted in Markdown for easy reading:

---

# When to Use What in the Domain Layer

## Entity
- Use when the object needs to be tracked by **ID**.
- Has identity and can change over time.
- **Examples**: `Customer`, `Worker`, `JobOrder`

## Value Object
- Use when the object is only important for its **value**, not identity.
- Immutable and interchangeable if values are the same.
- **Examples**: `Email`, `Vnd`

## Aggregate Root
- Use when you need to group related entities and value objects into a **consistent unit**.
- Acts as the **main access point** to the group.
- **Example**: `JobOrder` manages its state and assignments

## Domain Service
- Use when business logic involves **multiple entities** but doesn’t belong to any single one.
- Keeps logic clean and reusable.

## Domain Event
- Use when you want the system to **react to something that happened** in the domain.
- Helps trigger actions (like sending emails) without mixing external logic into domain models.
- **Example**: `OrderPaid`, `JobAssigned`


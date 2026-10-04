Don’t test across domains (except for unit testing adjacent entities/use cases).

The more a test depends on “the concrete”, the more it should be pushed to the outside (like clean architecture: dependencies go from outside to inside).

For example, a use case test should not depend on the database or an external API. Instead, it should use mocks or stubs to simulate these dependencies. This ensures that the test focuses on the logic of the use case itself, rather than the behavior of external systems.
# SharedKernel Contract

`SharedKernel/` contains only stable foundation primitives shared across features.

## Allowed

1. Generic result types (`Result`, `Result<T>`), if truly feature-agnostic.
2. Base exceptions and error categories.
3. Cross-cutting value objects and utility contracts with long-term stability.

## Forbidden

1. Feature-specific ports (`IOrdersRepository`, `IUsersService`, etc.).
2. Infrastructure details (EF Core, HttpClient, file paths, SQL concerns).
3. UI concerns (WinForms controls/forms/resources).

## Dependency Rule

Features and infrastructure may depend on `SharedKernel`, but `SharedKernel` must not depend on features or infrastructure.

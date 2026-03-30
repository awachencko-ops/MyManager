<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 5 Progress (Clean Architecture Repackaging)

Date: 2026-03-30  
Status: In progress

## Completed Increment: Architecture Guardrails (Baseline Lock)

1. Added verify test pack for API architecture boundaries:
   - `tests/Replica.VerifyTests/ReplicaApiArchitectureBoundaryTests.cs`
2. Guardrail #1:
   - `Application` layer must not reference presentation namespaces (`Controllers`, `Hubs`).
3. Guardrail #2 (baseline lock):
   - direct `Presentation -> Infrastructure/Data/Services` coupling must not expand beyond current approved baseline.
   - this allows progressive cleanup while preventing new architectural drift.

## Completed Increment: Diagnostics Controller Decoupling

1. Added diagnostics query handlers:
   - `Replica.Api/Application/Diagnostics/Queries/DiagnosticsReadQueries.cs`
   - `GetRecentOperationsQueryHandler` (reads recent operation events),
   - `GetPushDiagnosticsQueryHandler` (reads push observability snapshot).
2. Refactored `Replica.Api/Controllers/DiagnosticsController.cs`:
   - controller now routes diagnostics reads through MediatR query handlers,
   - preserved isolated-test fallback constructor without runtime store coupling.
3. Reduced presentation coupling baseline:
   - `DiagnosticsController.cs` removed from allowed `Presentation -> Infrastructure/Data/Services` baseline in architecture verify tests.

## Completed Increment: Users Controller Decoupling

1. Added actor accessor abstraction:
   - `Replica.Api/Application/Abstractions/IReplicaApiCurrentActorAccessor.cs`.
2. Added infrastructure implementation:
   - `Replica.Api/Infrastructure/ReplicaApiCurrentActorAccessor.cs`,
   - resolves actor from request `HttpContext` through existing current-user context.
3. Runtime DI updated:
   - registered `AddHttpContextAccessor()` and scoped `IReplicaApiCurrentActorAccessor`.
4. Refactored `Replica.Api/Controllers/UsersController.cs`:
   - switched to mediator-only controller flow (removed `ILanOrderStore` fallback constructor/path),
   - switched actor resolution to injected accessor,
   - removed direct `using` dependencies to `Infrastructure` and `Services`.
5. Reduced presentation coupling baseline:
   - `UsersController.cs` removed from allowed `Presentation -> Infrastructure/Data/Services` baseline.

## Test Evidence

1. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj --filter "ReplicaApiArchitectureBoundaryTests"`  
   Result: passed (`2/2`).
2. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj -p:BaseOutputPath=".../artifacts/tmp/test-out/"`  
   Result: passed (`348/348`).

## Open Notes

1. Current baseline still contains direct presentation coupling in API controllers (`AuthController`, `OrdersController`).
2. Stage 5 work should reduce this baseline gradually (without breaking current runtime path).

## Next Increment (planned)

1. Reduce approved baseline by moving controller runtime dependencies behind application ports/handlers.
2. Extend architecture tests with additional no-new-coupling rules for newly extracted seams.

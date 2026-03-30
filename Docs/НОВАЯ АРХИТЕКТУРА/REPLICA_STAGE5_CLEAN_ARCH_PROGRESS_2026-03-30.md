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

## Test Evidence

1. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj --filter "ReplicaApiArchitectureBoundaryTests"`  
   Result: passed (`2/2`).

## Open Notes

1. Current baseline still contains direct presentation coupling in API controllers.
2. Stage 5 work should reduce this baseline gradually (without breaking current runtime path).

## Next Increment (planned)

1. Reduce approved baseline by moving controller runtime dependencies behind application ports/handlers.
2. Extend architecture tests with additional no-new-coupling rules for newly extracted seams.

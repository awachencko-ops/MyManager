# Replica Stage 2 Progress (MediatR Command Bus)

Date: 2026-03-27  
Status: In progress

## Scope of this increment

First migration slice of write-paths to MediatR command handlers as required by `REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26` Stage 2.

## Implemented

1. Added MediatR dependencies and DI registration in `Replica.Api`.
2. Added command handlers for order mutating operations:
   - `CreateOrder`
   - `DeleteOrder`
   - `UpdateOrder`
   - `AddOrderItem`
   - `UpdateOrderItem`
   - `DeleteOrderItem`
   - `ReorderOrderItems`
   - `StartOrderRun`
   - `StopOrderRun`
3. Added command handler for user mutating operation:
   - `UpsertUser`
4. Migrated mutating endpoints in `OrdersController` and `UsersController` to dispatch commands through MediatR.
5. Kept compatibility fallback to direct store path when mediator is not available (important for isolated/unit controller construction).
6. Added/ran unit coverage for handlers in `MediatRWriteCommandHandlersTests`.

## Validation

Targeted verify run:

- `MediatRWriteCommandHandlersTests`
- `UsersAdminManagementTests`
- `ApiMutatingEndpointsAuthorizationMatrixTests`
- `OrdersControllerActorValidationTests`

Result: passed (58/58).

## Next Stage 2 steps

1. Add pipeline behaviors:
   - validation behavior,
   - idempotency behavior,
   - transaction behavior,
   - audit/telemetry behavior.
2. Move remaining write-path side effects from controllers/stores into handlers where practical.
3. Expand conflict/replay tests to explicitly cover mediator pipeline execution order.

## Increment Addendum (2026-03-27, pipeline behaviors)

### Implemented

1. Added command contracts:
   - `IReplicaApiWriteCommand`
   - `IReplicaApiIdempotentWriteCommand`
   - `IReplicaApiCommandValidator<TCommand>`
2. Added MediatR pipeline behaviors:
   - `ReplicaApiCommandValidationBehavior<,>`
   - `ReplicaApiCommandTelemetryBehavior<,>`
3. Added validation pack for all migrated write commands (`Orders` + `Users.Upsert`).
4. Added service registration extension:
   - `AddReplicaApiCommandPipeline()`
   - auto-registration of command validators from assembly.
5. Enabled pipeline in runtime composition (`Program.cs`).
6. Prevented duplicate write-metrics for mediator path by keeping controller-level recording only for fallback path (when mediator is unavailable).

### Validation (addendum)

Targeted verify run additionally includes:

- `MediatRCommandPipelineBehaviorsTests`

Result: passed (61/61).

### Remaining Stage 2 backlog

1. Idempotency behavior as dedicated mediator pipeline layer (currently idempotency remains at store/controller boundary).
2. Transaction behavior at mediator level (evaluate alignment with existing store-scoped transactions).
3. Optional: migrate read-side query path to MediatR for symmetrical command/query dispatch.

## Increment Addendum (2026-03-27, idempotency behavior)

### Implemented

1. Added dedicated idempotency MediatR behavior:
   - `ReplicaApiCommandIdempotencyBehavior<,>`
   - fail-fast guard for oversized key (`>128`).
2. Registered idempotency behavior in command pipeline between validation and telemetry.
3. Added verify coverage:
   - `PipelineIdempotency_WhenKeyIsTooLong_ReturnsBadRequest`.

### Validation (addendum)

Targeted verify run result: passed (62/62).

### Remaining Stage 2 backlog

1. Transaction behavior at mediator level (needs careful alignment with existing store transactions).
2. Optional read-side MediatR query path migration.

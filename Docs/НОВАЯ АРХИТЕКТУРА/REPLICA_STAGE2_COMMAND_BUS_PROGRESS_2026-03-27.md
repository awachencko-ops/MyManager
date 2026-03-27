<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.
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

## Increment Addendum (2026-03-27, transaction behavior)

### Implemented

1. Added dedicated mediator transaction behavior:
   - `ReplicaApiCommandTransactionBehavior<,>`.
2. Behavior applies only to write commands (`IReplicaApiWriteCommand`) and creates a serialized write transaction boundary via async gate.
3. Design note: ambient `TransactionScope` intentionally not used because current EF/Npgsql stores already manage explicit DB transactions internally.
4. Registered transaction behavior in pipeline between idempotency and telemetry.
5. Added verify coverage:
   - `PipelineTransaction_WhenConcurrentWriteCommands_ExecutesSerially`.

### Validation (addendum)

Targeted verify run result: passed (63/63).

### Remaining Stage 2 backlog

1. Decide whether to keep serialized boundary as default or introduce feature-flagged parallel write strategy for specific deployments.
2. Optional read-side MediatR query migration.

## Increment Addendum (2026-03-27, transaction gate configurability)

### Implemented

1. Added pipeline options:
   - `ReplicaApiCommandPipelineOptions.EnableSerializedWriteGate` (default `true`).
2. Wired options from config:
   - `ReplicaApi:CommandPipeline:EnableSerializedWriteGate`.
3. Updated transaction behavior to respect the flag.
4. Added verify coverage for both modes:
   - gate enabled => serialized write execution,
   - gate disabled => concurrent write overlap is allowed.

### Validation (addendum)

Targeted verify run result: passed (64/64).

## Increment Addendum (2026-03-27, read-side mediator queries)

### Implemented

1. Added read query handlers:
   - Orders: `GetOrdersQuery`, `GetOrderByIdQuery`
   - Users: `GetUsersQuery`
2. Migrated read endpoints to mediator dispatch with safe fallback (when mediator is unavailable):
   - `OrdersController.GetOrders/GetOrderById`
   - `UsersController.GetUsers/GetAllUsers`
3. Added verify coverage:
   - `MediatRReadQueryHandlersTests`.

### Validation (addendum)

Targeted verify run result: passed (67/67).

## Increment Addendum (2026-03-27, runtime mediator-only cleanup)

### Implemented

1. Controllers switched to mediator-only runtime composition via DI constructor selection (`ActivatorUtilitiesConstructor`).
2. Legacy store fallback preserved only in explicit test constructors for isolated controller tests.
3. Read/write execution helpers now fail fast if mediator is missing in runtime mode.
4. Controller-level write observability fallback remains only for test fallback mode.

### Validation (addendum)

- Verify target pack: passed (67/67).
- UiSmoke target pack: passed (2/2).

## Increment Addendum (2026-03-27, Stage 3 SignalR push kickoff)

### Implemented

1. Added SignalR hub:
   - `ReplicaOrderHub` (`/hubs/orders`).
2. Added push publisher abstraction:
   - `IReplicaOrderPushPublisher`.
3. Added publisher implementations:
   - `SignalRReplicaOrderPushPublisher` (runtime),
   - `NoOpReplicaOrderPushPublisher` (default fallback in DI).
4. Added MediatR push behavior:
   - `ReplicaApiPushNotificationBehavior<,>`.
5. Push events emitted after successful commands:
   - `OrderUpdated`,
   - `OrderDeleted`,
   - `ForceRefresh` (for successful `UpsertUser`).
6. Runtime wiring:
   - `AddSignalR()`,
   - `MapHub<ReplicaOrderHub>("/hubs/orders")`,
   - SignalR publisher registration.

### Validation (addendum)

- Verify target pack: passed (70/70).
- UiSmoke target pack: passed (2/2).

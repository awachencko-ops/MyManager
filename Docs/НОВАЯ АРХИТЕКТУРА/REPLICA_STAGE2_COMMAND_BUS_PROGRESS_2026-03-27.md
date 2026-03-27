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

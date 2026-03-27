<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 3 Progress (SignalR Push)

Date: 2026-03-27  
Status: In progress

## Scope of this increment

Stage 3 kickoff: real-time push notifications from API after successful mutating commands.

## Implemented

1. Added SignalR hub endpoint:
   - `ReplicaOrderHub` on `/hubs/orders`.
2. Added push publisher abstraction:
   - `IReplicaOrderPushPublisher`.
3. Added publisher implementations:
   - `SignalRReplicaOrderPushPublisher` for runtime,
   - `NoOpReplicaOrderPushPublisher` as default safe fallback.
4. Added MediatR behavior:
   - `ReplicaApiPushNotificationBehavior<,>`.
5. Added push events emitted after successful command execution:
   - `OrderUpdated` (all successful order mutations except delete),
   - `OrderDeleted` (successful delete),
   - `ForceRefresh` (successful user upsert).
6. Added runtime wiring:
   - `AddSignalR()`,
   - `MapHub<ReplicaOrderHub>("/hubs/orders")`,
   - SignalR publisher DI registration.
7. Added client-side push adapter:
   - `ILanOrderPushClient` + `SignalRLanOrderPushClient`,
   - reconnect policy with bounded backoff (`0s`, `2s`, `5s`, `10s`, `30s`),
   - robust payload parser for `OrderUpdated/OrderDeleted/ForceRefresh`.
8. Integrated push bridge into WinForms runtime:
   - adapter wired through `OrdersWorkspaceCompositionRoot`,
   - startup/shutdown integration in `OrdersWorkspaceForm` lifecycle,
   - reconnect triggers compensating pull (`ForceRefresh` with `reconnect-resync`).
9. Added coalesced push refresh path in UI:
   - incoming push events schedule storage snapshot refresh with coalescing,
   - merge strategy keeps running local sessions stable while applying server-authoritative updates,
   - `ForceRefresh(users-changed)` additionally refreshes users directory.
10. Added verify tests:
   - payload parsing cases for push events,
   - reconnect backoff policy checks.
11. Added SignalR integration tests (two clients):
   - TestServer host with real `ReplicaOrderHub`,
   - two connected hub clients (`A`, `B`) in one test run,
   - mediator command path (`CreateOrder` / `DeleteOrder`) verified to broadcast to client `B`.

## Validation

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests`): passed (15/15).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (28/28).

## Next Stage 3 steps

1. Add optional client metrics (push lag / reconnect counters) to diagnostics surface.
2. Add resilience guardrails for push storm scenarios (adaptive throttle + bounded queue telemetry).
3. Expand integration coverage to `ForceRefresh(users-changed)` scenario.

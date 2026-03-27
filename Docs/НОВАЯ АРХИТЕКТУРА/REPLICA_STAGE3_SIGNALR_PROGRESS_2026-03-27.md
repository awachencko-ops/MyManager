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
12. Extended integration coverage for `ForceRefresh`:
   - scenario `UpsertUser -> ForceRefresh(users-changed)` verified on client `B`,
   - event parser assertions include `reason` and empty `orderId` for force-refresh payload.
13. Added client push diagnostics + storm guardrails:
   - LAN tooltip now includes push channel diagnostics (`state`, `events/refresh`, `lag`, `reconnects`, `coalesced/throttled`),
   - push refresh loop now applies bounded throttling (`LanPushMinRefreshIntervalMs`) to reduce snapshot-pull pressure during event bursts,
   - coalesced/throttled counters and periodic telemetry logs added for long push storms.

## Validation

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests`): passed (26/26).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (28/28).

## Increment Addendum (2026-03-27, push reason counters + pressure alerts)

### Implemented

1. Extended push diagnostics with per-reason counters for `ForceRefresh` reasons:
   - tracks reason frequency (`users-changed`, `reconnect-resync`, etc.),
   - shows top reasons in LAN tooltip (`Push reason counters`).
2. Extended coalesced/throttled diagnostics line with rate view:
   - displays absolute counters and rates (`coalesced/throttled` with percentages).
3. Added bounded pressure alerts for push storms in runtime logs:
   - alert triggers when coalescing/throttling rates exceed configured thresholds,
   - alert is cooldown-limited to avoid log flooding,
   - alert payload includes current counters/rates and top force-refresh reasons.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests`): passed (17/17).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (28/28).

## Increment Addendum (2026-03-27, reconnect-chaos integration scenario)

### Implemented

1. Added integration scenario `ClientReconnect_WhenMissedPushDuringDisconnect_AllowsPullResyncAndReceivesSubsequentPush`:
   - client `B` is forcibly disconnected from SignalR hub,
   - command mutation is executed while `B` is offline,
   - after reconnect, compensating pull (`GetOrderById`) confirms server-authoritative state is recoverable,
   - subsequent mutation confirms push stream is restored for `B`.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests`): passed (17/17).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (28/28).

## Increment Addendum (2026-03-27, pressure-alert evaluator coverage)

### Implemented

1. Extracted pressure-alert decision rule to `LanPushPressureAlertEvaluator` (pure helper):
   - normalized threshold/cooldown inputs,
   - deterministic decision output (`ShouldAlert`, `CoalescedRate`, `ThrottledRate`).
2. `OrdersWorkspaceForm` pressure logging path now delegates decision logic to evaluator.
3. Added verify test pack `LanPushPressureAlertEvaluatorTests`:
   - below min-events gate,
   - coalesced threshold hit,
   - throttled threshold hit,
   - below-threshold no-alert path,
   - cooldown suppression path,
   - cooldown boundary re-enable path.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests`): passed (26/26).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (28/28).

## Increment Addendum (2026-03-27, operator-facing pressure hint)

### Implemented

1. Added operator-facing hint in LAN push diagnostics tooltip:
   - shows active pressure-alert counters and last alert timestamp,
   - shows human-readable hint while pressure-alert activity is recent.
2. Extended `LanPushPressureAlertEvaluator` with `IsHintActive` rule (window-based activity check).
3. Added verify coverage for hint activity window:
   - no-alert baseline,
   - within-window active,
   - outside-window inactive.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests`): passed (26/26).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (28/28).

## Increment Addendum (2026-03-27, UI reconnect-resync smoke coverage)

### Implemented

1. Added UiSmoke regression test `SR12C_ReconnectState_QueuesReconnectResyncForceRefresh`:
   - simulates `Reconnected` connection-state event on `OrdersWorkspaceForm`,
   - verifies bridge enqueues `ForceRefresh` with reason `reconnect-resync`,
   - verifies reconnect event updates push-event diagnostics state.

### Validation (addendum)

1. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (29/29).
2. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests`): passed (26/26).

## Next Stage 3 steps

1. Consider exposing pressure-alert counters in diagnostics endpoint for centralized monitoring.
2. Add lightweight alert-state reset strategy for long-lived sessions (operator acknowledgement or decay policy).
3. Add integration scenario with repeated reconnect cycles to verify bounded probe/pull behavior under churn.

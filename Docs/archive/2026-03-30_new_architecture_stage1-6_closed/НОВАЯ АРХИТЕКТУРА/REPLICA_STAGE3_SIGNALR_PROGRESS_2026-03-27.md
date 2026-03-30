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

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests` + `ReplicaApiObservabilityTests` + `DiagnosticsControllerTests`): passed (41/41).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (29/29).

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

## Increment Addendum (2026-03-27, server push diagnostics endpoint)

### Implemented

1. Extended API observability with push-channel counters:
   - published/failure totals,
   - per-event counters (`OrderUpdated`, `OrderDeleted`, `ForceRefresh`),
   - push publish success ratio.
2. `SignalRReplicaOrderPushPublisher` now records observability for successful publish and failure paths.
3. Added protected diagnostics endpoint:
   - `GET /api/diagnostics/push` -> returns push counters snapshot for centralized monitoring.
4. Added verify coverage:
   - `ReplicaApiObservabilityTests` for push counters,
   - `DiagnosticsControllerTests` for `GetPushDiagnostics`.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests` + `ReplicaApiObservabilityTests` + `DiagnosticsControllerTests`): passed (40/40).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (29/29).

## Increment Addendum (2026-03-27, pressure alert-state decay/reset)

### Implemented

1. Added lightweight decay/reset policy for long-lived client sessions:
   - pressure-alert state auto-resets after prolonged inactivity window.
2. Extended `LanPushPressureAlertEvaluator` with reset decision rule (`ShouldResetState`).
3. Wired reset policy into LAN push diagnostics snapshot generation.
4. Extended verify coverage for reset boundaries:
   - no-alert baseline,
   - reset when elapsed window exceeded,
   - no reset on exact boundary.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests` + `ReplicaApiObservabilityTests` + `DiagnosticsControllerTests`): passed (41/41).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (29/29).

## Increment Addendum (2026-03-27, reconnect-churn integration scenario)

### Implemented

1. Added integration scenario `ReconnectCycles_WhenRepeated_StopStart_ContinuesReceivingPushEvents`:
   - runs repeated client `stop/start` reconnect cycles,
   - verifies push delivery remains intact after each reconnect cycle.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests` + `ReplicaApiObservabilityTests` + `DiagnosticsControllerTests`): passed (41/41).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (29/29).

## Increment Addendum (2026-03-27, client surfacing of API push diagnostics)

### Implemented

1. Extended LAN probe flow to call `api/diagnostics/push` as optional endpoint.
2. Probe requests now include actor headers (`X-Current-User` / encoded variant) to allow diagnostics access for authorized actors.
3. Extended probe snapshot with API push counters:
   - `PublishedTotal`,
   - `PublishFailuresTotal`,
   - `PublishSuccessRatio`.
4. LAN tooltip now shows server push diagnostics line:
   - `Push API publish/fail: published/failures (success%)`.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests` + `ReplicaApiObservabilityTests` + `DiagnosticsControllerTests`): passed (41/41).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (29/29).

## Increment Addendum (2026-03-27, operator acknowledgement action)

### Implemented

1. Added manual acknowledgement action for active push-pressure warnings on connection-indicator click.
2. Acknowledgement now clears pressure-alert counters/state (`count` + `last-alert-at`) and logs explicit marker (`pressure-alert-acknowledged`).
3. Added UiSmoke regression test `SR12D_ToolConnectionClick_AcknowledgesPushPressureAlertState`.

### Validation (addendum)

1. UiSmoke targeted tests (`SR12D`): passed (1/1).
2. Stage 3 targeted packs remained green after increment.

## Increment Addendum (2026-03-27, settings-driven push-pressure tuning)

### Implemented

1. Moved push-pressure tuning knobs into `AppSettings`:
   - refresh throttle interval,
   - min events gate,
   - coalesced/throttled rate thresholds,
   - cooldown/hint/reset windows.
2. Added normalization guardrails in settings load path:
   - invalid values fallback to safe defaults,
   - reset window is enforced to be `>=` hint window.
3. `OrdersWorkspaceForm.LoadSettings()` now applies tuned values from settings into runtime Stage 3 push logic.
4. Added UiSmoke regression tests:
   - `SR12E_LoadSettings_AppliesLanPushMonitoringSettings`,
   - `SR12F_LoadSettings_NormalizesInvalidLanPushMonitoringSettings`.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests` + `ReplicaApiObservabilityTests` + `DiagnosticsControllerTests`): passed (41/41).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (32/32).

## Increment Addendum (2026-03-27, diagnostics auth-fallback contract coverage)

### Implemented

1. Added UiSmoke contract regression `SR12G_ProbeLanServer_PushDiagnosticsOptionalFallback_DoesNotBreakSnapshot`.
2. Test covers optional endpoint fallback statuses for `api/diagnostics/push`:
   - `401 Unauthorized`,
   - `403 Forbidden`,
   - `404 Not Found` (`optional-unavailable` path).
3. Added local probe stub server in UiSmoke tests to emulate full LAN probe endpoint set (`live/ready/slo/metrics/diagnostics` + push diagnostics status variants).
4. Assertions confirm that for optional push diagnostics failures:
   - base probe remains healthy (`ApiReachable=true`, `IsReady=true`),
   - push diagnostics counters stay unresolved (`-1`) and do not poison probe error state.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests` + `ReplicaApiObservabilityTests` + `DiagnosticsControllerTests`): passed (41/41).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (35/35).

## Increment Addendum (2026-03-27, push tuning controls in settings UI)

### Implemented

1. Extended `SettingsDialogForm` with LAN Push tuning controls:
   - `min refresh (ms)`,
   - `min events alert`,
   - `coalesced/throttled` thresholds (`0..1`),
   - `alert cooldown / hint window / reset window` (seconds).
2. Added dialog-level validation guard:
   - `reset window >= hint window`.
3. Integrated settings flow in `OrdersWorkspaceForm.ShowSettingsDialog()`:
   - values are persisted to `AppSettings`,
   - values are applied to runtime push-pressure fields without restart.

### Validation (addendum)

1. Verify targeted pack (`SignalRPushIntegrationTests` + `LanOrderPushClientTests` + `MediatRPushNotificationsBehaviorTests` + `LanPushPressureAlertEvaluatorTests` + `ReplicaApiObservabilityTests` + `DiagnosticsControllerTests`): passed (41/41).
2. UiSmoke targeted pack (`MainFormCoreRegressionTests` + `MainFormSmokeTests`): passed (35/35).

## Next Stage 3 steps

1. Start Stage 4 implementation using `REPLICA_STAGE4_DUAL_WRITE_CHECKLIST_2026-03-27.md` as execution gate.
2. Add optional UX polish for settings tab (group headers/tooltips for LAN Push knobs to simplify operator onboarding).

<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.
# Replica — Service-First Roadmap (Client/Server, Pull+Push, MediatR)

Дата: 2026-03-26  
Статус: In progress  
Контур: WinForms Client + ASP.NET Core API + PostgreSQL + HTTP(Pull) + SignalR(Push)

## 0) Что уже сделано (baseline для нового плана)

1. В проекте уже есть API-контур `Replica.Api` с EF Core, PostgreSQL миграциями и write-idempotency.
2. Критичные UI use-cases вынесены из формы в сервисный слой (`IOrderApplicationService` и набор command/mutation services).
3. Реализованы API-first пути для ключевых операций заказов (run/stop/create/update/items/delete), плюс correlation/idempotency и actor validation.
4. Есть переходный dual-sync контур `history.json <-> PostgreSQL` и bootstrap marker в БД.

**Вывод:** мы не начинаем с нуля; новый план должен закрепить server authoritative model, убрать остаточную orchestration-логику из UI и довести архитектуру до событийно-реактивной (Push-Pull) с безопасным cutover.

---

## Этап 1 — Security Foundation + Command Boundary Hardening

### Что делаем
- **Backend**: вводим обязательную AuthN/AuthZ политику для всех mutating endpoints; роли минимум `Admin`/`Operator`; deny-by-default.
- **Database**: таблицы/claims для ролей и аудита auth-событий (login/refresh/revoke).
- **Client**: login flow, хранение токена в защищённом хранилище, прокидка bearer/api-key в каждый HTTP/SignalR запрос.

### Паттерн
- **Service-First + Policy Enforcement Point**: контроллеры тонкие, бизнес-операции только через application services/handlers.
- **RBAC**: role-based authorization на уровне endpoint + command handler guard.

### Как тестируем
- Unit: authorization handlers/policies, token validation, role matrix.
- Integration: `401/403/200` сценарии на каждый write endpoint.
- E2E smoke: `Operator` не может admin-операции, `Admin` может.

---

## Этап 2 — MediatR как единая шина команд

### Что делаем
- **Backend**: все write use-cases переводим на `IRequest`/`IRequestHandler` (`Create/Run/Stop/Delete/Update/Reorder`).
- **Database**: транзакционные границы на handler уровне + optimistic concurrency (`StorageVersion`) как обязательный инвариант.
- **Client**: клиент больше не оркестрирует бизнес-ветвления, только отправляет команду и применяет outcome.

### Паттерн
- **CQRS-lite + Mediator Pipeline**:
  - Validation behavior,
  - Idempotency behavior,
  - Transaction behavior,
  - Audit/Telemetry behavior.

### Как тестируем
- Unit: отдельный тест-пак на каждый handler (happy path, conflict, validation fail, idempotent replay).
- Integration: конфликт версий, rollback при исключении, replay with same idempotency key.

---

## Этап 3 — Реактивность: SignalR + Hybrid Push-Pull

### Что делаем
- **Backend**: добавляем `OrderHub`; после успешной команды публикуем доменное уведомление (`OrderUpdated`, `OrderDeleted`, `ForceRefresh`).
- **Database**: outbox-таблица (или reliable publish policy) для гарантированной доставки событий после commit.
- **Client**: при старте открываем HubConnection; при push обновляем локальный snapshot точечно; при reconnect делаем компенсирующий pull.

### Паттерн
- **Hybrid Push-Pull**:
  - Pull = initial sync/recovery,
  - Push = low-latency updates.
- **Post-Commit Notification** через MediatR pipeline/notification handlers.

### Как тестируем
- Unit: notification handlers + mapping domain event -> hub event.
- Integration: два клиента, изменение у клиента A мгновенно видно у B.
- Reliability: reconnect/temporary disconnect, затем консистентный resync.

---

## Этап 4 — Data Migration: Controlled Dual-Write (history.json -> PostgreSQL)

### Что делаем
- **Backend**: включаем migration-mode: запись одновременно в PostgreSQL (primary) и в `history.json` (shadow mirror) ограниченный период.
- **Database**: migration markers, checksums, reconciliation reports, дедуп по `InternalId` + version.
- **Client**: UI работает только с API snapshot; локальный файл не используется как source of truth.

### Паттерн
- **Strangler Fig + Dual-Write + Reconciliation Job**.

### Как тестируем
- Unit: dual-write policy (primary success + mirror fail/alert), reconcile rules.
- Integration: импорт исторических данных, повторный импорт без дублей.
- Verification: ежедневный diff-report `json vs pg` до нулевого расхождения.

---

## Этап 5 — Clean Architecture Repackaging (Replica.Client / Replica.Api)

### Что делаем
- **Backend**: финализируем разделение на Domain/Application/Infrastructure/Presentation.
- **Client**: UI переводим в presenter/view-model слой; все операции через `IOrderApplicationService` и API gateways.
- **Database**: инфраструктурные адаптеры и миграции остаются изолированными в Infrastructure.

### Паттерн
- **Clean Architecture + Vertical Slices** (по фичам Orders/Auth/Users).

### Как тестируем
- Unit: application/domain без зависимостей на UI/EF.
- Architecture tests: запрет ссылок Presentation -> Infrastructure напрямую.
- Regression: существующие verify/ui-smoke без деградации UX.

---

## Этап 6 — Cutover + Decommission legacy file flow

### Что делаем
- **Backend**: выключаем dual-write, PostgreSQL становится единственным persistence контуром.
- **Database**: финальный migration checkpoint + backup snapshot.
- **Client**: удаляем legacy code-paths чтения/записи `history.json` из runtime; оставляем только import/archive utility.

### Паттерн
- **Feature Flags + Progressive Cutover** (canary users -> all users).

### Как тестируем
- Go/No-Go checklist: auth, command bus, push events, rollback plan.
- Load/soak: стабильность SignalR + API под конкурентной работой.
- Post-cutover monitoring: SLO/latency/error budget/failed commands.

---

## Целевая структура папок (Clean Architecture)

## Replica.Api

```text
Replica.Api/
  src/
    Replica.Api.Presentation/
      Controllers/
      Hubs/
      Contracts/
      Middleware/
    Replica.Api.Application/
      Abstractions/
      Orders/
        Commands/
        Queries/
        Handlers/
        Validators/
      Auth/
      Behaviors/
    Replica.Api.Domain/
      Orders/
      Users/
      Events/
      ValueObjects/
    Replica.Api.Infrastructure/
      Persistence/
        DbContext/
        Configurations/
        Migrations/
        Repositories/
      Auth/
      SignalR/
      Observability/
  tests/
    Replica.Api.UnitTests/
    Replica.Api.IntegrationTests/
    Replica.Api.ArchTests/
```

## Replica.Client (WinForms)

```text
Replica.Client/
  src/
    Replica.Client.Presentation/
      Forms/
      ViewModels/
      Binding/
      SignalR/
    Replica.Client.Application/
      Services/
      UseCases/
      DTO/
      Ports/
    Replica.Client.Domain/
      Models/
      Policies/
    Replica.Client.Infrastructure/
      Http/
      Auth/
      Caching/
      LocalStorage/
  tests/
    Replica.Client.UnitTests/
    Replica.Client.UiSmokeTests/
```

---

## Safety Guard (не потерять старые заказы при миграции)

1. **Immutable backup перед cutover**: snapshot `history.json` + pg dump (с датой/хэшем).
2. **Dual-write window**: фиксированный период (например, 2 недели) с daily reconciliation.
3. **Reconciliation SLA**: любые расхождения `json vs pg` блокируют переключение на следующий этап.
4. **Idempotent re-import**: повторный импорт безопасен, дубли невозможны (ключ `InternalId + ItemId + Version`).
5. **Read fallback только для recovery tool**: runtime не читает JSON после cutover, но аварийный recovery-утилитой доступен.
6. **Audit trail**: каждое изменение заказа имеет `correlation_id`, actor, timestamp, command type.
7. **Rollback protocol**: документированный playbook на случай критического инцидента (RPO/RTO, кто принимает решение, какой шаг отката).

---

## План внедрения по приоритету

1. Stage 1 (Security) — блокирующий.
2. Stage 2 (MediatR command bus) — блокирующий.
3. Stage 3 (SignalR push) — высокий приоритет для multi-client консистентности.
4. Stage 4 (Dual-write migration) — обязательный перед полным cutover.
5. Stage 5-6 (repackaging + decommission) — после стабилизации production-потока.

## Execution Log

1. `2026-03-27`: старт фактического исполнения roadmap.
2. Stage 1 backend increment зафиксирован в `REPLICA_STAGE1_SECURITY_PROGRESS_2026-03-27.md`:
   - token lifecycle (`login/refresh/revoke`),
   - bearer/api-key validation в current-user middleware,
   - auth sessions/audit persistence + migration,
   - unit coverage на token validation и bearer role-resolution.
3. Stage 1 client auth increment:
   - единый `ILanApiAuthSessionStore` (`DPAPI` + in-memory fallback),
   - auto-bearer для LAN identity/write/run gateways,
   - bootstrap session через `POST /api/auth/login`,
   - retry fallback на actor-header при `401` bearer.
4. Stage 1 client token refresh increment:
   - превентивный refresh перед истечением `expires_at` через `POST /api/auth/refresh`,
   - единый helper `LanApiAuthSessionHttpFlow` для refresh policy,
   - refresh policy подключена в identity/write/run gateways,
   - unit coverage: refresh path для identity/write/run.
5. Stage 1 client session UX increment:
   - профиль пользователя показывает текущее состояние auth-схемы (`AuthScheme`),
   - добавлено ручное управление сессией `Войти/Выйти`,
   - `LogoutAsync` ревокает bearer-сессию через `POST /api/auth/revoke` и очищает локальный token-store.
6. Stage 1 authorization matrix increment:
   - добавлен test-pack `ApiMutatingEndpointsAuthorizationMatrixTests`,
   - покрыта матрица `401/403/allowed` для mutating endpoints `Orders/Auth/Users`,
   - зафиксирована admin-граница для `UsersController.UpsertUser`.


## Execution Log Addendum

6. `2026-03-27`: Stage 1 command-boundary hardening finalized:
   - store-level admin role guard for `UpsertUser` across all stores,
   - verify coverage for guard enforcement.
7. `2026-03-27`: Stage 2 command-bus kickoff:
   - MediatR wired in `Replica.Api`,
   - mutating `Orders` + `Users.Upsert` endpoints routed through request handlers,
   - progress tracked in `REPLICA_STAGE2_COMMAND_BUS_PROGRESS_2026-03-27.md`.
8. `2026-03-27`: Stage 2 pipeline increment:
   - command contracts (`IReplicaApiWriteCommand`, validator abstraction),
   - MediatR pipeline behaviors for command validation + telemetry,
   - runtime registration via `AddReplicaApiCommandPipeline()`,
   - verify coverage extended with `MediatRCommandPipelineBehaviorsTests`.
9. `2026-03-27`: Stage 2 idempotency pipeline increment:
   - dedicated mediator idempotency behavior (`key length <= 128` guard),
   - verify coverage for pipeline idempotency failure path.
10. `2026-03-27`: Stage 2 transaction behavior increment:
   - mediator-level write transaction boundary (`ReplicaApiCommandTransactionBehavior`),
   - verify coverage for serialized concurrent write command execution.
11. `2026-03-27`: Stage 2 transaction gate configurability increment:
   - added config flag `ReplicaApi:CommandPipeline:EnableSerializedWriteGate` (default enabled),
   - verify coverage for enabled/disabled transaction gate modes.
12. `2026-03-27`: Stage 2 read-side mediator increment:
   - `GET` endpoints for orders/users moved to mediator query handlers with fallback path,
   - verify coverage added for query handlers.
13. `2026-03-27`: Stage 2 runtime cleanup increment:
   - controllers switched to mediator-only runtime path,
   - legacy store fallback kept only for isolated tests,
   - verify/ui-smoke checks passed after cleanup.
14. `2026-03-27`: Stage 3 SignalR push kickoff:
   - added `ReplicaOrderHub` (`/hubs/orders`) and push publisher abstraction,
   - added mediator push behavior with `OrderUpdated/OrderDeleted/ForceRefresh` events,
   - verify/ui-smoke checks passed for kickoff increment.
15. `2026-03-27`: Stage 3 client SignalR bridge increment:
   - added client adapter `ILanOrderPushClient` with `HubConnection` lifecycle and reconnect policy,
   - integrated push bridge into `OrdersWorkspaceForm` startup/shutdown lifecycle,
   - added coalesced push-triggered snapshot refresh (`OrderUpdated/OrderDeleted/ForceRefresh`) with reconnect resync,
   - verify/ui-smoke checks passed for client bridge increment.
16. `2026-03-27`: Stage 3 integration-test increment:
   - added SignalR TestServer integration tests with two clients (`A/B`) and real hub broadcast path,
   - covered scenarios: `CreateOrder -> OrderUpdated` and `DeleteOrder -> OrderDeleted` observed by client `B`,
   - verify/ui-smoke checks passed after integration coverage extension.

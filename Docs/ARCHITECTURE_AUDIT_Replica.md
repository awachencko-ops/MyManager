# Архитектурный аудит Replica (текущий код Replica)

> Контекст: аудит выполнен по текущему монолитному WinForms-приложению, которое работает с файловой системой/NAS и JSON-файлами конфигурации/истории.

> Актуализация на 2026-03-19 (этап 2): live-проверка PostgreSQL выполнена, `history.json` импортирован в `replica_db` (10 orders / 11 items), marker `history_json_bootstrap_v1` записан в `storage_meta`, orphan-items не обнаружены; добавлен интеграционный PostgreSQL regression-pack (single/group roundtrip, concurrency conflict, event append).
>
> Актуализация на 2026-03-19 (этап 3, Step 1): добавлены `Replica.Shared` и `Replica.Api`, поднят LAN API skeleton (`/health`, `/api/users`, `/api/orders` + write endpoints), подтверждён smoke-run API (`200` на health/users/orders). Полный cutover клиента на HTTP boundary и server-side orchestration остаются в Step 2 этапа 3.
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress): API переключён на `EfCoreLanOrderStore` + `ReplicaDbContext` (EF Core), добавлена baseline migration `20260320000100_BaselineSchema`, health подтверждает store mode `PostgreSql`; в клиенте вынесена repository/bootstrap логика из `MainForm` в `OrdersHistoryRepositoryCoordinator`.
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 2): добавлен `OrderRunStateService`, а методы `RunSelectedOrderAsync`/`StopSelectedOrder` переведены на сервисное управление run-state (план runnable/skipped + lifecycle токенов).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 3): добавлен `OrderStatusTransitionService`, а `SetOrderStatus` переведён на сервисное применение status-transition policy (нормализация source/reason, единое условие no-op).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 4): добавлены server-side `run/stop` endpoints (`POST /api/orders/{id}/run|stop`) и централизованный lock-state (`order_run_locks`) в `EfCoreLanOrderStore`; подтверждено PostgreSQL integration-тестами (lifecycle lock/events + version mismatch).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 5): в клиенте добавлен `LanOrderRunApiGateway`; в режиме `LanPostgreSql` команды `Run/Stop` идут через API boundary (`/api/orders/{id}/run|stop`) с локальным snapshot-refresh перед следующим `SaveHistory`.
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 6): добавлен `LanRunCommandCoordinator` и интерфейсный контракт `ILanOrderRunApiGateway`; orchestration LAN `run/stop` вынесена из `MainForm` в сервисный слой, покрыта unit-тестами coordinator (`success/conflict/fatal/stop`).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 7): добавлен `OrderRunExecutionService`; конкурентное выполнение run-сессий (`Task.WhenAll`, cancel/error handling, completion callbacks) вынесено из `MainForm` в сервисный use-case слой, покрыто unit-тестами (`success/cancel/failure/mixed`).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 8): добавлена двусторонняя sync-логика history в `OrdersHistoryRepositoryCoordinator` (`history.json <-> PostgreSQL`): `file->db` импорт отсутствующих заказов по `InternalId`, `db->file` зеркалирование актуального снимка; подтверждено integration-тестом coordinator sync.
>
> Актуализация на 2026-03-20 (этап 3, Step 2 close, срез 9): закрыт client cutover `run/stop` в LAN-режиме (локальный `_runTokensByOrder` больше не источник истины для server run-state), добавлена обязательная actor validation для write-endpoints (`X-Current-User`) и request-level `X-Correlation-Id` middleware.
>
> Актуализация на 2026-03-20 (этап 4 close, срез 10): внедрён auto-update baseline (client bootstrap + `wwwroot/updates/update.xml` feed), этап 4 закрыт; в рамках risk-burndown убраны silent `catch { }` в критическом runtime-пути (`OrderProcessor`, `OrderForm`, `ConfigService`) с заменой на контролируемый fallback + warning-лог.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 11): добавлен `OrderDeletionWorkflowService`; batch-удаление заказов/файлов (`remove-from-disk`, fallback на known paths, item reindex, агрегация ошибок) вынесено из `MainForm` в сервисный слой, покрыто unit-тестами (`orders delete`, `folder-miss fallback`, `item reindex`, `item-not-found`).
>
> Актуализация на 2026-03-20 (risk-burndown, срез 12): введён `ISettingsProvider` + `FileSettingsProvider`; runtime-path (`Program`/`MainForm`/`OrderProcessor`) переведён на provider-инъекцию, `ConfigService` отвязан от прямого `AppSettings.Load()` через `ConfigService.SettingsProvider`, добавлены unit-тесты provider-boundary.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 13): в `OrderProcessor` внедрён `FileOperationRetryPolicy` (retry+backoff для copy/move/delete/create/read), file-операции переведены на policy boundary с retry-telemetry (`FILE-RETRY`), добавлены unit-тесты policy и обновлены UI smoke-тесты cleanup-сценариев.

## Executive summary

- Текущая реализация **не готова** к роли транзакционно-безопасной платформы на сотни пользователей.
- Главные причины: API/worker-контур пока не доведён до production-boundary (authN/authZ, idempotency, full client cutover и observability всё ещё неполные), UI-центричная оркестрация остаётся значимой.
- В коде уже закрыт значимый кусок миграции: введён `IOrdersRepository`, реализован LAN PostgreSQL backend с optimistic concurrency (`StorageVersion` + conflict guard), добавлен `order_events` и one-time bootstrap marker в `storage_meta`; на этапе 3 добавлены API skeleton, EF Core storage слой, server-side `run/stop` lock-координация (`order_run_locks`), клиентские `LanOrderRunApiGateway` + `LanRunCommandCoordinator` и выносы из `MainForm` в сервисы (`OrdersHistoryRepositoryCoordinator`, `OrderRunStateService`, `OrderStatusTransitionService`, `OrderRunExecutionService`, `OrderDeletionWorkflowService`) + двусторонняя sync `history.json <-> PostgreSQL`.

---

## 1) Архитектурная целостность (Clean Architecture)

### Наблюдения

- Точка входа поднимает сразу WinForms (`Application.Run(new MainForm())`), без явного composition root для бизнес-слоя/инфраструктуры.
- `MainForm` агрегирует orchestration, хранение истории, статус-машину, UI-binding, файловые операции и запуск процессора; состояние формы содержит десятки полей и коллекций.
- Добавлен API-каркас (`Replica.Api`) и shared-контракты (`Replica.Shared`); клиент уже частично переведён на API gateway (`run/stop`), но полный cutover всех write-flow ещё не завершён.
- Из `MainForm` выделен `OrdersHistoryRepositoryCoordinator` (инициализация repository, bootstrap/fallback/event append), что уменьшило размер и связность части persistence-логики.
- Из `MainForm` выделен `OrderRunStateService` (run-state lifecycle и фильтрация runnable/skipped заказов), что уменьшило связность части run/stop orchestration.
- Из `MainForm` выделен `OrderStatusTransitionService` (policy status-transition и нормализация reason/source), что уменьшило связность статусной логики.
- В клиенте добавлен `LanOrderRunApiGateway`: `Run/Stop` в `LanPostgreSql` mode вызывают API endpoints вместо прямой локальной координации.
- В клиенте добавлен `LanRunCommandCoordinator`: LAN `run/stop` orchestration вынесена из `MainForm` в отдельный сервис (форма теперь использует coordinator, а не прямую LAN gateway-логику).
- Из `MainForm` выделен `OrderRunExecutionService`: конкурентное выполнение run-сессий и error/cancel lifecycle больше не оркестрируются внутри формы.
- Из `MainForm` выделен `OrderDeletionWorkflowService`: batch-удаление orders/items (включая disk-cleanup, fallback на known paths и reindex item-ов) переведено в use-case сервис.
- Введён интерфейсный слой настроек (`ISettingsProvider`), а core runtime-flow (`Program`, `MainForm`, `OrderProcessor`, `ConfigService`) переведён с прямого static-IO на provider boundary.
- В `OrdersHistoryRepositoryCoordinator` добавлена двусторонняя sync-стратегия `history.json <-> PostgreSQL` (импорт file-only заказов + mirror LAN snapshot обратно в файл).
- Persistence реализован через прямое чтение/запись JSON (`history.json`) из UI-слоя.
- `ConfigService` и `AppSettings` — статические сервисы/конфиги с прямым file IO, без интерфейсов и DI.

### Вывод

- SoC нарушен: UI-layer контролирует use-case/persistence.
- Налицо «God Object» в виде `MainForm` (+ partial-файлы как физическое разделение, но не архитектурное), хотя декомпозиция уже заметно продвинута (вынесены persistence/run-state/status-transition/LAN run-coordinator/run-execution).
- Замена persistence/API слоя потребует массового рефакторинга из-за сильной связности и отсутствия портов/адаптеров.

---

## 2) Транзакционная надежность (Transactional Integrity)

### Наблюдения

- Введён слой хранения через `IOrdersRepository` с режимами `FileSystem` и `LanPostgreSql` (feature-gate в настройках).
- В модели добавлены version-поля (`OrderData.StorageVersion`, `OrderFileItem.StorageVersion`).
- В `PostgreSqlOrdersRepository` реализован optimistic concurrency: version-check на update/delete и внешний conflict-guard перед save.
- Для первичного переноса истории добавлен one-time marker `history_json_bootstrap_v1` в `storage_meta`.
- В API реализована централизованная lock-координация `run/stop` через `order_run_locks` (`EfCoreLanOrderStore`, endpoints `POST /api/orders/{id}/run|stop`).
- В клиенте `Run/Stop` для `LanPostgreSql` идут через API (`LanOrderRunApiGateway`) и координируются через `LanRunCommandCoordinator`; локальный `OrderRunStateService` оставлен как runtime-state для активной сессии обработки.
- Для совместимости с текущим repository-слоем добавлен snapshot-refresh после server `run/stop`, чтобы исключить ложные `concurrency conflict` на следующем `SaveHistory`.
- Идемпотентности API пока нет: серверный API существует как skeleton, но command boundary/idempotency key ещё не внедрены.

### Вывод

- Риск `lost update` существенно снижен в LAN-режиме за счёт optimistic concurrency и запрета silent overwrite при конфликте.
- Полной транзакционной модели уровня API/command handling пока нет (нет idempotency keys и полного cutover всех write-flow клиента на server-command boundary).
- Повторные команды со стороны клиента не имеют глобальных idempotency key и дедупликации на сервере.

---

## 3) Отказоустойчивость и Resiliency

### Наблюдения

- Есть polling-ожидание файлов с timeout (`WaitForFileAsync`, `WaitForFileInAnyAsync`) и отмена по `CancellationToken`.
- В `OrderProcessor` добавлен retry/backoff policy (`FileOperationRetryPolicy`) для file-операций (copy/move/delete/create/read) с логированием попыток и exhausted-событий.
- В нескольких местах ошибки suppress-ятся (`catch { }`), что скрывает деградации.
- Нет circuit breaker / bulkhead / load shedding.
- Архитектура single-process: падение/фриз UI-компонента критично для всего потока выполнения.

### Вывод

- Устойчивость к «дрожащей» инфраструктуре улучшена за счёт retry/backoff на критичных file-операциях, но до production-resilience ещё нужны circuit-breaker/bulkhead и health-state внешних зависимостей.

---

## 4) Наблюдаемость (Observability & Audit)

### Наблюдения

- Логгер пишет plain-text строки в файл, без структурированных полей, trace/span/correlation id.
- Лог статусов заказа (`AppendOrderStatusLog`) текстовый, best-effort, с глушением ошибок записи.
- В PostgreSQL введён событийный журнал `order_events` (CRUD-события репозитория + `run/stop/delete/topology/add-item/remove-item/status-change` из клиентских workflow-точек).
- Распределенной трассировки нет (и отсутствует распределенная архитектура на текущем этапе).
- `order_events` хранится в БД и снижает риск mutable file-audit; добавлены базовые actor identity (write path) и request correlation id, но end-to-end трассировка и формализованные audit-дашборды пока отсутствуют.

### Вывод

- Для форензики инцидентов ситуация улучшилась (есть DB event log), но observability всё ещё недостаточна для SLA/SRE-уровня.
- Нет надежной корреляции «кто/когда/какой запрос/какой этап пайплайна» в стандартизированном виде.

---

## 5) Безопасность (Security by Design)

### Наблюдения

- Бизнес-данные формируются и обрабатываются в thick-client без server-side validation boundary.
- Есть базовая санитаризация имени папки по invalid file chars в `OrderForm`, но отсутствует централизованный validation policy на уровне домена.
- Конфигурация содержит hardcoded сетевые пути по умолчанию (`\\NAS\...`), однако явных паролей/connection string с секретами в коде не найдено.
- Ошибки часто глушатся, что затрудняет выявление атак/аномалий.

### Вывод

- Для enterprise-эксплуатации нужно вводить серверный trust boundary, авторизацию, централизованную валидацию команд и секрет-менеджмент (env/vault), даже если сейчас «секретов в коде» почти нет.

---

## Матрица рисков (Компонент | Риск | Критичность | Рекомендация)

| Компонент | Риск | Критичность | Рекомендация |
|---|---|---|---|
| `MainForm` orchestration | God Object, смешение UI + domain + persistence + file IO (снижено сервисными выносами, включая delete-workflow) | **Med/High** | Продолжить декомпозицию: выделить use-case слой (`IOrderApplicationService`), UI оставить как presenter/view; внедрить DI/composition root. |
| История заказов (`history.json` / LAN PostgreSQL) | В FileSystem-режиме остаётся риск race; в LAN-режиме риск снижен через version-check | **Med** | Оставить FileSystem только как fallback; целевой режим — PostgreSQL + server-side command boundary. |
| `SetOrderStatus` + `SaveHistory` | Клиентская неатомарность между UI-операцией и persistence | **Med/High** | Перенести статусные команды в API/worker с unit of work и server-side invariants. |
| `_runTokensByOrder` (in-memory) | Переведён в runtime-session state; риск смещён в сторону UX-согласованности между клиентами | **Low/Med** | Сохранить server lock/state единственным источником истины и расширять server-driven refresh-сценарии. |
| `OrderProcessor` file workflow | Retry/backoff внедрён в core file-операции; остаточный риск — отсутствие circuit breaker/bulkhead | **Med** | Следующий шаг: circuit-breaker + dependency health-state + timeout budget per stage. |
| Ожидание hotfolder | Polling без circuit breaker | **Med** | Добавить circuit breaker + health state для внешних зависимостей (PitStop/Imposing/NAS). |
| Логирование (`Logger`) | В API введён базовый request correlation (`X-Correlation-Id`), но нет полного end-to-end structured telemetry | **Med/High** | Довести до единого structured logging/tracing контура (client+api+worker). |
| Order status log file | best-effort append, mutable file (частично компенсировано `order_events`) | **Med** | Сделать `order_events` primary audit source, добавить retention/архив и SQL-аудит отчёты. |
| Ошибки с `catch { }` | В критическом runtime-пути silent catches устранены; остаточный риск остаётся в legacy/UI-участках | **Low/Med** | Поддерживать policy: без silent catch в production-path; остаточные блоки вычищать по итерациям. |
| ConfigService/AppSettings static IO | Сильная связность с файловой системой частично снижена (`ISettingsProvider` внедрён в runtime-path); остаток в legacy/UI-экранах | **Low/Med** | Довести до полного покрытия provider/repository boundary на всех формах и убрать остаточные direct `AppSettings.Load()`. |
| Отсутствие API идемпотентности | Дубли заказов при повторной отправке | **High** | В API-командах использовать `Idempotency-Key` + таблицу дедупликации. |
| Отсутствие полного authN/authZ контура | Базовая actor validation write-path уже есть, но role/claim policy и полноценная authN не внедрены | **Med/High** | Ввести API authN/authZ (JWT/SSO + role/claim-based authorization). |
| Валидация входных данных | Локальная и фрагментарная | **Med** | Централизовать validation на command DTO/domain rules, добавить schema/contract validation. |
| Хардкод дефолтных путей NAS | Сложность portability/segmentation | **Low/Med** | Вынести в environment-specific config profiles, добавить проверку доступности на старте. |
| Отсутствие распределенной трассировки | Невозможно проследить end-to-end через сервисы | **Med (сейчас), High (после разделения)** | Внедрить OpenTelemetry tracing заранее в новом API/worker-контуре. |

---

## Статус закрытия матрицы рисков (итерации)

1. Итерация 1 (2026-03-20): закрыт риск silent `catch { }` в критическом runtime-пути.
   - Что сделано: заменены silent catches на контролируемый fallback с логированием в `OrderProcessor`, `OrderForm`, `ConfigService`.
   - Эффект: снижен риск «немых» деградаций при file/workflow и config-операциях.
2. Итерация 2 (2026-03-20): закрыт следующий срез `MainForm` God Object (delete-workflow).
   - Что сделано: удаление заказов и item-ов вынесено в `OrderDeletionWorkflowService` (disk cleanup + fallback + reindex + batch failure aggregation), `MainForm` переключён на сервис, добавлены unit-тесты.
   - Эффект: снижена связность `MainForm` и риск регрессий в delete-сценариях за счёт выделенного use-case слоя и автотестов.
3. Итерация 3 (2026-03-20): закрыт срез `ConfigService/AppSettings` static IO для runtime-path.
   - Что сделано: добавлены `ISettingsProvider` + `FileSettingsProvider`; `Program`, `MainForm`, `OrderProcessor` и `ConfigService` переведены на provider boundary; добавлены unit-тесты `ConfigService` на injected provider path.
   - Эффект: повышена тестируемость и снижена связность core-path с файловой системой/статикой.
4. Итерация 4 (2026-03-20): закрыт срез resiliency в `OrderProcessor` (retry/backoff policy).
   - Что сделано: добавлен `FileOperationRetryPolicy`; file-операции `copy/move/delete/create/read` переведены на policy boundary, добавлен telemetry-контур `FILE-RETRY`, добавлены unit-тесты policy и обновлены UI smoke-тесты cleanup.
   - Эффект: снижен риск фейлов от кратковременных NAS/file-lock сбоев и улучшена диагностируемость file-workflow.
5. На очереди (итерация 5): dependency health-state + circuit-breaker для hotfolder-интеграций.
   - План следующего среза: добавить health-marker внешних контуров (PitStop/Imposing/NAS), предохранитель деградации и UI-индикацию readiness.

---

## Технический долг: что «сжечь и переписать» сейчас

### P0 (делать немедленно)

1. **Persistence-модель на JSON в UI** → заменить на backend + PostgreSQL.
2. **Статусные переходы и аудит «мимо транзакций»** → сделать transactional command handling.
3. **God Object (MainForm как бизнес-оркестратор)** → выделить application layer и инфраструктурные адаптеры (In progress: уже вынесены history/run-state/status-transition/run-execution/delete-workflow).
4. **Неструктурированное логирование и mutable file-audit** → заменить на structured logs + append-only `order_events`.

### P1 (сразу после P0)

1. Ввести **optimistic concurrency** (`row_version`) и конфликто-разрешение.
2. Ввести **idempotency** для write-операций API.
3. Добавить **resilience policies** (retry/circuit breaker/timeouts/bulkhead).
4. Ввести **health-checks + readiness/liveness + SLO метрики**.

### P2 (масштабирование на сотни пользователей)

1. Разделить pipeline на сервисы/воркеры: `Order API`, `Processing Worker`, `Integration Adapter (PitStop/Imposing)`.
2. Event-driven интеграция (outbox/inbox pattern).
3. Observability platform: logs+metrics+traces, correlation id везде.

---

## Целевая архитектура Replica (рекомендуемая)

- **Client (WinForms/Web)**: только UI и orchestration UX.
- **Replica API (Application Layer)**: команды/запросы, валидация, authZ.
- **Domain Layer**: агрегаты `Order`, `OrderItem`, инварианты и статусная машина.
- **Infrastructure Layer**:
  - PostgreSQL (orders, order_items, order_events, idempotency_keys);
  - message broker/queue для долгих задач;
  - adapters к NAS/PitStop/Imposing.
- **Worker Layer**: выполнение файловых операций и внешних интеграций под контролем retry/circuit-breaker.
- **Observability Layer**: OpenTelemetry + централизованный лог-стек + аудит.

---

## Минимальный roadmap миграции (8–12 недель)

1. **Week 1–2**: выделение contracts/shared domain, API skeleton, миграция модели `Order` + `OrderEvent`.
2. **Week 3–4**: PostgreSQL + EF migrations, optimistic concurrency, идемпотентность create/update.
3. **Week 5–6**: воркер обработки заказов, очередь задач, resilient adapters.
4. **Week 7–8**: structured logging, tracing, audit queries, dashboards.
5. **Week 9+**: cutover client на API, постепенный deprecation локального JSON хранения.

---

## Заключение

Текущий Replica (будущий Replica) хорош как локальный/переходный инструмент, но для enterprise-scale и критичных данных требуется архитектурный pivot: **от UI-центричного file-driven монолита к транзакционному API+worker контуру с наблюдаемостью и строгими границами доверия**.


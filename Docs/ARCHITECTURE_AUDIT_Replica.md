# Архитектурный аудит Replica (текущий код Replica)

> Контекст: аудит выполнен по текущему монолитному WinForms-приложению, которое работает с файловой системой/NAS и JSON-файлами конфигурации/истории.

> Актуализация на 2026-03-19 (этап 2): live-проверка PostgreSQL выполнена, `history.json` импортирован в `replica_db` (10 orders / 11 items), marker `history_json_bootstrap_v1` записан в `storage_meta`, orphan-items не обнаружены.

## Executive summary

- Текущая реализация **не готова** к роли транзакционно-безопасной платформы на сотни пользователей.
- Главные причины: отсутствие выделенного серверного API/worker-контуров, UI-центричная оркестрация, ограниченная observability (без trace/correlation id), отсутствие централизованных authN/authZ границ и идемпотентности API.
- В коде уже закрыт значимый кусок миграции: введён `IOrdersRepository`, реализован LAN PostgreSQL backend с optimistic concurrency (`StorageVersion` + conflict guard), добавлен `order_events` и one-time bootstrap marker в `storage_meta`. Это снижает риски потери данных, но ещё не делает систему enterprise-ready.

---

## 1) Архитектурная целостность (Clean Architecture)

### Наблюдения

- Точка входа поднимает сразу WinForms (`Application.Run(new MainForm())`), без явного composition root для бизнес-слоя/инфраструктуры.
- `MainForm` агрегирует orchestration, хранение истории, статус-машину, UI-binding, файловые операции и запуск процессора; состояние формы содержит десятки полей и коллекций.
- Persistence реализован через прямое чтение/запись JSON (`history.json`) из UI-слоя.
- `ConfigService` и `AppSettings` — статические сервисы/конфиги с прямым file IO, без интерфейсов и DI.

### Вывод

- SoC нарушен: UI-layer контролирует use-case/persistence.
- Налицо «God Object» в виде `MainForm` (+ partial-файлы как физическое разделение, но не архитектурное).
- Замена persistence/API слоя потребует массового рефакторинга из-за сильной связности и отсутствия портов/адаптеров.

---

## 2) Транзакционная надежность (Transactional Integrity)

### Наблюдения

- Введён слой хранения через `IOrdersRepository` с режимами `FileSystem` и `LanPostgreSql` (feature-gate в настройках).
- В модели добавлены version-поля (`OrderData.StorageVersion`, `OrderFileItem.StorageVersion`).
- В `PostgreSqlOrdersRepository` реализован optimistic concurrency: version-check на update/delete и внешний conflict-guard перед save.
- Для первичного переноса истории добавлен one-time marker `history_json_bootstrap_v1` в `storage_meta`.
- Модель конкурентного запуска ограничивается in-memory-словарем `_runTokensByOrder` в рамках одного процесса UI.
- Межклиентская координация обработки (`run/stop`) на сервер не вынесена; coordination остаётся в клиентском процессе.
- Идемпотентности API нет, так как серверного API на текущем шаге нет.

### Вывод

- Риск `lost update` существенно снижен в LAN-режиме за счёт optimistic concurrency и запрета silent overwrite при конфликте.
- Полной транзакционной модели уровня API/command handling пока нет (нет server-side use-case boundary и idempotency keys).
- Повторные команды со стороны клиента не имеют глобальных idempotency key и дедупликации на сервере.

---

## 3) Отказоустойчивость и Resiliency

### Наблюдения

- Есть polling-ожидание файлов с timeout (`WaitForFileAsync`, `WaitForFileInAnyAsync`) и отмена по `CancellationToken`.
- Нет стратегии retry с backoff/jitter для операций copy/move/read на временно недоступном NAS.
- В нескольких местах ошибки suppress-ятся (`catch { }`), что скрывает деградации.
- Нет circuit breaker / bulkhead / load shedding.
- Архитектура single-process: падение/фриз UI-компонента критично для всего потока выполнения.

### Вывод

- Устойчивость к «дрожащей» инфраструктуре (NAS/сетевые share) ограничена timeout-паттерном, но без управляемой деградации и предохранителей.

---

## 4) Наблюдаемость (Observability & Audit)

### Наблюдения

- Логгер пишет plain-text строки в файл, без структурированных полей, trace/span/correlation id.
- Лог статусов заказа (`AppendOrderStatusLog`) текстовый, best-effort, с глушением ошибок записи.
- В PostgreSQL введён событийный журнал `order_events` (CRUD-события репозитория + `run/stop/delete/topology/add-item/remove-item/status-change` из клиентских workflow-точек).
- Распределенной трассировки нет (и отсутствует распределенная архитектура на текущем этапе).
- `order_events` хранится в БД и снижает риск mutable file-audit, но пока отсутствуют корреляция запросов, actor identity и формализованные audit-дашборды.

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
| `MainForm` orchestration | God Object, смешение UI + domain + persistence + file IO | **High** | Выделить use-case слой (`IOrderApplicationService`), UI оставить как presenter/view; внедрить DI/composition root. |
| История заказов (`history.json` / LAN PostgreSQL) | В FileSystem-режиме остаётся риск race; в LAN-режиме риск снижен через version-check | **Med** | Оставить FileSystem только как fallback; целевой режим — PostgreSQL + server-side command boundary. |
| `SetOrderStatus` + `SaveHistory` | Клиентская неатомарность между UI-операцией и persistence | **Med/High** | Перенести статусные команды в API/worker с unit of work и server-side invariants. |
| `_runTokensByOrder` (in-memory) | Контроль выполнения только в рамках процесса | **High** | Вынести coordination в backend (job table/queue), статус/lock хранить централизованно. |
| `OrderProcessor` file workflow | Нет retry/backoff на сетевые ошибки | **High** | Политики resilience (Polly): retry with jitter, timeout budget, fallback. |
| Ожидание hotfolder | Polling без circuit breaker | **Med** | Добавить circuit breaker + health state для внешних зависимостей (PitStop/Imposing/NAS). |
| Логирование (`Logger`) | Неструктурированные логи без correlation | **High** | Перейти на structured logging (Serilog + sink), обязательные поля: `order_id`, `actor`, `operation`, `trace_id`. |
| Order status log file | best-effort append, mutable file (частично компенсировано `order_events`) | **Med** | Сделать `order_events` primary audit source, добавить retention/архив и SQL-аудит отчёты. |
| Ошибки с `catch { }` | Потеря диагностических сигналов | **Med** | Запретить silent catch без метрик/логов; ввести error budget и алерты. |
| ConfigService/AppSettings static IO | Сильная связность с файловой системой, сложная тестируемость | **Med** | Абстрагировать через `IConfigRepository`, `ISettingsProvider`, внедрить mockable adapters. |
| Отсутствие API идемпотентности | Дубли заказов при повторной отправке | **High** | В API-командах использовать `Idempotency-Key` + таблицу дедупликации. |
| Отсутствие authN/authZ границ | Невозможно enforce policy на сервере | **High** | Ввести API gateway + JWT/SSO + role/claim-based authorization. |
| Валидация входных данных | Локальная и фрагментарная | **Med** | Централизовать validation на command DTO/domain rules, добавить schema/contract validation. |
| Хардкод дефолтных путей NAS | Сложность portability/segmentation | **Low/Med** | Вынести в environment-specific config profiles, добавить проверку доступности на старте. |
| Отсутствие распределенной трассировки | Невозможно проследить end-to-end через сервисы | **Med (сейчас), High (после разделения)** | Внедрить OpenTelemetry tracing заранее в новом API/worker-контуре. |

---

## Технический долг: что «сжечь и переписать» сейчас

### P0 (делать немедленно)

1. **Persistence-модель на JSON в UI** → заменить на backend + PostgreSQL.
2. **Статусные переходы и аудит «мимо транзакций»** → сделать transactional command handling.
3. **God Object (MainForm как бизнес-оркестратор)** → выделить application layer и инфраструктурные адаптеры.
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

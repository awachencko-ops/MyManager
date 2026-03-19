# Этап 2: group-order логика и PostgreSQL/LAN план

Дата актуализации: 2026-03-19
Статус: In progress (`E2-P1`, `E2-P2`, `E2-P3`, `E2-P4`, `E2-P5` закрыты)

## 1. Входные условия (подтверждено)

1. Миграция на `MainForm` закрыта (`build 0/0`, baseline tests 30/30 PASS).
2. Таблица single/group стабилизирована (`SR-13...SR-27`).
3. Hash-слой (`Source/Prepared/Print`) добавлен в модель заказа и item.
4. Риск `R8` (LAN feature-gate) перенесён из этапа 1 в этот этап и этап 3.

## 2. Цель этапа

Перевести хранение заказов из файлового режима в LAN-контур с PostgreSQL, сохранив текущую бизнес-модель:
1. `single-order` = контейнер с 1 item.
2. `group-order` = контейнер с 2+ items.
3. Клиент (`MainForm`) работает через серверный контракт, не теряя offline-предсказуемость.

## 3. Целевая модель данных

### 3.1 Контейнер `orders`

Ключевые поля:
1. `order_id` (UUID, PK)
2. `order_number` (text, not null)
3. `manager_order_date` (date, not null)
4. `arrival_date` (timestamp)
5. `created_by_user` (text)
6. `aggregate_status` (text)
7. `items_count` (int)
8. `version` (bigint, concurrency token)
9. `created_at`, `updated_at` (timestamp)

### 3.2 Элементы `order_items`

Ключевые поля:
1. `item_id` (UUID, PK)
2. `order_id` (UUID, FK -> `orders.order_id`)
3. `sequence_no` (int, `check(sequence_no > 0)`)
4. `client_file_label`, `variant`
5. `source_path`, `prepared_path`, `print_path`
6. `source_file_size_bytes`, `prepared_file_size_bytes`, `print_file_size_bytes`
7. `source_file_hash`, `prepared_file_hash`, `print_file_hash`
8. `item_status`, `last_reason`, `updated_at`, `version`
9. `pitstop_action`, `imposing_action`

### 3.3 События `order_events`

1. `event_id` (UUID, PK)
2. `order_id`, `item_id` (nullable для container-level событий)
3. `event_type` (`run/stop/delete/add-item/remove-item/topology/status-change`)
4. `event_source` (`processor/ui/file-sync/api`)
5. `payload_json`, `created_at`, `created_by`

### 3.4 Пользователи `users`

1. `user_id` (UUID, PK)
2. `user_name` (unique)
3. `is_active`
4. `updated_at`

## 4. PostgreSQL + DBeaver: практический старт

1. Развернуть PostgreSQL на LAN-сервере (рекомендуемо 16+).
2. Создать БД и роли:
   - runtime-role (приложение)
   - admin-role (миграции/сопровождение)
3. Подключить DBeaver к серверу и зафиксировать рабочие подключения команды.
4. В DBeaver выполнить начальный SQL-драфт (`orders`, `order_items`, `order_events`, `users`, `storage_meta`, индексы, FK, unique/check).
5. Включить регулярный backup БД (ежедневно) и тест восстановления.

### 4.1 Протокол верификации в DBeaver (E2-P4)

1. Проверить наличие bootstrap-marker:
   - `select meta_key, meta_value, updated_at from storage_meta where meta_key = 'history_json_bootstrap_v1';`
2. Проверить факт переноса контейнеров и item:
   - `select count(*) as orders_count from orders;`
   - `select count(*) as items_count from order_items;`
3. Проверить связность item -> order:
   - `select count(*) as orphan_items from order_items i left join orders o on o.internal_id = i.order_internal_id where o.internal_id is null;`
4. Проверить событийный журнал после bootstrap и smoke-прогона:
   - `select event_type, event_source, count(*) from order_events group by event_type, event_source order by event_type, event_source;`
5. Критерий успеха E2-P4:
   - marker существует (`history_json_bootstrap_v1`);
   - `orders_count`/`items_count` не нулевые для непустой истории;
   - `orphan_items = 0`;
   - в `order_events` есть bootstrap-события `add-order`/`add-item`.

### 4.2 Live-результат верификации (2026-03-19)

1. Подключение: `Host=localhost;Port=5432;Database=replica_db;User ID=postgres`.
2. Фактические значения:
   - `orders_count = 10`
   - `items_count = 11`
   - `orphan_items = 0`
   - marker `history_json_bootstrap_v1` присутствует (`state=imported`, `imported_orders=10`)
3. События bootstrap:
   - `add-order | ui | 10`
   - `add-item | ui | 11`

## 5. План реализации в коде (этап 2)

| ID | Шаг | Результат | Статус |
|---|---|---|---|
| E2-P1 | Ввести data-access abstraction (`IOrdersRepository`) в клиенте | `MainForm` работает через абстракцию источника данных | Completed |
| E2-P2 | Реализовать PostgreSQL repository + optimistic concurrency | CRUD заказов и item с `version`/conflict handling | Completed |
| E2-P3 | Перенести `order_events` логирование в серверный слой | Трассируемость операций без потери текущей семантики | Completed |
| E2-P4 | Миграция данных из `history.json` в PostgreSQL | Исторические заказы и item перенесены с hash-полями | Completed |
| E2-P5 | Добавить LAN feature-gate в настройки клиента | Явный режим `FileSystem` / `LanPostgreSql` + fallback-поведение | Completed |
| E2-P6 | Интеграционный regression pack (client + DB) | Автопроверка ключевых single/group сценариев на серверном хранилище | In progress |

Примечание по `E2-P1` (выполнено 2026-03-19):
1. Добавлены `OrdersStorageMode` и настройки backend/connection string в `AppSettings`.
2. Введены `IOrdersRepository`, `FileSystemOrdersRepository`, `PostgreSqlOrdersRepository`, `OrdersRepositoryFactory`.
3. `MainForm.LoadHistory/SaveHistory` переключены на repository + fallback в файловое хранилище при недоступности LAN/PostgreSQL.
4. Добавлен пакет `Npgsql`.
5. В `SettingsDialogForm` добавлены UI-поля feature-gate (`FileSystem` / `LAN PostgreSQL`) и connection string.

Примечание по `E2-P2` (закрыто 2026-03-19):
1. `PostgreSqlOrdersRepository` переведен на нормализованные таблицы (`orders`, `order_items`, `order_events`, `users`) вместо единой snapshot-таблицы.
2. Включено чтение/запись заказов и item в PostgreSQL в рамках текущего `SaveHistory/LoadHistory` контура.
3. Добавлены `StorageVersion` поля в модель (`OrderData`, `OrderFileItem`) и проверка optimistic concurrency перед записью.
4. Реализован conflict-guard: при изменениях в БД другим клиентом возвращается `concurrency conflict`, без silent overwrite.
5. Добавлены version-check update/delete операции для container/item.
6. Для `concurrency conflict` отключён silent fallback в `history.json` (чтобы не маскировать LAN-расхождения).

Примечание по `E2-P3` (закрыто 2026-03-19):
1. Репозиторий пишет серверные события `add/update/delete-order` и `add/update/remove-item` в `order_events`.
2. Добавлен контракт `IOrdersRepository.TryAppendEvent(...)` для прямой серверной записи событий из клиентских workflow-точек.
3. `MainForm` отправляет в `order_events` события `run/stop/delete/topology/add-item/remove-item` через `AppendOrderOperationLog`.
4. `MainForm` отправляет в `order_events` события `status-change` из `SetOrderStatus` с `source=ui/processor/file-sync`.
5. `event_source` сохраняется по источнику статуса (`ui`, `processor`, `file-sync`), payload хранится в `jsonb`.

Примечание по `E2-P4` (закрыто 2026-03-19):
1. Добавлен bootstrap-переезд: при пустой PostgreSQL истории выполняется загрузка из `history.json` и первичная запись в БД.
2. Добавлена one-time marker-логика: `history_json_bootstrap_v1` в `storage_meta` (state=`imported`/`empty-source`).
3. При наличии marker bootstrap из `history.json` повторно не запускается.
4. Протокол SQL-верификации в DBeaver формализован (раздел `4.1`).
5. Выполнена live-проверка (локальный PostgreSQL `localhost:5432`, БД `replica_db`): `orders=10`, `order_items=11`, `orphan_items=0`, marker записан (`state=imported`).
6. В `order_events` после bootstrap: `add-order|ui=10`, `add-item|ui=11` (всего `21` событий).

Примечание по `E2-P6` (прогресс 2026-03-19):
1. Добавлены verify-тесты repository-слоя (factory + filesystem roundtrip + connection-string guards).
2. Добавлены тесты на event-контракт (`TryAppendEvent`) для filesystem no-op и PostgreSQL guard по пустой connection string.
3. Добавлены тесты на meta-контракт (`TryGetMetaValue`/`TryUpsertMetaValue`) для PostgreSQL guard по пустой connection string.
4. Текущее состояние test-pack: `dotnet test Replica.sln` -> `39/39 PASS`:
   - `tests/Replica.VerifyTests`: `14/14 PASS`
   - `tests/Replica.UiSmokeTests`: `25/25 PASS`

## 6. Риски и контрмеры

1. Путаница дат (`manager_order_date` vs `arrival_date`) -> строгие имена и валидация в API/DB.
2. Конфликт order-level и item-level путей -> источником истины остаётся item-level.
3. Параллельное редактирование -> `version` + optimistic concurrency + `409 Conflict`.
4. Потеря порядка item -> `sequence_no` + `unique(order_id, sequence_no)`.
5. Потеря дедупликации файлов -> хранить/проверять file hash на стороне API/DB.

## 7. Definition of Done этапа 2

Этап 2 считается закрытым, когда одновременно выполнено:
1. PostgreSQL схема развернута и проверена через DBeaver.
2. Клиент читает/пишет `orders` и `order_items` через LAN-контур.
3. `order_events` фиксирует container/item операции.
4. Миграция данных из файловой истории выполнена и верифицирована.
5. Автотесты этапа 2 проходят без `P0/P1` дефектов.

## 8. Связь с этапами

- Вход: `Docs/ready/1_MAINFORM_MIGRATION_COMPLEX_RESEARCH_AND_PLAN.md`
- Выход: `3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`

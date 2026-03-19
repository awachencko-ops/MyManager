# Этап 2: group-order логика и PostgreSQL/LAN план

Дата актуализации: 2026-03-19
Статус: Ready to start (этап 1 закрыт)

## 1. Входные условия (подтверждено)

1. Миграция на `MainForm` закрыта (`build 0/0`, `tests 30/30 PASS`).
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
4. В DBeaver выполнить начальный SQL-драфт (`orders`, `order_items`, `order_events`, `users`, индексы, FK, unique/check).
5. Включить регулярный backup БД (ежедневно) и тест восстановления.

## 5. План реализации в коде (этап 2)

| ID | Шаг | Результат | Статус |
|---|---|---|---|
| E2-P1 | Ввести data-access abstraction (`IOrdersRepository`) в клиенте | `MainForm` работает через абстракцию источника данных | Planned |
| E2-P2 | Реализовать PostgreSQL repository + optimistic concurrency | CRUD заказов и item с `version`/conflict handling | Planned |
| E2-P3 | Перенести `order_events` логирование в серверный слой | Трассируемость операций без потери текущей семантики | Planned |
| E2-P4 | Миграция данных из `history.json` в PostgreSQL | Исторические заказы и item перенесены с hash-полями | Planned |
| E2-P5 | Добавить LAN feature-gate в настройки клиента | Явный режим `FileSystem` / `LanPostgreSql` + fallback-поведение | Planned |
| E2-P6 | Интеграционный regression pack (client + DB) | Автопроверка ключевых single/group сценариев на серверном хранилище | Planned |

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

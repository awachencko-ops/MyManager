# MyManager: мульти-заказы — анализ рисков, проектирование и план внедрения (с адаптацией под PostgreSQL)

Дата актуализации: 2026-03-13
Статус: проектный документ (без изменений в коде)

## 1. Контекст и цель

Этот документ фиксирует целевую бизнес-логику **контейнера заказа** и **вложенных файлов (items)**, а также план перехода к модели данных, готовой к PostgreSQL и работе по локальной сети.

Ключевая идея:
1. **Заказ = контейнер**.
2. Контейнер хранит только ключевые атрибуты менеджера:
   - номер заказа;
   - дата просчёта/дата заказа менеджера.
3. **Файлы внутри заказа = items**:
   - именно item содержит операционные поля (этапы source/prepared/print, статусы, действия, служебные метаданные и т.д.);
   - момент поступления файла в препресс фиксируется на item-уровне.

## 2. Что уже соответствует этой модели в текущем проекте

По коду уже есть базовые элементы, на которые можно опереться:
1. В `OrderData` есть контейнер `Items`, признак топологии (`SingleOrder`/`MultiOrder`) и разделение `ArrivalDate` / `OrderDate`.
2. В `OrderFileItem` есть item-идентификатор, стадийные пути, статус, операции и FIFO-последовательность (`SequenceNo`).

Следовательно, задача не «с нуля», а **дожать и стандартизировать** уже начатую модель, снять неоднозначности и закрепить контракт под PostgreSQL.

## 3. Предлагаемая доменная модель (строго)

### 3.1 Сущность `Order` (контейнер)

Минимальный обязательный набор бизнес-полей:
1. `OrderNumber` — номер заказа (человекочитаемый, не равен внутреннему GUID).
2. `ManagerOrderDate` — дата заказа/просчёта, которую заполняет менеджер.

Служебный набор:
1. `OrderId` (UUID) — технический PK.
2. `CreatedAt`, `UpdatedAt`.
3. `CreatedByUser`, `UpdatedByUser` (опционально, но желательно для LAN-аудита).
4. Агрегированные поля (computed/materialized):
   - `ItemsCount`;
   - `AggregateStatus`.

### 3.2 Сущность `OrderItem` (файл внутри заказа)

Обязательные поля item:
1. `ItemId` (UUID).
2. `OrderId` (FK -> Order).
3. `PrepressArrivalAt` — дата поступления конкретного файла в препресс.
4. `SequenceNo` — порядок поступления в рамках заказа.
5. Стадии и пути: `SourcePath`, `PreparedPath`, `PrintPath`.
6. `ItemStatus`, `LastReason`, `UpdatedAt`.
7. Локальные операции: `PitStopAction`, `ImposingAction`.

Важно: если в заказе один item — это всё равно контейнер + item; single-режим является частным случаем, а не другой сущностью.

### 3.3 Single/Multi-маркировка

Варианты:
1. **Явная метка** (`topology_marker` = single/multi).
2. **Вычисляемая метка** (`items_count == 1 -> single`, `>1 -> multi`).

Рекомендация:
- в БД хранить вычисляемо (через `COUNT`/materialized поле),
- в UI показывать человекочитаемую форму «Одиночный / Мульти» при необходимости,
- не делать метку обязательной ручной логикой, чтобы не ловить рассинхрон.

## 4. Ключевые риски и контрмеры

### R1. Расхождение дат контейнера и item
Риск: пользователи путают «дата заказа менеджера» и «дата поступления в препресс».
Контрмеры:
1. Жёсткие названия полей в UI и API (без двусмысленных `Date`).
2. Разные колонки/подсказки в форме.
3. Валидация на уровне DTO + БД constraints.

### R2. Дублирование legacy-полей путей на order-уровне
Риск: order-level `Source/Prepared/Print` расходится с item-level путями.
Контрмеры:
1. Признать item-level пути единственным источником истины.
2. Order-level пути оставить только как backward-compat адаптер на переходный период.
3. Добавить миграционный флаг этапа (`legacy_path_mode`) и срок удаления.

### R3. Конкурентная работа в LAN
Риск: два оператора параллельно редактируют один заказ.
Контрмеры:
1. `row_version`/optimistic concurrency (xmin или отдельный bigint version).
2. Идемпотентные API-команды.
3. Аудит событий (кто/когда изменил).

### R4. Нестабильная сортировка item в мульти-заказе
Риск: порядок файлов «скачет» между клиентами.
Контрмеры:
1. Жёсткий `SequenceNo` + уникальность (`OrderId`, `SequenceNo`).
2. Явные правила вставки (append, insert-before, resequence).

### R5. Неполная готовность UI к item-level операциям
Риск: backend поддерживает multi, UI «single-only» в критичных местах.
Контрмеры:
1. Вынести действия в матрицу разрешений: order-level vs item-level.
2. Планировать toolbar-кнопку «Добавить файл в заказ» как штатную item-команду.
3. Проверить контекстное меню/drag&drop/clipboard для multi-сценариев.

### R6. Риски миграции с файлового/JSON-слоя в PostgreSQL
Риск: потеря связей order-item, дубль item, некорректные даты.
Контрмеры:
1. Dry-run миграция на копии данных.
2. Checksums/сверка количества заказов/items до и после.
3. Скрипт обратного отката и журнал ошибок мигратора.

## 5. PostgreSQL: целевой контракт (черновой)

## 5.1 Таблицы

1. `orders`
   - `order_id uuid pk`
   - `order_number text not null`
   - `manager_order_date date not null`
   - `created_at timestamptz not null`
   - `updated_at timestamptz not null`
   - `aggregate_status text not null`
   - `items_count int not null default 0`
   - `version bigint not null default 0`

2. `order_items`
   - `item_id uuid pk`
   - `order_id uuid not null references orders(order_id) on delete cascade`
   - `prepress_arrival_at timestamptz not null`
   - `sequence_no bigint not null`
   - `source_path text not null default ''`
   - `prepared_path text not null default ''`
   - `print_path text not null default ''`
   - `item_status text not null`
   - `last_reason text not null default ''`
   - `pitstop_action text not null default '-'`
   - `imposing_action text not null default '-'`
   - `updated_at timestamptz not null`
   - `version bigint not null default 0`

3. `order_events` (аудит/история, желательно сразу)
   - `event_id uuid pk`
   - `order_id uuid not null`
   - `item_id uuid null`
   - `event_type text not null`
   - `payload jsonb not null`
   - `actor text not null`
   - `created_at timestamptz not null`

## 5.2 Индексы и ограничения

1. `unique(order_number, manager_order_date)` — обсуждаемо (зависит от бизнес-правил повторов).
2. `unique(order_id, sequence_no)` для стабильного порядка item.
3. Индексы на `order_items(order_id)`, `orders(updated_at)`, `orders(manager_order_date)`.
4. `check(sequence_no > 0)`.
5. Опционально check-constraints на статусы (или enum-таблица).

## 5.3 API (минимальный набор команд)

1. `POST /orders` — создать контейнер заказа.
2. `PATCH /orders/{id}` — обновить номер/дату просчёта.
3. `POST /orders/{id}/items` — добавить файл в заказ.
4. `PATCH /orders/{id}/items/{itemId}` — изменить item (пути/статус/операции).
5. `POST /orders/{id}/items/reorder` — изменить порядок.
6. `GET /orders` + фильтры + пагинация.
7. `GET /orders/{id}` — карточка контейнера с items.

Принцип: все изменения проходят через командные endpoints, чтобы централизовать валидацию и аудит.

## 6. План работ (без кодинга, аналитический roadmap)

### Этап A — Уточнение терминов и контрактов (1–2 дня)
1. Зафиксировать словарь терминов в проекте:
   - «Дата заказа менеджера (просчёта)»;
   - «Дата поступления файла в препресс».
2. Утвердить финальные названия полей UI/API/DB.
3. Подтвердить логику single/multi как вычисляемую от количества items.

**Результат:** утверждённый словарь и ER-схема v1.

### Этап B — Проектирование миграции в PostgreSQL (2–4 дня)
1. Описать mapping текущих сущностей в SQL-таблицы.
2. Подготовить правила трансформации legacy order-level путей.
3. Спроектировать backfill `SequenceNo`, если его нет/битый.
4. Спроектировать dual-write/compatibility режим (если нужен мягкий переход).

**Результат:** миграционный design-doc + SQL draft + план отката.

### Этап C — API-дизайн под LAN-работу (2–3 дня)
1. Контракты DTO (CreateOrder, AddItem, UpdateItem...).
2. Concurrency-стратегия (`version` + 409 Conflict).
3. Политика идемпотентности для повторных запросов.
4. Модель аудита (`order_events`).

**Результат:** OpenAPI-черновик и матрица ошибок.

### Этап D — UI/UX-изменения (план, без реализации) (1–2 дня)
1. Кнопка в toolbar: «Добавить файл в заказ».
2. Явное разделение order-полей vs item-полей в карточке.
3. Режимы отображения списка:
   - compact (order-only);
   - expanded (order + items).
4. Поведение контекстного меню в multi.

**Результат:** UX-спека и сценарии ручной проверки.

### Этап E — Тестовая стратегия (1–2 дня)
1. Инварианты домена:
   - заказ не существует без контейнера;
   - item всегда принадлежит заказу;
   - single/multi определяется count(items).
2. Набор критичных интеграционных сценариев:
   - add item;
   - reorder;
   - concurrent update;
   - migration parity.

**Результат:** тест-план + checklist для регрессии.

## 7. Принятые проектные решения на текущий момент

1. Заказ рассматривается как контейнер верхнего уровня.
2. Номер и дата просчёта — атрибуты контейнера.
3. Дата поступления в препресс фиксируется на item-уровне.
4. Single/multi — не отдельные сущности, а режимы одной модели.
5. PostgreSQL-переход проектируется с приоритетом на консистентность, конкурентную безопасность и аудит.

## 8. Definition of Ready перед началом кодинга мульти-заказов

Перед реализацией должны быть готовы:
1. Словарь терминов и имен полей (UI/API/DB).
2. ER-схема и SQL draft.
3. Контракты API (минимум CRUD+command на items).
4. План миграции и отката.
5. Чеклист критичных сценариев multi-order.

---

Статус документа: рабочий аналитический план следующего этапа (multi-order + PostgreSQL).

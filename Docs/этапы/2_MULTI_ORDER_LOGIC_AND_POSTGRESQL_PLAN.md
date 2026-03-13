# Этап 2: group-order логика и PostgreSQL план

Дата актуализации: 2026-03-13
Статус: рабочий план этапа 2

## 1. Цель этапа

После стабилизации `MainForm` (этап 1) перевести модель заказов в единый формат:
1. `single-order` = контейнер с 1 item.
2. `group-order` = контейнер с 2+ items.

И подготовить эту модель к серверному хранению в PostgreSQL без рассинхрона данных.

## 2. Бизнес-модель (зафиксировано)

### 2.1 Контейнер `Order`

Обязательные поля:
1. `OrderNumber`.
2. `ManagerOrderDate` (дата оформления/просчёта менеджером).

Служебные поля:
1. `OrderId` (UUID).
2. `CreatedAt`, `UpdatedAt`.
3. `CreatedById`, `CreatedByUser`.
4. `ItemsCount`, `AggregateStatus`, `Version`.

### 2.2 Файл `OrderItem`

1. `ItemId` (UUID), `OrderId` (FK).
2. `PrepressArrivalAt` (дата поступления в препресс).
3. `SequenceNo` (порядок внутри контейнера).
4. `SourcePath`, `PreparedPath`, `PrintPath`.
5. `ItemStatus`, `LastReason`, `UpdatedAt`, `Version`.
6. `PitStopAction`, `ImposingAction`.

## 3. UI-правила для group-order

1. Обязательная метка типа заказа: `single-order` / `group-order`.
2. Для `group-order` — другой цвет плашки строки.
3. ЛКМ по контейнеру: expand/collapse items.
4. В контейнере показываем: номер + дата менеджера.
5. В item-строке показываем операционные данные файла.
6. На toolbar должна быть команда «Добавить файл в заказ».

## 4. PostgreSQL: минимальный контракт

## 4.1 Таблицы

1. `orders`
2. `order_items`
3. `order_events`
4. `users`

## 4.2 Ограничения

1. `unique(order_id, sequence_no)`.
2. `check(sequence_no > 0)`.
3. Индекс по автору (`CreatedById`/`CreatedByUser`).
4. `Version` как concurrency token.

## 5. Риски и контрмеры

1. Путаница дат (`ManagerOrderDate` vs `PrepressArrivalAt`) -> жёсткие названия и UI-подсказки.
2. Расхождение order-level и item-level путей -> item-level источник истины.
3. Параллельное редактирование -> optimistic concurrency + `409 Conflict`.
4. Потеря порядка items -> `SequenceNo` + уникальность.
5. Потеря трассировки изменений -> обязательный `order_events`.

## 6. Definition of Done этапа 2

1. Утверждён словарь полей контейнера и item.
2. Утверждён SQL draft (`orders/order_items/order_events/users`).
3. Зафиксированы UI-правила `group-order`.
4. Подготовлены migration/checklist критерии перед переходом к этапу 3.

---

Связь с этапами:
- Вход: `1_MAINFORM_MIGRATION_COMPLEX_RESEARCH_AND_PLAN.md`
- Выход: `3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`

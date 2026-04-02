<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Структура таблицы заказов: as-is и target для OLV/Avalonia (2026-04-02)

## 1. As-is структура (текущий production)
1. Центральный list-контур: `DataGridView dgvJobs` внутри `pnlTable` (`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs:324`).
2. Колонки в таблице фиксированы (`colStatus`..`colCreated`) (`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs:334-394`).
3. Идентификация строк через `Tag` и ручное разруливание `order/item` (`Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:932`, `:985`).
4. Group-order разворот/сворачивание реализован вручную (`ToggleOrderExpanded`, `TryToggleOrderExpandedRowsInPlace`) (`Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:1002-1098`).
5. Есть параллельный tiles-режим, который связан с list-режимом (`Features/Orders/UI/OrdersWorkspace/Views/OrdersWorkspaceForm.PrintTiles.cs`).

## 2. OLV target-структура (рекомендуемый production путь)
### 2.1 UI слой
1. `OrdersWorkspaceForm` остается основным shell (минимальный организационный риск).
2. В `pnlTable` меняется только реализация list-контрола (`DataGridView` -> `TreeListView`/`FastObjectListView`).

### 2.2 Presentation слой
1. Ввести единый `OrdersGridRowModel` с явными полями:
- `RowType` (`Order`/`Item`),
- `OrderInternalId`, `ItemId`,
- значения всех колонок,
- флаги визуальных состояний.
2. Ввести `OrdersGridBuilder`, который формирует коллекцию row-model из `_orderHistory`.
3. Ввести `IOrdersGridAdapter` (две реализации):
- `DataGridViewOrdersAdapter` для fallback,
- `OlvOrdersAdapter` для нового контура.

### 2.3 Interaction слой
1. Все stage-операции вынести из обработчиков контрола в сервис (например, `OrdersGridInteractionService`).
2. Контрол только маршрутизирует событие в сервис и обновляет selection.
3. Фильтры/сортировка считаются на модели, не на UI-строках.

## 3. Avalonia target-структура (R&D/долгосрочно)
### 3.1 Текущее состояние
1. Отдельный проект-прототип: `Prototypes/AvaloniaOrdersPrototype`.
2. Структура:
- `MainWindow.axaml` + `TreeDataGrid` (`Prototypes/AvaloniaOrdersPrototype/MainWindow.axaml:45`),
- `HierarchicalTreeDataGridSource<OrderNode>` (`.../MainWindow.axaml.cs:24`, `:38`),
- `OrderNode` + demo-factory (`.../OrderNode.cs:10`, `:96`).
3. Theme include обязателен (`.../App.axaml:9`).

### 3.2 Если идти в production на Avalonia
1. Нужен отдельный frontend-модуль (не встраивать в текущую WinForms форму).
2. Нужен контракт обмена состоянием list/tiles/actions с текущим application-слоем.
3. Нужен отдельный тестовый UI-harness (текущие smoke-тесты привязаны к WinForms).

## 4. Сравнение структурного воздействия
| Область | OLV в WinForms | Avalonia |
|---|---|---|
| UI shell | Сохраняется | Меняется |
| Слой событий | Рефакторинг локально | Переписывается |
| Тестовый контур | Эволюционный переход | Новый контур |
| Риск для текущего релиза | Ниже | Выше |

## 5. Рекомендуемая целевая структура проекта
1. Краткосрок: `OLV + adapter + row-model` в текущем WinForms.
2. Среднесрок: убрать прямую бизнес-логику из событий контрола.
3. Долгосрок: Avalonia рассматривать как полноценный UI-модуль после стабилизации adapter-границы.

## 6. Практический вывод
Лучший структурный компромисс на сегодня: сделать migration-направление через OLV, но проектировать слой адаптера так, чтобы потом без повторного «большого взрыва» можно было подключить Avalonia-клиент.

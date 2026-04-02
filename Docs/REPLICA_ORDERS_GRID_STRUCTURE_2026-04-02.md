<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Структура таблицы заказов для OLV migration (2026-04-02)

## 1. As-is структура
1. `OrdersWorkspaceForm` держит `dgvJobs` и связанную логику.
2. В таблице 9 колонок (`colStatus`..`colCreated`).
3. Строка идентифицируется `Tag`-строкой (`order|...` / `item|...`).
4. Group/item дерево эмулируется ручными insert/remove.

## 2. Target структура на OLV
### 2.1 UI слой
1. `OrdersWorkspaceForm` остается оболочкой.
2. В `pnlTable` добавляется OLV-контур (`TreeListView`) под флагом.
3. Старый `dgvJobs` временно сохраняется как fallback.

### 2.2 Presentation слой
1. `OrdersGridRowModel`:
- `RowType` (`Order`/`Item`),
- `OrderInternalId`, `ItemId`,
- значения 9 колонок,
- визуальные флаги.
2. `OrdersGridBuilder`: строит иерархию row-model из `_orderHistory`.
3. `OrdersGridFilterService`: фильтры/queue/status/user/date по модели.
4. `OrdersGridSortService`: централизованная сортировка.

### 2.3 Adapter слой
1. `IOrdersGridAdapter` (контракт поведения таблицы).
2. `DataGridViewOrdersAdapter` (legacy).
3. `OlvOrdersAdapter` (новый).

### 2.4 Interaction слой
1. `OrdersGridInteractionService`:
- stage clicks,
- double-click actions,
- context actions,
- drag-drop routing.
2. Контролы только подают `GridCellContext` в сервис.

## 3. OLV prototype как заготовка переноса
1. Прототип уже строит `TreeListView` с нужными колонками.
2. В прототипе есть:
- expand/collapse,
- сортировка с индикатором,
- быстрый фильтр по строкам,
- summary counts,
- статусные иконки.
3. Файл: `Features/Orders/UI/OrdersWorkspace/Prototypes/OrdersTreePrototypeForm.cs`.

## 4. Принцип интеграции без простоя
1. Production остается на `dgvJobs`.
2. OLV развивается параллельно под флагом.
3. Переключение делается конфигом, без удаления legacy до стабилизации.

## 5. Структурный вывод
Миграция должна быть "через модель и адаптер", а не через прямой перенос event-handler'ов между контролами. Это единственный надежный путь без деградации качества.

<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Привязка функций таблицы: DataGridView as-is -> OLV target (2026-04-02)

## 1. As-is события DataGridView
Источник: `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs`.
1. `RowsAdded/RowsRemoved/DataBindingComplete` -> derived refresh.
2. `CellPainting/CellFormatting` -> кастом визуал.
3. `CellClick/CellDoubleClick` -> stage logic + expand/collapse + edit.
4. `CellToolTipTextNeeded` -> статусные tooltip.
5. `DragEnter/DragOver/DragDrop` -> file drop сценарии.
6. `SelectionChanged/CurrentCellChanged` -> sync list/tiles/buttons/tray.

## 2. OLV соответствия
Источник API: `ObjectListView2019Core3.xml` в nuget пакете.

| As-is поведение | OLV привязка |
|---|---|
| Клик по ячейке | `ObjectListView.CellClick` |
| Правый клик | `ObjectListView.CellRightClick` / `ColumnRightClick` |
| Tooltip | `ObjectListView.CellToolTipShowing` |
| Кастом row/cell стиль | `ObjectListView.FormatRow` / `FormatCell` |
| Drag-drop | `CanDrop` + `Dropped` / `ModelCanDrop` + `ModelDropped` |
| Сортировка | `Sort(...)` + `ShowSortIndicator(s)` |
| Group/item иерархия | `TreeListView.CanExpandGetter` + `ChildrenGetter` |

## 3. Что переносим в сервисы до смены контрола
1. Resolve контекста ячейки (`order/item/stage`).
2. Stage file actions (open/pick/copy/rename/remove).
3. Валидации и статусные переходы.
4. Selection sync list <-> tiles.

## 4. Минимальный контракт адаптера
1. `BindTree(IReadOnlyList<OrdersGridRowModel> roots)`
2. `SetFilter(GridFilterState filter)`
3. `SetSort(GridSortState sort)`
4. `RestoreSelection(RowKey key)`
5. `GetCurrentContext()`
6. `event CellActionRequested`
7. `event ContextActionRequested`
8. `event FilesDropped`

## 5. Вывод
Переход на OLV должен быть "сначала перенос функции в adapter/service", потом "переключение UI-контрола". Это удержит стабильность и ускорит rollout.

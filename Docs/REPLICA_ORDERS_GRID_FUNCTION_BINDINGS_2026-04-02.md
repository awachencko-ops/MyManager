<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Привязка функций таблицы: текущий контур, OLV target, Avalonia target (2026-04-02)

## 1. Текущие привязки (DataGridView)
### 1.1 Базовые события и refresh
Источник: `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:862-866`, `:1072-1086`.
1. `RowsAdded/RowsRemoved/DataBindingComplete` -> `HandleOrdersGridChanged()`.
2. `CellValueChanged` -> derived-state refresh.
3. `SelectionChanged/CurrentCellChanged` -> синхронизация selection/buttons/tray.

### 1.2 Интеракции и рендеринг
Источник: `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:1054-1066`, `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.GridInteractions.cs:261`, `:315`, `:642`, `:897`.
1. `CellPainting` -> кастом статуса и декора.
2. `CellFormatting` -> цвета, file-link поведение, missing-file визуал.
3. `CellClick` -> group expand/collapse + stage actions.
4. `DragEnter/DragOver/DragDrop` -> file import into stages.

### 1.3 Контекстное меню
Источник: `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.ContextMenu.cs:102`.
1. `CellMouseDown` правой кнопкой -> выбор контекста и запуск меню команд.

## 2. OLV target: функциональная карта соответствия
Источник по API OLV: `C:/Users/user/.nuget/packages/objectlistview.repack.core3/2.9.3/lib/net5.0-windows7.0/ObjectListView2019Core3.xml`.

| Текущая задача | OLV событие/механика | Комментарий |
|---|---|---|
| Клик по ячейке | `ObjectListView.CellClick` | Прямой перенос `stage` маршрутизации |
| Правый клик | `ObjectListView.CellRightClick`/`ColumnRightClick` | Контекстное меню переносится без потери UX |
| Tooltip | `ObjectListView.CellToolTipShowing` | Аналог `CellToolTipTextNeeded` |
| Кастом стиль строк/ячеек | `ObjectListView.FormatRow`/`FormatCell` | Перенос палитры и статусного визуала |
| Drag-drop | `CanDrop`/`Dropped` и/или `ModelCanDrop`/`ModelDropped` | Можно сделать безопаснее на model-id |
| Сортировка + индикатор | `Sort(...)`, `ShowSortIndicators` | Встроенная поддержка toggles |
| Иерархия group/item | `TreeListView.CanExpandGetter` + `ChildrenGetter` | Нативный tree-контур |

## 3. Avalonia target: функциональная карта соответствия
Источники: `Prototypes/AvaloniaOrdersPrototype/MainWindow.axaml.cs:24`, `:38`, `:41`, `:102`, `:132`; `.../App.axaml:9`.

| Текущая задача | Avalonia механизм | Комментарий |
|---|---|---|
| Иерархия group/item | `HierarchicalTreeDataGridSource<T>` + `HierarchicalExpanderColumn<T>` | Чистая tree-модель |
| Сортировка | сортировка колонок `TreeDataGrid` | Нативно, без ручного `DisableSorting` |
| Выделение | `RowSelection.SelectionChanged` | Selection-логику нужно маппить в текущий workflow |
| Кнопки действий | `Click` handlers / Commands | Уже в прототипе `Expand/Collapse/Add` |
| Рендеринг | стили Avalonia + templates | Визуал придется собирать заново |
| Theme-зависимость | `StyleInclude ... TreeDataGrid ...` | Без этого таблица не рисуется |

## 4. Что обязательно отвязать до миграции (для обоих путей)
1. Stage-операции от прямой зависимости на конкретный контрол.
2. Идентификацию строк через `Tag` на явный row-model.
3. Фильтрацию и counters от обхода UI-строк.
4. Тесты от приватных полей `dgvJobs`.

## 5. Рекомендуемый контракт привязок
Ввести единый функциональный интерфейс, например:
1. `BindRows(IEnumerable<OrdersGridRowModel>)`
2. `RestoreSelection(RowKey key)`
3. `TryGetCurrentCellContext(out GridCellContext)`
4. `SetSort(GridSortSpec spec)`
5. `SetFilter(GridFilterSpec spec)`
6. `event CellActionRequested`
7. `event ContextActionRequested`
8. `event FilesDropped`

## 6. Вывод
Для текущего релиза функционально дешевле и безопаснее закрыть карту привязок через OLV-адаптер. Avalonia потребует не просто замену событий, а новый UI-layer и новый тестовый контур.

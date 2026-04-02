<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Привязка функций основной таблицы (2026-04-02)

## 1. Точки подписки на события таблицы
### 1.1 Queue и derived refresh
`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:861-865`
1. `RowsAdded` -> `HandleOrdersGridChanged()`
2. `RowsRemoved` -> `HandleOrdersGridChanged()`
3. `DataBindingComplete` -> `HandleOrdersGridChanged()`
4. `CellValueChanged` -> `DgvJobs_CellValueChanged`
5. `CellDoubleClick` -> `DgvJobs_CellDoubleClick`

### 1.2 Визуал/интеракции/drag-drop
`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:1053-1065`
1. `CellPainting` -> `DgvJobs_CellPainting`
2. `CellFormatting` -> `DgvJobs_CellFormatting`
3. `CellClick` -> `DgvJobs_CellClick`
4. `CellToolTipTextNeeded` -> `DgvJobs_CellToolTipTextNeeded`
5. `CellMouseEnter` -> `DgvJobs_CellMouseEnter`
6. `CellMouseLeave` -> `DgvJobs_CellMouseLeave`
7. `MouseLeave` -> `DgvJobs_MouseLeave`
8. `MouseDown` -> `DgvJobs_MouseDown`
9. `MouseMove` -> `DgvJobs_MouseMove`
10. `MouseUp` -> `DgvJobs_MouseUp`
11. `DragEnter` -> `DgvJobs_DragEnter`
12. `DragOver` -> `DgvJobs_DragOver`
13. `DragDrop` -> `DgvJobs_DragDrop`

### 1.3 Состояние кнопок/выделения
`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:1071-1086`
1. `SelectionChanged` -> синхронизация list/tiles + `UpdateActionButtonsState()` + `UpdateTrayStatsIndicator()`
2. `CurrentCellChanged` -> синхронизация list/tiles + `UpdateActionButtonsState()` + `UpdateTrayStatsIndicator()`

### 1.4 Контекстное меню
`Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.ContextMenu.cs:102`
1. `CellMouseDown` -> `DgvJobs_CellMouseDown` (правый клик, контекст row/column)

### 1.5 Кастомный скролл
`Features/Orders/UI/OrdersWorkspace/Views/OrdersWorkspaceForm.OrdersViewScrollBar.cs:41-47`
1. `Scroll` -> `DgvJobs_ScrollForCustomBar`
2. `MouseWheel` -> `DgvJobs_MouseWheelForOrdersViewScrollBar`
3. `RowsAdded/RowsRemoved/DataBindingComplete/SizeChanged/VisibleChanged` -> `UpdateOrdersViewScrollBarFromActiveView()`

## 2. Карта поведения по основным обработчикам
| Обработчик | Что делает | Ключевые зависимости |
|---|---|---|
| `DgvJobs_CellClick` | Раскрытие group-order, open file, pick+copy file по stage, item/order branching | `GetStageByColumnIndex`, `ResolveOrderFromRowTag`, `PickAndCopyFileForOrderAsync`, `PickAndCopyFileForItemAsync` |
| `DgvJobs_CellDoubleClick` | Выбор PitStop/Imposing action или редактирование заказа | `SelectPitStopActionFromGrid`, `SelectImposingActionFromGrid`, `EditOrderFromGridAsync` |
| `DgvJobs_CellFormatting` | Цвета строк/ячеек, link-like текст для файлов, подсветка missing files | `ResolveOrderFromRowTag`, `TryResolveGridFilePathForFormatting`, `HasExistingFileCachedForUi` |
| `DgvJobs_CellPainting` | Полный кастомный paint статуса и шапки, подавление focus rectangle | `TryPaintStatusCell` |
| `DgvJobs_DragDrop` | Добавление файла drag-drop в order/item stage | `HandleGridFileDropAsync`, `TryResolveGridFileCell`, `AddDraggedFileToTargetAsync` |
| `DgvJobs_CellToolTipTextNeeded` | Tooltip причины ошибки статуса | `GetOrderByRowIndex`, `NormalizeStatus` |
| `DgvJobs_MouseDown/Move/Up` | Выделение, drag-init, hover/selection контроль | `TryResolveGridFileCell`, `CollapseDragSelectionToSingleRow` |

Источник: `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.GridInteractions.cs`.

## 3. Привязка действий контекстного меню
`Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.ContextMenu.cs:24-100`
1. Открыть папку -> `OpenOrderStageFolder(...)`
2. Удалить заказ -> `RemoveSelectedOrderAsync()`
3. Запустить/остановить -> `RunSelectedOrderAsync()` / `StopSelectedOrderAsync()`
4. Pick/Remove/Rename/Paste file -> stage-specific file ops
5. Watermark и CopyToGrandpa -> print-stage операции
6. PitStop/Imposing manager -> manager forms
7. Лог заказа -> `OpenOrderLogForOrderOnly(...)`

## 4. Системные привязки после изменения данных
| Точка | Что срабатывает |
|---|---|
| `RebuildOrdersGrid()` | Полная перестройка строк + `HandleOrdersGridChanged()` |
| `PersistGridChanges()` | Применяет `OrderGridMutationUiPlan`: save, fast-refresh или rebuild |
| `HandleOrdersGridChanged()` | Инвалидация кешей + coalesced derived refresh |
| `ApplyOrdersGridDerivedRefreshCore()` | Фильтры, captions, queue presentation, tiles refresh, tray indicators |

Источники: `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:91-173`, `:547-603`, `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.StageFileOps.cs:262-288`.

## 5. Тестовые привязки (важно для миграции)
1. Тесты напрямую ожидают приватные поля `dgvJobs`, `colStatus`, `colOrderNumber`, `colPrep`, `colPrint` и др.
2. Тесты вызывают приватные методы формы рефлексией.

Источники: `tests/Replica.UiSmokeTests/MainFormCoreRegressionTests.cs:95`, `:615-619`, `:796-800`, `tests/Replica.UiSmokeTests/MainFormTestHarness.cs:94-139`.

## 6. Вывод для миграции
1. Привязок много и они широкие: таблица — центральный event-hub формы.
2. При миграции нельзя просто заменить контрол в Designer; нужен adapter-слой с совместимыми API для обработчиков и тестов.
3. Лучшая последовательность: сначала вынести domain-операции из `dgvJobs`-обработчиков, потом подключать OLV.
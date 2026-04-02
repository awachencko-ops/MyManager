<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Аудит миграции основной таблицы на OLV (ObjectListView.Repack.Core3) — 2026-04-02

## Цель
Перевести основной list-контур заказов с `DataGridView` на `OLV` (`TreeListView`) без остановки работы текущей программы, снять фризы, вернуть стабильную сортировку и сохранить рабочие сценарии операторов.

## Режим внедрения (зафиксировано)
1. Новая OLV-таблица реализуется в отдельном окне прототипа.
2. Текущая рабочая таблица (`dgvJobs`) не отключается и не заменяется на этапе разработки.
3. Перенос OLV в рабочее окно выполняется только после достижения полного функционального паритета.
4. Финальный перенос выполняется только после отдельного согласования с владельцем продукта.

## Текущая техническая база (as-is)
1. Основной контрол: `dgvJobs` (`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs:324`).
2. Полная перестройка списка делается вручную через `RebuildOrdersGrid()` (`Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:547`, `:921`, `:974`).
3. Иерархия group/item держится вручную через `Tag` и insert/remove строк (`...OrdersLifecycle.cs:932`, `:985`, `:1002-1098`).
4. Фильтрация идет через `row.Visible` на UI-строках (`Features/Orders/UI/OrdersWorkspace/Filters/OrdersWorkspaceForm.Filters.Evaluation.cs:209-312`).
5. Большой event-hub вокруг `dgvJobs` (клики, формат, рисование, drag-drop, selection) (`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:862-866`, `:1054-1066`, `:1072-1086`).
6. Тесты завязаны на приватные поля `dgvJobs` и колонки (`tests/Replica.UiSmokeTests/MainFormCoreRegressionTests.cs:95`, `:615-617`, `:1480-1481`).

## Почему OLV лучше для текущего этапа
1. `OLV` работает в текущем WinForms-контуре и не требует переписывания оболочки экрана.
2. Есть нативная иерархия (`TreeListView`) вместо ручного `Rows.Insert/Remove`.
3. Есть встроенная сортировка и sort indicators (`Sort`, `ShowSortIndicator(s)`).
4. Есть event-модель, которая покрывает текущие сценарии (`CellClick`, `CellRightClick`, `CellToolTipShowing`, `FormatRow/FormatCell`, `CanDrop/Dropped`).
5. Миграция может идти под feature-flag с быстрым rollback.

## Что уже готово в проекте
1. Пакет подключен в основном проекте: `ObjectListView.Repack.Core3 2.9.3` (`Replica.csproj:17`).
2. Есть рабочий OLV prototype form с tree-колонками и статусами:
- `Features/Orders/UI/OrdersWorkspace/Prototypes/OrdersTreePrototypeForm.cs`.
3. В основном UI есть кнопка запуска OLV прототипа:
- `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs` (`OLV proto`).
- `Features/Orders/UI/OrdersWorkspace/Prototypes/OrdersWorkspaceForm.TreePrototype.cs`.

## Риски миграции
| Риск | Вероятность | Влияние | Как закрываем |
|---|---|---|---|
| Потеря паритета stage-кликов/drag-drop | Высокая | Высокое | Вынести stage-операции в отдельный interaction-service до замены контрола |
| Регрессии фильтров/counters | Высокая | Высокое | Перевести фильтрацию и счетчики на модель данных, не на UI-строки |
| Ломка выбора/синхронизации list/tiles | Средняя | Высокое | Единый `SelectionState` по `orderInternalId/itemId` |
| Визуальная деградация статусов | Средняя | Среднее | Перенести палитру и статусный renderer через `FormatCell` |
| Ломка smoke-тестов | Очень высокая | Высокое | Ввести adapter-level тесты и постепенно снять привязку к приватному `dgvJobs` |

## Ключевые решения до начала кода
1. Основной OLV-контрол: `TreeListView` (для group/item дерева).
2. Источник истины: `OrdersGridRowModel` + builder, не UI-строки.
3. Миграция только под `feature-flag` (`UseOlvOrdersGrid`) с rollback на DataGridView.
4. Сначала перенос domain/presentation логики, потом замена UI-контрола.
5. Разработка и доводка OLV-контура идут в отдельном окне, рабочее окно меняется только после sign-off.

## Мое мнение
Для production-внедрения сейчас OLV — лучший вариант по рискам и скорости. Он дает нужную производительность и иерархию в текущем WinForms, без большого архитектурного взрыва.

<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Аудит миграции основной таблицы на ObjectListView.Repack.Core3 (2026-04-02)

## Контекст
Цель: заменить основную таблицу заказов (`dgvJobs`) с `DataGridView` на `ObjectListView.Repack.Core3`, чтобы убрать фризы, улучшить отзывчивость, стабилизировать сортировку/фильтрацию и упростить поддержку.

Область аудита: только основной workspace заказов (`Features/Orders/UI/OrdersWorkspace`) и связанные smoke-тесты.

## Фактическая база (по коду)
1. Основная таблица сейчас реализована как `DataGridView dgvJobs` с 9 колонками в `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs:313-394`.
2. Сортировка колонок принудительно отключена (`DataGridViewColumnSortMode.NotSortable`) в `UI/GridStyleHelper.cs:42-49` и вызывается из `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceGridStyle.cs:69`.
3. Полная перерисовка таблицы идет через `RebuildOrdersGrid()` с `Rows.Clear()` и повторным `Rows.Add()/Rows.Insert()` для всех order/item-строк: `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:547-587` и `:921-986`.
4. Фильтрация делается постфактум по уже созданным строкам: двойной проход по `dgvJobs.Rows` и переключение `row.Visible` (`Features/Orders/UI/OrdersWorkspace/Filters/OrdersWorkspaceForm.Filters.Evaluation.cs:209-323`).
5. В таблицу навешано много обработчиков ввода/отрисовки/drag-drop/selection (`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:861-865`, `:1053-1065`, `:1071-1086`, `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.ContextMenu.cs:102`, `Features/Orders/UI/OrdersWorkspace/Views/OrdersWorkspaceForm.OrdersViewScrollBar.cs:41-47`).
6. Есть heavy custom paint и per-cell логика (`CellPainting`, `CellFormatting`, статусные иконки, hover, проверка существования файлов с кэшем) в `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.GridInteractions.cs:261-373` и `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.StatusCellVisuals.cs:210-312`.
7. Есть отдельный плиточный режим (`ImageListView`) и двусторонняя синхронизация выбора с таблицей: `Features/Orders/UI/OrdersWorkspace/Views/OrdersWorkspaceForm.PrintTiles.cs:132-170`, `:486-581`, `:665-809`.
8. В проекте есть smoke-тесты, которые рефлексией ожидают именно `DataGridView dgvJobs` и колонки `colStatus/...`: `tests/Replica.UiSmokeTests/MainFormCoreRegressionTests.cs:95`, `:615-619`, `:1480-1484`.

## Наблюдаемые узкие места
1. Архитектурный: таблица одновременно отвечает за представление, вычисление видимости, идентификацию сущностей через `Tag`, часть бизнес-правил и синхронизацию с tiles-view.
2. Производительный: большое количество O(n)-проходов по `Rows` на rebuild, фильтры, counters, tiles-refresh и stats (`OrdersLifecycle`, `Filters.Evaluation`, `StatusStrip`, `PrintTiles`).
3. UI-событийный: высокая «плотность» обработчиков на один контрол + кастомный скроллбар, что увеличивает вероятность event-storm и фризов на больших наборах.
4. UX-сортировка: пользовательская сортировка в таблице фактически выключена (`DisableSorting`), из-за чего текущая модель не закрывает потребность в стабильных sort toggles.
5. Тестовый: текущие regression/smoke тесты сильно завязаны на приватные поля `DataGridView`, что ломает безболезненную замену контрола.

## Оценка пакета ObjectListView.Repack.Core3
1. NuGet-пакет `ObjectListView.Repack.Core3` (версия `2.9.3`) совместим с `netcoreapp3.1` и `net5.0-windows7.0` и выше, значит для текущего `net8.0-windows` проекта подходит.
2. В составе доступны `ObjectListView`, `FastObjectListView`, `TreeListView`, `VirtualObjectListView` (XML API docs пакета `ObjectListView2019Core3.xml`).
3. Для скорости особенно релевантен `FastObjectListView` (документация класса: быстрый virtual-backed режим, `ObjectListView2019Core3.xml:1717-1733`).
4. Для текущей иерархии order/item релевантен `TreeListView` (встроенная иерархическая модель, вместо ручных `Rows.Insert/Remove`).
5. Есть встроенные механики сортировки и индикаторов сортировки (`ObjectListView.Sort`, `ShowSortIndicator`, `ObjectListView2019Core3.xml:7223-7252`).

## Риски миграции (что учесть)
| Риск | Вероятность | Влияние | Что делать заранее |
|---|---|---|---|
| Потеря функционального паритета кликов/drag-drop по стадиям | Высокая | Высокое | Вынести stage-операции в отдельный adapter-сервис до замены контрола |
| Ломка group-order поведения (expand/collapse, item rows) | Средняя | Высокое | Явно выбрать модель: `TreeListView` или flat-row VM; не смешивать |
| Просадка UX из-за несовпадения кастомной отрисовки | Средняя | Среднее | Сразу заложить renderer-слой и сверять визуал по скриншотам/чек-листу |
| Регрессии фильтров/очереди/счетчиков | Высокая | Высокое | Перевести фильтры с обхода UI-строк на обход модели данных |
| Падение текущих smoke/regression тестов | Очень высокая | Высокое | Параллельно сделать новый тестовый harness через интерфейс представления |
| Синхронизация list/tiles станет нестабильной | Средняя | Среднее | Ввести единый SelectionState по `orderInternalId`, без прямой UI-зависимости |
| Риск от устаревания/ограничений внешней библиотеки | Средняя | Среднее | Сначала MVP на feature-flag + fallback на DataGridView до стабилизации |

## Критичные решения до начала кодинга
1. Какой контрол берем за основной: `FastObjectListView` (скорость) или `TreeListView` (нативная иерархия).
2. Где будет «истина» фильтра/сортировки: только модель (рекомендуется), а не свойства UI-строк.
3. Какой уровень совместимости с текущими тестами нужен на первом этапе: адаптерный слой или полная переписка тестов.
4. Сохраняем ли кастомный внешний вертикальный скроллбар (`pnlScrollBar`) или уходим на нативный скролл OLV.

## Мое мнение
Миграция на OLV в этом проекте оправдана и нужна. Текущий `DataGridView`-контур уже перегружен: много ручных проходов, ручная иерархия через `Tag`, сложная отрисовка и большая событийная связность. Это естественно приводит к «тупнякам» и фризам при росте данных.

Рекомендую идти поэтапно и безопасно:
1. Сначала выделить слой модели представления таблицы (row view-model + adapter операций) без смены UI.
2. Затем подключить OLV под feature-flag и добиться функционального паритета на ключевых сценариях.
3. Только после стабилизации перевести по умолчанию на OLV и убрать DataGridView-ветку.

Это минимизирует риск «большого взрыва» и даст реальный прирост производительности без потери рабочих сценариев.
<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Структура основной таблицы заказов (2026-04-02)

## 1. Положение в UI
1. Таблица встроена в `pnlTable` и занимает центральную область формы (`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs:303-330`).
2. Справа расположен отдельный контейнер `pnlScrollBar` под кастомный вертикальный скролл (`...Designer.cs:396-402`).
3. Над таблицей отдельные панели queue/search/filters, влияющие на видимость строк (`...Designer.cs:404-450` и `Features/Orders/UI/OrdersWorkspace/Filters/*`).

## 2. Колонки
| Поле | Заголовок | Смысл | Заполнение для order-строки | Заполнение для item-строки |
|---|---|---|---|---|
| `colStatus` | `Состояние` | Текущий workflow статус | `displayStatus` (`ApplyOrderRowValues`) | `itemStatus` (`ApplyOrderItemRowValues`) |
| `colOrderNumber` | `№ заказа` | Номер заказа | `BuildOrderRowCaption(order, isExpanded)` | `order.Id` |
| `colSource` | `Исходные` | Файл source | `ResolveSingleOrderDisplayPath(OrderStages.Source)` | `item.SourcePath` |
| `colPrep` | `Заголовок задания` | Файл prepared | `ResolveSingleOrderDisplayPath(OrderStages.Prepared)` | `item.PreparedPath` |
| `colPitstop` | `Проверка файлов` | Действие PitStop | `ResolveSingleOrderDisplayAction(...)` | item-level с fallback на order |
| `colHotimposing` | `Спуск полос` | Действие Imposing | `ResolveSingleOrderDisplayAction(...)` | item-level с fallback на order |
| `colPrint` | `Готов к печати` | Файл print | `ResolveSingleOrderDisplayPath(OrderStages.Print)` | `item.PrintPath` |
| `colReceived` | `Заказ принят` | Дата заказа | `FormatDate(order.OrderDate)` | `FormatDate(order.OrderDate)` |
| `colCreated` | `В препрессе` | Дата прихода в препресс | `FormatDate(order.ArrivalDate)` | `FormatDate(order.ArrivalDate)` |

Источники: `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs:332-394`, `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:705-778`, `:921-986`.

## 3. Типы строк и идентификация
1. Контейнер заказа: `Tag = order|{orderInternalId}`.
2. Строка item: `Tag = item|{orderInternalId}|{itemId}`.
3. Парсинг тегов и восстановление сущностей делается через `OrderGridLogic`.

Источник: `Features/Orders/UI/OrdersWorkspace/Core/OrderGridLogic.cs:11-59`, `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:932`, `:985`.

## 4. Сборка данных в таблицу
1. Полный rebuild:
- сортировка history по `ArrivalDate` убыв. (`OrdersLifecycle.cs:566-568`);
- поиск по `tbSearch` через `OrderMatchesSearch` (`:570-576`);
- полная очистка `dgvJobs.Rows.Clear()` (`:564`);
- добавление order/item строк (`:585-587`, `:921-986`).
2. Инкрементальное обновление:
- `TryRefreshGridRowsWithoutRebuild` обновляет значения в существующих строках (`:619-703`).
3. Частичное раскрытие/сворачивание group-order:
- `ToggleOrderExpanded` + `TryToggleOrderExpandedRowsInPlace` (`:1002-1057`).

## 5. Фильтрация и видимость
1. Фильтры применяются после построения таблицы: `ApplyStatusFilterToGrid()`.
2. Сначала проход по order-строкам, затем второй проход по item-строкам.
3. Видимость меняется через `row.Visible`, а счетчики кешируются отдельно.

Источник: `Features/Orders/UI/OrdersWorkspace/Filters/OrdersWorkspaceForm.Filters.Evaluation.cs:209-327`, `:522-575`.

## 6. Связь с другими представлениями
1. Есть параллельный tiles-view (`ImageListView`) со своим набором элементов.
2. Tiles строятся из `visible` order-строк таблицы.
3. Синхронизация выделения двусторонняя: `grid -> tiles` и `tiles -> grid`.

Источник: `Features/Orders/UI/OrdersWorkspace/Views/OrdersWorkspaceForm.PrintTiles.cs:486-581`, `:665-809`.

## 7. Что важно для миграции
1. Таблица сейчас не просто «рисует», а хранит много вычисленного состояния (видимость, selection, Tag-идентификацию).
2. Замена контрола потребует переноса этой логики в модельный/adapter слой.
3. Без этого миграция станет переносом technical debt в другой UI-компонент.
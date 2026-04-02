<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Дизайн основной таблицы (as-is и target для OLV) — 2026-04-02

## 1. Текущий визуальный каркас (as-is)
1. Компоновка: header + filters + table + status-tray (`OrdersWorkspaceForm.Designer.cs:285-311`, `:404-414`).
2. Таблица: белый фон, full-row selection, row height = 42, собственная линия сетки, скрытые row headers (`OrdersWorkspaceGridStyle.cs:37-67`, `OrdersWorkspaceForm.Designer.cs:315-330`).
3. Цветовые токены вынесены в state-константы (`OrdersWorkspaceForm.State.cs:333-347`).
4. У table есть внешний кастомный вертикальный скролл (`OrdersViewScrollBar.cs:20-31`, `:228-303`).

## 2. Дизайн строк и состояний
1. Базовые строки: белая + очень легкая zebra (`OrdersRowBaseBackColor` / `OrdersRowZebraBackColor`).
2. Hover и selected у обычных строк мягкие, без агрессивного контраста.
3. Group-order контейнер и item-строки имеют отдельную «теплую» палитру (желтовато-кремовую), визуально отделяя иерархию.
4. В status-ячейке рисуется:
- левый маркер активной строки,
- цветной блок под иконку,
- иконка статуса,
- текст статуса.

Источник: `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.StatusCellVisuals.cs:210-271`, `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.GridInteractions.cs:315-373`.

## 3. Дизайн фильтров
1. Фильтры находятся не в header ячейках, а в отдельной `flpFilters` панели.
2. Каждый фильтр — пара `glyph + label` (кастомный chevron и подпись).
3. Выпадающие панели фильтров реализованы вручную через `ToolStripDropDown`.

Источник: `Features/Orders/UI/OrdersWorkspace/Filters/OrdersWorkspaceForm.Filters.SetupQueue.cs:17-222`, `Features/Orders/UI/OrdersWorkspace/Filters/OrdersWorkspaceForm.Filters.Evaluation.cs`.

## 4. Дизайн взаимодействий
1. Одиночный клик по file-stage ячейке: open existing file или pick+copy.
2. Двойной клик:
- PitStop/Imposing колонки -> выбор действия,
- Номер заказа -> редактирование заказа.
3. Drag&Drop поддерживает перенос/копирование файлов по stage-ячейкам.
4. Правый клик вызывает контекстное меню с action-картой по колонке.

Источник: `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.GridInteractions.cs`, `...ContextMenu.cs`, `OrderGridContextMenu.cs`.

## 5. Target-дизайн на OLV (сохранить UX, убрать технический долг)
### 5.1 Что сохраняем
1. Ту же визуальную палитру строк, hover/selected и статусные иконки.
2. Отдельную панель фильтров (не переносим фильтры в заголовки).
3. Двухрежимность List/Tiles (tiles остаются на `ImageListView`, list мигрирует на OLV).
4. Контекстные действия по stage и drag-drop-сценарии.

### 5.2 Что меняем
1. Источник данных таблицы: вместо ручных `Rows.Add/Insert/Remove` — object-model в OLV.
2. Отрисовка: переносим в OLV renderer/format hooks (`FormatRow`, `FormatCell`, custom renderer).
3. Идентификацию строки через `Tag` заменяем на явный row-model с полями `RowType`, `OrderInternalId`, `ItemId`.
4. Фильтрация/сортировка считаются на модели, а не через обход UI-строк.

### 5.3 Рекомендуемый выбор OLV-класса
1. Для производительности: `FastObjectListView`.
2. Для нативной иерархии order/item: `TreeListView`.
3. Компромисс: начать с `FastObjectListView + flat-row VM` для быстрого выигрыша, затем решить, нужна ли полноценная tree-модель.

## 6. Нефункциональные дизайн-требования миграции
1. Нулевая деградация текущих пользовательских сценариев (клики, DnD, контекстные действия).
2. Субъективная плавность при больших списках должна стать лучше текущей.
3. Сортировка должна быть предсказуемой и с явным визуальным индикатором.
4. Поддержка существующей темы/цветов без «визуального скачка».

## 7. Минимальный визуальный acceptance checklist
1. Цвета иконок/строк соответствуют текущей палитре.
2. Group-order контейнер и item визуально различимы.
3. Hover/selected не мерцают при быстром движении мыши.
4. Tooltip для `Ошибка` в статусе отображается корректно.
5. List/Tiles переключение не ломает выделение.
6. Скролл/колесо мыши не вызывают «ступенек» и резких прыжков.
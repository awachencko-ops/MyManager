<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# Аудит выбора стека таблицы: OLV vs Avalonia (2026-04-02)

## Цель документа
Принять техническое решение для основной таблицы заказов:
1. `OLV` (ObjectListView.Repack.Core3) внутри текущего WinForms.
2. `Avalonia` (TreeDataGrid) как новый UI-контур.

## Текущая база (по коду проекта)
1. Основной экран заказов сейчас жестко завязан на `DataGridView dgvJobs` и 9 колонок (`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs:324-346`).
2. Таблица строится вручную через `RebuildOrdersGrid()` + `Rows.Clear/Add/Insert` (`Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:547`, `:921`, `:974`).
3. Фильтрация и видимость делаются через обход UI-строк и `row.Visible` (`Features/Orders/UI/OrdersWorkspace/Filters/OrdersWorkspaceForm.Filters.Evaluation.cs:209`, `:286`, `:311`).
4. На таблицу навешан плотный набор событий и drag-drop (`Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:862-866`, `:1054-1066`, `:1072-1086`, `Features/Orders/UI/OrdersWorkspace/FileOps/OrdersWorkspaceForm.FileOps.GridInteractions.cs:261`, `:315`, `:642`, `:897`).
5. Тесты сильно завязаны на приватные поля `dgvJobs/colStatus/colOrderNumber` (`tests/Replica.UiSmokeTests/MainFormCoreRegressionTests.cs:95`, `:615-617`, `:1480-1481`).

## Прототипы, которые уже есть
1. WinForms `TreeListView` прототип на OLV:
- `Features/Orders/UI/OrdersWorkspace/Prototypes/OrdersTreePrototypeForm.cs:10`, `:39`, `:53-54`, `:78-85`.
2. Avalonia `TreeDataGrid` прототип:
- `Prototypes/AvaloniaOrdersPrototype/MainWindow.axaml.cs:24`, `:38`, `:41`, `:102`, `:132`.
3. Отдельная кнопка запуска Avalonia из main UI:
- `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.Designer.cs:408`, `:422-427`.
- `Features/Orders/UI/OrdersWorkspace/Prototypes/OrdersWorkspaceForm.TreePrototype.cs:33`, `:38`, `:62`.

## Внешние ограничения (подтверждено источниками)
1. `Avalonia.Controls.TreeDataGrid` начиная с `11.2.0` требует лицензию Avalonia Accelerate.
2. Для TreeDataGrid обязательно подключать theme include, иначе контрол не рендерится.
3. `ObjectListView.Repack.Core3 2.9.3` совместим с `netcoreapp3.1` и `net5.0-windows7.0+`, значит для нашего `net8.0-windows` подходит.

## Сравнение вариантов
| Критерий | OLV в WinForms | Avalonia TreeDataGrid |
|---|---|---|
| Объем изменений | Ниже (замена контрола/адаптер в существующей форме) | Выше (новый UI-стек, перенос интеракций и визуала) |
| Риск регрессий | Средний | Высокий |
| Скорость получения результата | Быстро | Медленнее |
| Совместимость с текущими тестами | Проще адаптировать постепенно | Понадобится новый тестовый контур |
| Лицензирование | Прозрачно для текущего пакета | Для актуальных версий TreeDataGrid есть лицензия |
| Долгосрочный UX-потенциал | Ограничен WinForms | Выше (современный UI-стек) |
| Итог для текущего релиза | Сильный кандидат | R&D/долгосрочный кандидат |

## Риски и что учесть
| Риск | Где критичен | Вероятность | Влияние | Митигирование |
|---|---|---|---|---|
| Потеря паритета stage-кликов/drag-drop | OLV/Avalonia | Высокая | Высокое | Вынести stage-операции в отдельный `GridInteractionService` до смены UI |
| Ломка group-order expand/collapse | OLV/Avalonia | Средняя | Высокое | Общий `RowModel` + явный контракт иерархии (`Order`/`Item`) |
| Ломка тестов из-за `dgvJobs`-зависимости | OLV/Avalonia | Очень высокая | Высокое | Ввести adapter-тесты, потом постепенно снять рефлексию на приватных полях |
| Проблемы лицензии/версии TreeDataGrid | Avalonia | Высокая | Высокое | Либо бюджет и лицензирование, либо заморозка на старой версии с осознанным техдолгом |
| UX-деградация (визуал/скролл/tooltip) | OLV/Avalonia | Средняя | Среднее | Чек-лист визуального паритета + smoke прогон на реальных наборах |

## Решение на сейчас (мое мнение)
Для основной production-миграции лучше `OLV` как основной путь.

Почему:
1. Мы уже в WinForms-контуре, а table logic глубоко интегрирована с form/event/test слоями.
2. OLV снижает стоимость перехода и позволяет закрыть текущую боль производительности быстрее.
3. Avalonia сейчас разумнее держать как parallel R&D-трек для следующего большого UI-этапа, а не как срочный cutover основной таблицы.

## Рекомендуемая стратегия
1. `Primary track`: OLV миграция под feature-flag с поэтапным переносом логики.
2. `R&D track`: Avalonia прототип развивать отдельно и проверять UX/производительность на тех же сценариях.
3. `Decision gate`: к Avalonia production-cutover возвращаться после стабилизации OLV и снятия тестовой/адаптерной связности от `DataGridView`.

## Источники
1. Avalonia TreeDataGrid docs: https://docs.avaloniaui.net/controls/data-display/structured-data/treedatagrid
2. NuGet Avalonia.Controls.TreeDataGrid: https://www.nuget.org/packages/Avalonia.Controls.TreeDataGrid/
3. NuGet ObjectListView.Repack.Core3: https://www.nuget.org/packages/ObjectListView.Repack.Core3/

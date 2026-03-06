# MIGRATION CHECKLIST: Form1 -> MainForm

Цель: переносить функциональность из legacy `Form1` в `MainForm` маленькими проверяемыми шагами,
не ломая текущую работу.

## Статусы
- `Legacy only` — пока есть только в `Form1`.
- `In progress` — перенос начат, но не завершён.
- `MainForm ready` — работает в `MainForm` и проверено вручную.
- `Stabilized` — несколько циклов использования без регрессий.

## Матрица паритета

| Область | Сценарий | Статус | Где в legacy | Где в новой форме | Примечание |
|---|---|---|---|---|---|
| Startup/Shell | Приложение стартует с `MainForm` | MainForm ready | `Form1` (архив) | `Forms/MainForm.cs` | Startup уже переключён |
| Navigation | Дерево очереди + `cbQueue` синхронизированы (6 статусов) | MainForm ready | `Forms/Archive/Form1.cs` | `Forms/MainForm.cs` | Маппинг рабочих статусов зафиксирован в `ORDER_TREE_STATUSES.md`, по умолчанию активен `Все задания` |
| Filters | Фильтр `Состояние задания` (чекбоксы + счётчики) | MainForm ready | `Forms/Archive/Form1.cs` | `Forms/MainForm.cs` | Popup вызывается по клику на заголовок/иконку |
| Filters | Фильтр `Номер заказа` (popup-поиск) | MainForm ready | `Forms/Archive/Form1.cs` | `Forms/MainForm.cs` | Кнопки `Очистить`/`Применить`, реакция на ввод и `Enter` |
| Filters | Фильтр `Пользователь` | In progress | `Forms/Archive/Form1.cs` | `Forms/MainForm.cs` | Временно используются болванки: `Андрей`, `Катя`, `Вероника` |
| Filters | Фильтры дат `Дата поступления` и `Начало обработки` | In progress | `Forms/Archive/Form1.cs` | `Forms/MainForm.cs` | Popup и кастомный календарь подключены, нужна финальная стабилизация сценариев закрытия |
| Grid | Каркас основной таблицы (`dgvJobs`, колонки, реакция на изменения строк) | In progress | `Forms/Archive/Form1.cs` | `Forms/MainForm.cs` | UI-скелет есть, фильтры применяются к текущим строкам |
| Grid | Наполнение таблицы заказами (аналог `FillGrid`) | Legacy only | `Forms/Archive/Form1.cs` | - | В `MainForm` пока нет полного пайплайна загрузки/построения строк |
| Grid | Контекстное меню таблицы и операции по строкам | Legacy only | `Forms/Archive/Form1.cs` + `UI/OrderGridContextMenu.cs` | - | Требуется подключение `OrderGridContextMenu` в `MainForm` |
| Grid | Drag&Drop, tooltips, форматирование/подсветка ячеек, double-click действия | Legacy only | `Forms/Archive/Form1.cs` | - | UI-поведение таблицы пока не мигрировано в `MainForm` |
| Orders | Загрузка списка заказов | Legacy only | `Forms/Archive/Form1.cs` | - | Перенос в очередь |
| Orders | Выбор/открытие заказа | Legacy only | `Forms/Archive/Form1.cs` | - | Перенос в очередь |
| Actions | Контекстное меню заказа | Legacy only | `UI/OrderGridContextMenu.cs` + `Forms/Archive/Form1.cs` | - | Переиспользовать текущий UI helper |
| Actions | Запуск обработки заказа | Legacy only | `Forms/Archive/Form1.cs` + `Services/OrderProcessor.cs` | - | Вынести orchestration в сервисный слой |
| Actions | Операции copy/move/open folder | Legacy only | `Forms/Archive/Form1.cs` | - | Нужен слой command/service |
| Logs | Просмотр лога заказа | Legacy only | `Forms/OrderLogViewerForm.cs` + `Forms/Archive/Form1.cs` | - | Подвязать из MainForm |
| Settings | Открытие/сохранение настроек | MainForm ready | `Forms/SettingsDialogForm.cs` + `Models/AppSettings.cs` | `Forms/MainForm.cs` | Кнопка `Параметры` на `tsMainActions` открывает диалог и сохраняет настройки
| Configs | Управление PitStop/Imposing | Legacy only | `Forms/ActionManagerForm.cs`, `Forms/ImposingManagerForm.cs` | - | Доступ из MainForm через меню |
| Stability | Возможность fallback на legacy | MainForm ready | `Forms/Archive/Form1.cs` | `MainForm` как startup | Legacy сохранён как архив |

## Карта перетока (старое -> новое)

| Что в legacy (`Form1`) | Куда перетекает в новой схеме | Где фиксируется/реализуется | Статус |
|---|---|---|---|
| Startup: запуск главной формы | `MainForm` как основной shell | `Program.cs` (`Application.Run(new MainForm())`) | MainForm ready |
| Старый монолитный экран `Form1` | Архивный fallback + постепенный вынос сценариев | `Forms/Archive/Form1.cs` + `MIGRATION_CHECKLIST.md` | MainForm ready (как fallback) |
| Навигация по очереди и статусам | `treeView1` + `cbQueue` в `MainForm` | `Forms/MainForm.cs`, `ORDER_TREE_STATUSES.md` | MainForm ready |
| Сопоставление рабочих статусов с узлами очереди | `QueueStatusMappings` + статусы фильтра | `Forms/MainForm.cs`, `ORDER_TREE_STATUSES.md` | MainForm ready |
| Фильтр статусов заказа | Popup checklist с чекбоксами и счётчиками | `Forms/MainForm.cs` (`Состояние задания`) | MainForm ready |
| Поиск по номеру заказа | Popup-поиск с `Очистить`/`Применить` | `Forms/MainForm.cs` (`Номер заказа`) | MainForm ready |
| Фильтр по пользователю | Popup checklist; затем подключение к реальному источнику | `Forms/MainForm.cs` (`Пользователь`) | In progress |
| Фильтры по датам | Popup + календарь для `Дата поступления`/`Начало обработки` | `Forms/MainForm.cs` | In progress |
| Настройки и пути файлов | Диалог настроек + нормализация путей + сохранение | `Forms/MainForm.cs`, `Forms/SettingsDialogForm.cs`, `Models/AppSettings.cs`, `Services/StoragePaths.cs` | MainForm ready |
| Маршрутизация лог-файла менеджера | Централизованный путь в `Logger` | `Services/Logger.cs` + `Forms/MainForm.cs` | MainForm ready |
| Запуск обработки заказов | Из UI в сервис обработки | `Services/OrderProcessor.cs` (интеграция из `MainForm` запланирована) | Legacy only / planned |
| Контекстные действия по заказу | Переиспользуемый UI helper + интеграция в `MainForm` | `UI/OrderGridContextMenu.cs` | Legacy only / planned |
| Просмотр лога заказа | Вызов `OrderLogViewerForm` из `MainForm` | `Forms/OrderLogViewerForm.cs` + `Forms/MainForm.cs` | Legacy only / planned |
| Менеджеры конфигов (PitStop/Imposing) | Вызов из `MainForm` через меню/настройки | `Forms/ActionManagerForm.cs`, `Forms/ImposingManagerForm.cs` | Legacy only / planned |

## Файловая карта миграции

| Было | Стало |
|---|---|
| `Forms/Form1.*` как основная форма | `Forms/MainForm.*` как основная форма; `Forms/Archive/Form1.*` как legacy fallback |
| Логика, смешанная в форме | Разделение по слоям: `Models/`, `Services/`, `UI/`, `Forms/` |
| Точечные сценарии из `Form1` | Поэтапный перенос в `MainForm` с фиксацией статуса в этом чеклисте |

## UI-события и маршрутизация (что нажимают -> куда идет)

| Действие пользователя | Контрол / событие в MainForm | Обработчик и маршрут в новой форме | Эквивалент/источник в legacy | Статус |
|---|---|---|---|---|
| Открыть настройки | `tsMainActions.ItemClicked` (`tsbParameters`) | `TsMainActions_ItemClicked` -> `ShowSettingsDialog()` -> `AppSettings`/`StoragePaths`/`Logger` | `Form1` -> `SettingsDialogForm` + сохранение путей | MainForm ready |
| Выбрать пользователя/статус в левой очереди | `treeView1.AfterSelect` | `TreeView1_AfterSelect` -> `SelectUser` -> `FillQueueCombo` | Логика выбора очереди в `Form1` | MainForm ready |
| Выбрать статус через верхний `cbQueue` | `cbQueue.SelectedIndexChanged` | `CbQueue_SelectedIndexChanged` -> поиск узла -> синхронизация `treeView1.SelectedNode` | Логика синхронизации статуса в `Form1` | MainForm ready |
| Изменения в таблице заказов | `dgvJobs.RowsAdded/RowsRemoved/DataBindingComplete/CellValueChanged` | `HandleOrdersGridChanged` -> `ApplyStatusFilterToGrid` + обновление фильтров/счетчиков/очереди | Частичная логика обновления grid в `Form1` | In progress |
| Открыть фильтр статусов | `lblFStatus.Click` / `picFStatusGlyph.Click` | `LblFStatus_Click` -> `ShowStatusFilterDropDown` | Статусный фильтр из legacy UI | MainForm ready |
| Поставить/снять статус в фильтре | checklist `ItemCheck` | `StatusFilterCheckedList_ItemCheck` -> `UpdateSelectedStatusesFromChecklist` -> `ApplyStatusFilterToGrid` | Статусный фильтр в `Form1` | MainForm ready |
| Поиск по номеру заказа | `lblFOrderNo.Click` / `picFOrderNoGlyph.Click`, ввод в popup | `LblFOrderNo_Click` -> `ShowOrderNoFilterDropDown`; затем `ApplyOrderNoFilterFromPopup` (`Enter`/`Применить`/`Очистить`) | Поиск по ID в legacy grid | MainForm ready |
| Фильтр по пользователю | `lblFUser.Click` / `picFUserGlyph.Click`, checklist popup | `LblFUser_Click` -> `ShowUserFilterDropDown` -> `ApplyUserFilterFromPopup` | Пользовательский фильтр из legacy | In progress |
| Фильтр `Дата поступления` | `lblFCreated.Click` / `picFCreatedGlyph.Click`, popup + календарь | `LblFCreated_Click` -> `ShowCreatedDateFilterDropDown`; применение через `ApplyCreatedDateFilterFromPopup` | Датовый фильтр legacy | In progress |
| Фильтр `Начало обработки` | `lblFReceived.Click` / `picFReceivedGlyph.Click`, popup + календарь | `LblFReceived_Click` -> `ShowReceivedDateFilterDropDown`; применение через `ApplyReceivedDateFilterFromPopup` | Датовый фильтр legacy | In progress |
| Запустить менеджер PitStop | (пока не подключено в MainForm) | План: обработчик в MainForm -> `ActionManagerForm` | В `Form1`: `OpenPitStopManager()` | Legacy only / planned |
| Запустить менеджер Imposing | (пока не подключено в MainForm) | План: обработчик в MainForm -> `ImposingManagerForm` | В `Form1`: `OpenImposingManager()` | Legacy only / planned |
| Просмотр лога выбранного заказа | (пока не подключено в MainForm) | План: открыть `OrderLogViewerForm` из MainForm | В `Form1`: открытие `OrderLogViewerForm` | Legacy only / planned |
| Операции запуска/остановки/удаления заказа | `tsbRun/tsbStop/tsbRemove` (кнопки есть, маршрутизация не подключена) | План: маршрутизация в `OrderProcessor` и сервисный слой | В `Form1`: `_processor = new OrderProcessor(...)` + команды | Legacy only / planned |

## Правило выполнения итерации
1. Выбрать **1 сценарий** из таблицы.
2. Перенести логику в `Services` (если необходимо).
3. Подключить сценарий в `MainForm`.
4. Ручная проверка сценария.
5. Обновить статус в таблице.

## Ближайшие 3 шага (предложение)
1. Завершить стабилизацию popup-календарей в фильтрах дат (`Дата поступления`, `Начало обработки`) и закрыть все пограничные сценарии закрытия/перекрытия.
2. Подключить фильтр `Пользователь` к реальному источнику пользователей (вместо временных болванок).
3. Подключить из `MainForm` сценарий открытия/просмотра заказа (включая просмотр лога через `OrderLogViewerForm`).

## Зафиксированные договоренности по статусам
- Актуальное сопоставление рабочих статусов и групп `treeView1`/`cbQueue` фиксируется в `ORDER_TREE_STATUSES.md`.

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
| Orders | Загрузка списка заказов | Legacy only | `Forms/Archive/Form1.cs` | - | Перенос в очередь |
| Orders | Выбор/открытие заказа | Legacy only | `Forms/Archive/Form1.cs` | - | Перенос в очередь |
| Actions | Контекстное меню заказа | Legacy only | `UI/OrderGridContextMenu.cs` + `Forms/Archive/Form1.cs` | - | Переиспользовать текущий UI helper |
| Actions | Запуск обработки заказа | Legacy only | `Forms/Archive/Form1.cs` + `Services/OrderProcessor.cs` | - | Вынести orchestration в сервисный слой |
| Actions | Операции copy/move/open folder | Legacy only | `Forms/Archive/Form1.cs` | - | Нужен слой command/service |
| Logs | Просмотр лога заказа | Legacy only | `Forms/OrderLogViewerForm.cs` + `Forms/Archive/Form1.cs` | - | Подвязать из MainForm |
| Logs | Открытие лога менеджера из верхнего меню | MainForm ready | `Forms/Archive/Form1.cs` (OpenLogFile) | `Forms/MainForm.cs` | Добавлен пункт `Параметры -> Лог менеджера`, меню закреплено в верхней шапке формы |
| Settings | Открытие/сохранение настроек | MainForm ready | `Forms/SettingsDialogForm.cs` + `Models/AppSettings.cs` | `Forms/MainForm.cs` | Открытие по кнопке `tsbConfig`, сохранение в `AppSettings` подключено |
| Configs | Управление PitStop/Imposing | Legacy only | `Forms/ActionManagerForm.cs`, `Forms/ImposingManagerForm.cs` | - | Доступ из MainForm через меню |
| Stability | Возможность fallback на legacy | MainForm ready | `Forms/Archive/Form1.cs` | `MainForm` как startup | Legacy сохранён как архив |

## Правило выполнения итерации
1. Выбрать **1 сценарий** из таблицы.
2. Перенести логику в `Services` (если необходимо).
3. Подключить сценарий в `MainForm`.
4. Ручная проверка сценария.
5. Обновить статус в таблице.

## Ближайшие 3 шага (предложение)
1. ✅ Подключено: открытие `SettingsDialogForm` из `MainForm` (`tsbConfig`).
2. Подключить из `MainForm` просмотр `OrderLogViewerForm` для выбранного заказа.
3. Подключить из `MainForm` запуск `OrderProcessor` для тестового сценария.

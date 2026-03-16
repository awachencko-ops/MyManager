# Этап 1: Single-Order Regression Checklist

Дата запуска чеклиста: 2026-03-16
Дата закрытия: 2026-03-16
Статус: Completed

## 1. Цель

Подтвердить, что после миграции на новый `MainForm` single-order сценарии работают без блокирующих дефектов (`P0/P1`).

## 2. Область проверки

Проверяется только этап `single-first`:
- один заказ (модель group-order из одного item),
- операции файловых стадий `in/prepress/print`,
- фильтры, статусы, StatusStrip, NAS/offline fallback пользователей.

## 3. Прогоны

| ID | Сценарий | Тип | Критерий | Статус | Комментарий |
|---|---|---|---|---|---|
| SR-01 | Сборка проекта | Auto | `dotnet build` = `0 warnings`, `0 errors` | PASS | Пройдено 2026-03-16 |
| SR-02 | Старт приложения и загрузка истории | Manual | `MainForm` открывается, история читается, UI не падает | PASS | Smoke-старт без аварийного завершения + `history.json` валиден (массив, 6 записей) |
| SR-03 | Создание simple заказа | Manual | Создан заказ, строка появилась в гриде, статус корректный | PASS | Auto PASS: `verify_single_order_regression` (`AddCreatedOrder` + запись истории) + `MainFormSmokeTests` (launch/grid) |
| SR-04 | Редактирование simple заказа | Manual | Изменения сохраняются, перезагрузка формы сохраняет данные | PASS | Auto PASS: `verify_single_order_regression` (edit + reload `history.json`) |
| SR-05 | Операции файлов стадий | Manual | Добавить/переименовать/удалить файл на `in/prepress/print` без ошибок | PASS | Auto PASS: core lifecycle add/rename/delete (`GetStageFolder` + `UpdateOrderFilePath`) |
| SR-06 | Запуск/остановка обработки | Manual | Статусы переходят корректно (`Обрабатывается`/`Отменено`/`Ошибка`/`Завершено`) | PASS | Auto PASS: `MainFormCoreRegressionTests.SR06_StatusTransitions_AreApplied` |
| SR-07 | Статус-колонка (иконка+фон+текст) | Manual | Визуал соответствует статусу, высота рабочих строк корректна | PASS | Auto PASS: `MainFormCoreRegressionTests.SR07_StatusCellVisuals_AreRegistered_AndRowHeightIsStable` |
| SR-08 | Фильтр по статусам | Manual | Отбор по статусам корректно скрывает/показывает строки | PASS | Auto PASS: `MainFormCoreRegressionTests.SR08_SR09_SR10_Filters_WorkForStatusUserOrderAndDates` (status branch) |
| SR-09 | Фильтр по пользователям | Manual | Список пользователей берется из `users.json`/cache, фильтрация корректна | PASS | Auto PASS: `verify_users_directory` + `MainFormCoreRegressionTests` (user filter + source/cache) |
| SR-10 | Фильтр по номеру/датам | Manual | Поиск номера и фильтры дат работают без ложных срабатываний | PASS | Auto PASS: `MainFormCoreRegressionTests.SR08_SR09_SR10_Filters_WorkForStatusUserOrderAndDates` (order/date branches) |
| SR-11 | StatusStrip индикаторы | Manual | Статус/прогресс/счетчики/диск/алерты обновляются корректно | PASS | Auto PASS: `MainFormCoreRegressionTests.SR11_TrayIndicators_UpdateStatsConnectionDiskErrorsAndProgress` + `MainFormSmokeTests` |
| SR-12 | Offline fallback (NAS недоступен) | Manual | Пользователи подтягиваются из cache, UI показывает offline режим | PASS | Auto PASS: `verify_users_directory` + `MainFormCoreRegressionTests.SR12_UsersDirectory_UsesCache_WhenSourceUnavailable` |

## 4. Gate для закрытия P3

`P3` закрывается, если одновременно:
1. Все пункты `SR-01...SR-12` имеют статус `PASS`.
2. Нет блокирующих дефектов уровня `P0/P1`.
3. Результаты прогона зафиксированы в этом файле и в основном плане этапа 1.

## 5. Техническая отметка закрытия

- На старте чеклиста кодовая база уже приведена к typed-контрактам `status/stage/column IDs`.
- Автопроверка `SR-01` выполнена и зафиксирована.
- Для `SR-02` выполнен базовый запуск приложения (`dotnet run --project Replica.csproj --no-build`) без аварийного завершения.
- Дополнительно подтверждено чтение истории: путь из `settings.json` доступен, `history.json` корректно парсится как JSON-массив.
- Для `SR-09/SR-12` выполнен автоматический тех-прогон `UsersDirectoryService` (source -> cache -> fallback), результаты положительные.
- Для `SR-03/SR-04/SR-05` выполнен автоматический тех-прогон `artifacts/verify_single_order_regression` (PASS, лог: `artifacts/verify_single_order_regression/last-run.log`).
- Добавлен тестовый проект `tests/Replica.UiSmokeTests` (`FlaUI + core regression`) и выполнен прогон `dotnet test`: `9/9 PASS`.

## 6. Итог

`P3` закрыт: все пункты `SR-01...SR-12` имеют статус `PASS`, блокирующих дефектов `P0/P1` не выявлено.

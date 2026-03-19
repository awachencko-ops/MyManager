# Этап 1: Regression Checklist (single + group)

Дата запуска чеклиста: 2026-03-16
Дата финальной актуализации: 2026-03-19
Статус: Completed

## 1. Цель

Подтвердить, что после миграции на новый `MainForm`:
1. single-order сценарии стабильны,
2. group-order сценарии стабильны,
3. hash-слой и архивная синхронизация не ломают рабочий цикл.

## 2. Область проверки

Проверяется финальный контур этапа 1:
- single-order (`SR-01...SR-12`),
- group/table stabilization (`SR-13...SR-27`),
- сборка и автопрогоны (`Verify` + `UiSmoke`).

## 3. Прогоны

| ID | Сценарий | Статус | Подтверждение |
|---|---|---|---|
| SR-01 | Сборка проекта | PASS | `dotnet build Replica.sln` -> `0 warnings`, `0 errors` (2026-03-19) |
| SR-02 | Старт приложения и загрузка истории | PASS | `MainForm` стартует, `history.json` читается |
| SR-03 | Создание simple заказа | PASS | Авто/тех-прогон + smoke |
| SR-04 | Редактирование simple заказа | PASS | Сохранение и перезагрузка истории |
| SR-05 | Операции файлов стадий | PASS | add/rename/delete по стадиям |
| SR-06 | Переходы статусов | PASS | `MainFormCoreRegressionTests.SR06_*` |
| SR-07 | Статус-колонка и высота строк | PASS | `MainFormCoreRegressionTests.SR07_*` |
| SR-08 | Фильтр по статусам | PASS | `MainFormCoreRegressionTests.SR08_*` |
| SR-09 | Фильтр по пользователям | PASS | `UsersDirectory` + `MainFormCoreRegressionTests` |
| SR-10 | Фильтр по номеру/датам | PASS | `MainFormCoreRegressionTests.SR08_SR09_SR10_*` |
| SR-11 | StatusStrip индикаторы | PASS | `MainFormCoreRegressionTests.SR11_*` |
| SR-12 | Offline fallback пользователей | PASS | `MainFormCoreRegressionTests.SR12_*` |
| SR-13 | Group expand/collapse | PASS | `MainFormCoreRegressionTests.SR13_*` |
| SR-14 | Browse-folder mismatch rule | PASS | `MainFormCoreRegressionTests.SR14_*` |
| SR-15 | Item selection disables container add-file | PASS | `MainFormCoreRegressionTests.SR15_*` |
| SR-16 | Reverse transition multi -> single | PASS | `MainFormCoreRegressionTests.SR16_*` |
| SR-17 | Корректный target удаления для item-row | PASS | `MainFormCoreRegressionTests.SR17_*` |
| SR-18 | Header group-row не зеркалит first item stage | PASS | `MainFormCoreRegressionTests.SR18_*` |
| SR-19 | Resolve order/item context | PASS | `MainFormCoreRegressionTests.SR19_*` |
| SR-20 | Zebra palette item-строк | PASS | `MainFormCoreRegressionTests.SR20_*` |
| SR-21 | Технический статус `Group` скрыт из UI-фильтров | PASS | `MainFormCoreRegressionTests.SR21_*` |
| SR-22 | Click-toggle group-row только при reselect | PASS | `MainFormCoreRegressionTests.SR22_*` |
| SR-23 | DragDrop item -> item без потери origin | PASS | `MainFormCoreRegressionTests.SR23_*` |
| SR-24 | DragDrop item -> single-order без потери group-состояния | PASS | `MainFormCoreRegressionTests.SR24_*` |
| SR-25 | Missing-file cell: red text only | PASS | `MainFormCoreRegressionTests.SR25_*` |
| SR-26 | Quite Imposing cleanup | PASS | `MainFormCoreRegressionTests.SR26_*` |
| SR-27 | PitStop cleanup | PASS | `MainFormCoreRegressionTests.SR27_*` |

## 4. Техническая отметка финального прогона (2026-03-19)

1. `dotnet build Replica.sln` -> `0 warnings`, `0 errors`.
2. `dotnet test Replica.sln` -> `30/30 PASS`:
   - `tests/Replica.VerifyTests` -> `5/5 PASS`
   - `tests/Replica.UiSmokeTests` -> `25/25 PASS`
3. Smoke-контракт toolbar синхронизирован с текущим UI: ожидаемая кнопка `Добавить файл`.

## 5. Gate закрытия этапа 1

Этап 1 закрыт, так как одновременно выполнено:
1. Все пункты `SR-01...SR-27` имеют статус `PASS`.
2. Нет блокирующих дефектов уровня `P0/P1`.
3. Результат зафиксирован в этом файле и в основном плане этапа 1.

## 6. Итог

Миграционный regression-пакет этапа 1 закрыт полностью; single/group контур готов к переходу в LAN/PostgreSQL этап.

## 7. Финальная проверка полноты (2026-03-19)

### 7.1 Покрытие checklist

| Проверка | Статус | Примечание |
|---|---|---|
| `SR-01...SR-12` (single) | ✅ | Полностью закрыто |
| `SR-13...SR-27` (group/table/hash-stability) | ✅ | Полностью закрыто |
| Блокирующие дефекты `P0/P1` | ✅ | Не выявлены |

### 7.2 Технический gate

| Проверка | Статус | Значение |
|---|---|---|
| Сборка решения | ✅ | `dotnet build Replica.sln` -> `0 warnings`, `0 errors` |
| Тесты решения | ✅ | `dotnet test Replica.sln` -> `30/30 PASS` |
| Актуальность чеклиста | ✅ | Документ синхронизирован с фактическим прогоном |

Вывод: проверка полноты по regression-checklist пройдена.

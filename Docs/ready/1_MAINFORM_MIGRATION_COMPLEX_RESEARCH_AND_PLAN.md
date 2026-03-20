# Replica: миграция MainForm (финальный статус этапа 1)

Дата актуализации: 2026-03-19  
Статус этапа: **Completed**

## 1. Область и границы

1. Продукт в этом этапе: **prepress-manager** (заказы, статусы, файлы, история, логи).
2. Фокус этапа: завершить миграцию на новый `MainForm` и стабилизировать single/group-контур.
3. LAN-сервер, PostgreSQL и DBeaver: следующий этап, после закрытия этого документа.

## 2. Фактический результат миграции

1. Вход в приложение полностью через `MainForm`, legacy-форма из рабочего контура выведена.
2. `MainForm` декомпозирован на partial-блоки и domain-участки (`Core`, `FileOps`, `Filters`, `Views`).
3. Group-first модель закреплена в рабочем контуре: `single-order` = частный случай `group-order`.
4. Таблица заказов стабилизирована для single/group сценариев, включая expand/collapse и item-строки.
5. Добавлен hash-слой для файлов (`Source/Prepared/Print`) в `OrderData` и `OrderFileItem`.
6. Введён `FileHashService` (SHA-256) и инкрементальный backfill известных хешей на истории.
7. Синхронизация архива усилена: сопоставление не только по имени/размеру, но и по hash.
8. `OrderTopologyService` синхронизирует order/item пути, размеры и hash для single-кейса.
9. Typed-контракты (`WorkflowContracts`) закреплены для статусных, stage и column идентификаторов.
10. User-filter работает по фактическому `order.UserName` + `users.json` + cache/offline fallback.
11. StatusStrip, индикаторы подключения/диска/ошибок и прогресса работают стабильно.
12. Pipeline `OrderProcessor` стабилен для single и multi, включая cleanup PitStop/Imposing артефактов.
13. Toolbar-контракт актуализирован: кнопка `Добавить файл` в основном сценарии группы.
14. Сборка подтверждена: `dotnet build Replica.sln` -> `0 warnings`, `0 errors` (2026-03-19).
15. Регрессия подтверждена: `dotnet test Replica.sln` -> `30/30 PASS` (2026-03-19):
    - `tests/Replica.VerifyTests`: `5/5 PASS`
    - `tests/Replica.UiSmokeTests`: `25/25 PASS`

## 3. Закрытие рисков этапа 1

| ID | Статус | Результат |
|---|---|---|
| R1 | Closed | Крупные блоки `MainForm` декомпозированы, бизнес-логика вынесена в профильные partial/сервисы |
| R2 | Closed | Фильтрация пользователей работает по реальным данным заказа и покрыта тестами |
| R3 | Closed | Group-order UI стабилен и покрыт регрессией (`SR13+`) |
| R4 | Closed | Строковые контракты централизованы в typed-константах (`WorkflowContracts`, operation/source names) |
| R5 | Closed | Неоднозначность order/item путей снижена: нормализация topology + синхронизация path/size/hash |
| R6 | Closed | Snapshot + smoke-контур внедрён и стабилен |
| R7 | Monitoring | Точечные текстовые/кодировочные артефакты отслеживаются, блокирующих дефектов нет |
| R8 | Transferred | LAN feature-gate и серверный режим официально перенесены в следующий этап (`2` и `3`) |

## 4. План этапа 1 (финальный)

| ID | Задача | Статус |
|---|---|---|
| P1 | User-filter (`users.json` + cache + offline fallback) | Completed |
| P2 | Typed-контракты `status/stage/column` | Completed |
| P3 | Single-order regression baseline | Completed |
| P4 | Автотесты (`Verify` + `UiSmoke`) | Completed |
| P5 | Release baseline stage-1 документации | Completed |
| P6 | Hash-слой и hash-ориентированная синхронизация архива | Completed |
| P7 | Стабилизация таблицы single/group и group UI регрессий | Completed |
| P8 | Финальный прогон и фиксация статуса миграции | Completed |

## 5. Definition of Done (факт закрытия)

Этап `MainForm migration` считается закрытым, так как одновременно выполнено:
1. Все пункты `P1...P8` имеют статус `Completed`.
2. Нет блокирующих дефектов (`P0/P1`) по regression-пакету этапа.
3. Технический baseline подтверждён (`build 0/0`, `tests 30/30 PASS`, дата: 2026-03-19).
4. Документация этапа актуализирована и готова к передаче в LAN/PostgreSQL трек.

## 6. Передача на следующий этап

Дальнейшая работа выполняется в следующем порядке:
1. `Docs/ready/2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`
2. `Docs/ready/3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`

## 7. Финальная проверка полноты (2026-03-19)

### 7.1 Ключевые риски

| Проверка | Статус | Примечание |
|---|---|---|
| `R1` декомпозиция MainForm | ✅ | Выполнено |
| `R2` user-filter по фактическим данным | ✅ | Выполнено |
| `R3` стабильность group UI | ✅ | Выполнено (`SR13+`) |
| `R4` string-контракты -> typed-контракты | ✅ | Выполнено |
| `R5` согласование order/item путей + hash | ✅ | Выполнено |
| `R8` LAN feature-gate для следующего этапа | ✅ | Передано в этап 2/3 официально |

### 7.2 Технический gate

| Проверка | Статус | Значение |
|---|---|---|
| Сборка решения | ✅ | `dotnet build Replica.sln` -> `0 warnings`, `0 errors` |
| Тесты решения | ✅ | `dotnet test Replica.sln` -> `30/30 PASS` |
| Документация этапа 1 | ✅ | Актуализирована |

Вывод: для этапа 1 все ключевые риски закрыты/переданы, проверка полноты пройдена.


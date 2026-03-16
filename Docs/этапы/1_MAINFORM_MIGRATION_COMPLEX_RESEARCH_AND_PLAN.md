# Replica: строгий план на текущий момент

Дата актуализации: 2026-03-16

## 1. Область и границы

1. Продукт на текущем этапе: **prepress-manager** (подготовка заказов, статусы, файлы, история, логи).
2. Мониторинг физической печати/спулера: **вне scope**.
3. Текущий фокус: завершить `single-first` миграцию и стабилизировать новый `MainForm`.

## 2. Статус по факту

### 2.1 Сделано

1. Приложение запускается через `MainForm`, legacy `Forms/Archive` исключен из сборки.
2. `MainForm` декомпозирован по partial-файлам и смысловым папкам.
3. Рабочий pipeline `OrderProcessor` (PitStop/Imposing/timeout/progress) стабилен.
4. Внедрена group-first модель (`single-order`/`group-order`) в backend и историю (терминология обновлена для этапного трека).
5. StatusStrip и нижние индикаторы работают (статус, прогресс, счетчики, связь, диск, алерты).
6. Статус-колонка в таблице заказов оформлена (иконка + фон + текст), строки подогнаны под квадратный блок статуса.
7. NAS-cutover выполнен для рабочих путей, temp-структуры и конфигов.
8. `qihot4.xml` экспортирован с NAS-путями, backup сделан.
9. Ошибка `Duplicate ... Attribute` устранена исключением `artifacts/**` из компиляции.
10. Сборка: `0 warnings`, `0 errors`.
11. Реальный user-filter подключен к `users.json` (source + cache + offline fallback + индикатор состояния в UI).
12. Введены typed-контракты `status/stage/column IDs` и убраны string-магии в `MainForm`/`UI`/core-сервисах.
13. Финальный single-regression (`P3`) закрыт по чеклисту `SR-01...SR-12` (автоматизированный прогон).
14. Зафиксирован release baseline этапа `single-first`: `Docs/ready/1_SINGLE_FIRST_RELEASE_BASELINE_2026-03-16.md`.
15. `P4` закрыт: добавлен snapshot-слой `Verify` (`tests/Replica.VerifyTests`, `5/5 PASS`) и UI/core smoke (`tests/Replica.UiSmokeTests`, `9/9 PASS`).
16. Закрыт `R1`: вынесен `OrderGridLogic` и выполнена дополнительная декомпозиция `MainForm.Filters.Popups` на отдельные partial для created/received фильтров дат.
17. Закрыт `R2`: user-filter подтвержден на фактическом `order.UserName` + source/cache/fallback (`users.json`) и покрыт smoke-тестами.
18. Закрыт `R3`: минимальный group-order UI стабилизирован и подтвержден регрессионными тестами `SR13/SR14/SR15` (expand/collapse, browse-folder mismatch, container-level actions).

### 2.2 В работе

1. Риск-трек этапа 1: точечная стабилизация по `R4/R5/R8` (`R1/R2/R3` закрыты).
2. Рабочий документ риск-трека: `Docs/ready/1_STAGE1_RISK_TRACK_AFTER_P_CLOSE.md`.
3. `R4` стартован: выполняется дочистка остаточных string-контрактов в активном `MainForm`-контуре; вынесены operation-коды order-лога в `OrderOperationNames` и source-контракты статусов в `OrderStatusSourceNames`.

### 2.3 Не начато

1. Полноценный group-order UI (group/item визуализация и item-level действия).
2. Этап 2: `2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`.

## 3. Ключевые риски (без изменений)

| ID | Приоритет | Статус | Наблюдение | Риск |
|---|---|---|---|---|
| R1 | Средний | Closed | `MainForm` дополнительно декомпозирован (`OrderGridLogic`, split popup partials) | Снижен: aggregate уменьшен, логика вынесена |
| R2 | Высокий | Closed | User-filter привязан к фактическому `order.UserName` + `users.json` source/cache/fallback | Снижен: ложные совпадения устранены |
| R3 | Средний | Closed | Минимальный group-order UI внедрен и закреплен тестами `SR13/SR14/SR15` | Снижен: multi-order сценарий выполняется без переключения на legacy |
| R4 | Средний | Open | Есть string-контракты (имена колонок, статусы, маркеры) | Хрупкость при рефакторинге |
| R5 | Средний | Open | Остатки legacy-подхода: order-level пути плюс item-level пути | Риск расхождений данных |
| R6 | Низкий | Closed | Snapshot + smoke слой внедрен (`Verify` + `FlaUI`) | Базовые регрессии покрыты автоматически |
| R7 | Низкий | Open | Остаточные проблемы кодировки отдельных UI-строк | UX-шум и поддерживаемость |
| R8 | Средний | Open | LAN-контур формально не закреплён как отдельный feature-gate | Непредсказуемость при доступе к сетевым папкам |

## 4. Строгий план выполнения (по порядку)

| ID | Задача | Статус | Результат на выходе | Критерий закрытия |
|---|---|---|---|---|
| P1 | Реальный user-filter из `users.json` + кеш + offline fallback | Completed | Пользовательский фильтр из сети с безопасной деградацией | При отключении NAS фильтр продолжает работу из кеша, UI показывает offline |
| P2 | Typed-контракты для `status/stage/column IDs` | Completed | Убраны критичные string-магии в core потоке | Основные сценарии используют централизованные константы/enum |
| P3 | Финальный single-regression | Completed | Закрыт чеклист `1_SINGLE_ORDER_REGRESSION_CHECKLIST.md` | Нет блокирующих дефектов (`P0/P1`) |
| P4 | Минимум автотестов (`Verify` + `FlaUI`) | Completed | Snapshot + UI smoke база | Выполнено: `Verify` 5/5 + `FlaUI` 9/9 |
| P5 | Фиксация релизного baseline по single-first | Completed | Release note + known issues + актуальная документация | Baseline зафиксирован в `Docs/ready/1_SINGLE_FIRST_RELEASE_BASELINE_2026-03-16.md` |

## 5. Приоритет на ближайший цикл

1. Выполнять риск-трек этапа 1 строго по порядку: `R4 -> R5 -> R8` (`R1/R2/R3` закрыты).
2. После фиксации рисков перейти к этапу `2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`.

## 6. Зафиксированные решения

1. Внешние референсы для текущей реализации: только `FlaUI` и `Verify`.
2. `pdfdroplet`, `Rmg.PdfPrinting`, `PrintManager` исключены из практического плана.
3. UI-библиотеки (`DockPanelSuite`, `ObjectListView`) не внедрять до закрытия стабилизации.
4. Печатный spooler-мониторинг не внедрять: продукт остается в prepress-scope.

## 7. Definition of Done для текущего этапа

Этап `migration on MainForm (single-first)` считается завершенным, когда одновременно выполнены:
1. `P1`, `P2`, `P3` закрыты.
2. Нет блокирующих дефектов по single-checklist.
3. Документация и baseline зафиксированы (включая этот файл).

---

Статус документа: рабочий, строгий, используется как основной трекер текущего этапа.

Этап 1 (`single-first`) закрыт по треку `P1...P5`.


## 8. Передача на следующий этап

После закрытия этапа 1 работа идёт по очереди:
1. `2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`
2. `3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`

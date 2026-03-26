# Replica: Release Baseline (Single-First)

Дата фиксации baseline: 2026-03-16  
Этап: `MainForm migration (single-first)`  
Статус baseline: `Approved`

## 1. Область baseline

Baseline покрывает только prepress-сценарии этапа `single-first`:
- запуск через новый `MainForm`,
- single-order поток (модель group-order из одного item),
- файловые стадии `in/prepress/print`,
- статусы, фильтры, StatusStrip,
- user directory (`users.json` + cache + offline fallback),
- NAS-ориентированная конфигурация хранения.

Вне baseline:
- мониторинг физической печати/спулера,
- полнофункциональный multi-order UI.

## 2. Что вошло в релиз

1. Legacy `Forms/Archive` исключен из сборки; вход только через `MainForm`.
2. `MainForm` декомпозирован на partial-структуру по доменам.
3. Typed-контракты (`status/stage/column IDs`) внедрены в критический поток.
4. Реальный user-filter подключен к `users.json` с cache/fallback.
5. StatusStrip и нижние индикаторы стабилизированы (статус, прогресс, счетчики, связь, диск, алерты).
6. Статус-ячейка: иконка + фон + текст; высота рабочих строк синхронизирована.
7. NAS cutover и конфигурационные пути актуализированы.
8. Single-regression чеклист `SR-01...SR-12` закрыт.

## 3. Валидация baseline

1. `dotnet build Replica.csproj` -> `0 warnings`, `0 errors`.
2. `dotnet test tests\Replica.UiSmokeTests\Replica.UiSmokeTests.csproj` -> `9/9 PASS`.
3. Regression-checklist: `Docs/ready/1_SINGLE_ORDER_REGRESSION_CHECKLIST.md` -> `Completed`, все `SR-01...SR-12 = PASS`.
4. `dotnet test tests\Replica.VerifyTests\Replica.VerifyTests.csproj` -> `5/5 PASS`.

## 4. Known Issues (non-blocking)

1. `R3` Multi-order backend есть, UI пока single-first.
2. `R4` Остались точечные string-контракты вне критического потока.
3. `R6` Снимковое покрытие есть, но расширение snapshot-набора потребуется на следующих этапах.
4. `R8` LAN-контур не оформлен как отдельный feature-gate.

Критических дефектов `P0/P1` в рамках этапа `single-first` не выявлено.

## 5. Решение по выпуску

`GO` для эксплуатации этапа `single-first` в рамках prepress-scope.

Условия:
1. Использовать только сценарии single-first.
2. Multi-order считать следующим этапом разработки.
3. Дальнейшие этапы выполнять без возврата legacy-форм.

## 6. Дальнейшие шаги

1. Перейти к `Docs/ready/2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`.
2. Параллельно вести риск-трек этапа 1 (`R1/R2/R3/R4/R5/R8`).


<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.
## 1. Цель
Снять текущие лаги в `OrdersWorkspace` и убрать архитектурную связность UI <-> orchestration так, чтобы:
- форма не блокировалась при сетевых и файловых операциях;
- команды/запросы масштабировались без разрастания `OrdersWorkspaceForm`;
- переход на серверный режим (LAN PostgreSQL + API-first) был устойчивым и наблюдаемым.

## 2. Факты из текущего кода
Ниже не гипотезы, а конкретные точки, которые сейчас создают тормоза и архитектурный долг.

### 2.1 Блокировки UI-потока
- Синхронное ожидание async в форме:
  - `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:228` (`GetAwaiter().GetResult()`).
- Синхронные TCP wait в статус-логике:
  - `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.StatusStrip.cs:978` (`Wait(350ms)`),
  - `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.ServerHardLock.cs:81` (`Wait(450ms)`).
- Синхронный HTTP для справочника пользователей:
  - `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.UsersDirectory.cs:132-145` (`SendAsync(...).GetResult()`, `ReadAsStringAsync(...).GetResult()`).

### 2.2 Частые тяжелые UI-операции
- Полная перестройка таблицы (`Rows.Clear + repopulate`) на многие события:
  - `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:209-259`.
- Поиск в таблице без debounce:
  - `Features/Orders/UI/OrdersWorkspace/OrdersWorkspaceForm.cs:748` (`TextChanged => RebuildOrdersGrid()`).
- На каждый `RebuildOrdersGrid` запускается каскад тяжелых обновлений:
  - `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:89-106`.

### 2.3 Лишние O(n) / O(n^2) проходы
- В фильтрации и плитках много `FindOrderByInternalId(...)` на каждую строку:
  - `Features/Orders/UI/OrdersWorkspace/Filters/OrdersWorkspaceForm.Filters.Evaluation.cs:207`,
  - `Features/Orders/UI/OrdersWorkspace/Views/OrdersWorkspaceForm.PrintTiles.cs:513`.
- На больших списках это быстро превращается в заметные задержки интерфейса.

### 2.4 Тяжелые операции в циклах статуса
- Индексация архива через рекурсивный `Directory.EnumerateFiles(..., AllDirectories)`:
  - `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.ArchiveSync.cs:337`.
- Подогрев представления считает SHA256 по сериализованной сигнатуре истории:
  - `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.OrdersLifecycle.cs:47-86`.

### 2.5 Масштаб слоя UI
- `Features/Orders/UI/OrdersWorkspace` сейчас: `30` файлов, примерно `16616` строк.
- Самые тяжелые файлы:
  - `OrdersWorkspaceForm.StatusStrip.cs` (`1946` строк),
  - `OrdersWorkspaceForm.OrdersLifecycle.cs` (`1643` строк),
  - `OrdersWorkspaceForm.PrintTiles.cs` (`1251` строк),
  - `OrdersWorkspaceForm.cs` (`1071` строк).
- Это подтверждает риск god-object и высокую стоимость изменений.

## 3. Нужен ли Mediator именно здесь
Коротко: **да, нужен**, но как часть плана, а не как "магическая таблетка" от лагов.

### Что даст Mediator в Replica
- Уберет прямую связность формы с большим фасадом orchestration (`IOrderApplicationService`).
- Введет явные `Command/Query` границы вместо "комбайна" сервиса.
- Даст pipeline для кросс-сечений: авторизация/actor, idempotency, telemetry, retry-policy hooks.
- Упростит дальнейшую модульную декомпозицию (Application/Domain/Infrastructure/UI).

### Что Mediator не решит сам
- Он **не устраняет** синхронные блокировки UI автоматически.
- Он **не ускоряет** таблицу, если оставить текущий pattern полного `Rebuild`.
- Он добавляет слой абстракции, который нужно внедрять поэтапно.

Вывод: сначала фиксируем P0 производительности, затем вводим Mediator пошагово, не ломая рабочий контур.

## 4. Рекомендованная целевая архитектура (гибрид)
Структура под ваш сценарий:

- `Features/Orders/UI/`
  - формы и UI-адаптеры (тонкие, без orchestration-логики).
- `Features/Orders/Application/`
  - `Commands/Queries/Notifications` + handlers;
  - use-case orchestration через Mediator.
- `Features/Orders/Domain/`
  - правила статусов, инварианты single/group, topology/business rules.
- `Features/Orders/Infrastructure/`
  - PostgreSQL/EF adapters, file/NAS adapters, HTTP adapters.
- `SharedKernel/` (внутри `Replica.Shared` или отдельный проект)
  - минимальные стабильные примитивы: `Result<T>`, базовые ошибки, контракты.

Правило зависимостей:
- `UI -> Application`
- `Application -> Domain + Abstractions`
- `Infrastructure -> Application/Domain contracts`
- `Domain` не зависит от `UI` и конкретных адаптеров.

## 5. План внедрения в 8 итераций
Ниже план "без остановки завода", с приоритетом на отзывчивость.

### Итерация 1 (P0): baseline и метрики
- Ввести замеры:
  - длительность `RebuildOrdersGrid`;
  - время реакции кнопок Start/Stop/Create/Delete;
  - count UI-freeze > 100ms.
- Зафиксировать baseline в `docs`.

### Итерация 2 (P0): убрать sync-over-async в UI
- Перевести `GetResult/Wait` в async flow:
  - `OrdersWorkspaceForm.cs:228`,
  - `StatusStrip.cs:978`,
  - `ServerHardLock.cs:81`,
  - `UsersDirectory.cs:132-145`.
- Все сетевые проверки только async + cancel + timeout.

### Итерация 3 (P0): разгрузить rebuild таблицы
- Добавить debounce на поиск (`150-250ms`).
- Ввести "dirty flags" и частичное обновление строк вместо полного `Rows.Clear()`.
- Разнести тяжелые post-rebuild шаги на отдельные условные refresh.

### Итерация 4 (P1): оптимизация lookup/caching
- Поддерживать индекс `Dictionary<internalId, OrderData>` для O(1) доступа.
- Убрать повторные `FindOrderByInternalId` в циклах фильтров/tiles.

### Итерация 5 (P1): archive/warmup в background
- Архивную индексацию выполнять в фоне с TTL/incremental режимом.
- Подогрев/хэш-сигнатуру запускать по изменению данных, а не по частому таймеру.

### Итерация 6 (P1): bootstrap Mediator
- Подключить:
  - `Mediator.SourceGenerator` (outer app),
  - `Mediator.Abstractions` (message/handler слой).
- Добавить минимальный DI root (`ServiceCollection`) и `AddMediator`.
- Первый вертикальный срез: `StartOrderCommand`.

### Итерация 7 (P1): write-boundary на commands
- Перевести write use-cases:
  - create/update/delete order,
  - add/update/delete/reorder item,
  - run/stop.
- Добавить pipeline behaviors:
  - actor validation,
  - idempotency key/fingerprint,
  - logging/metrics.

### Итерация 8 (P2): query-boundary и демонтаж legacy-фасада
- Перевести read paths на queries.
- Сузить/разбить `IOrderApplicationService` до thin-adapter либо убрать.
- Финальный аудит зависимостей и latency.

## 6. Потоки и синхронность: обязательные правила
Чтобы больше не ловить "подвисания":

- UI-поток:
  - только render + простая state mutation.
- I/O/CPU:
  - только async/background, с `CancellationToken`.
- Таймеры:
  - не допускают re-entry (guard-флаг),
  - не выполняют тяжелую работу прямо в Tick.
- HTTP к API:
  - единый `HttpClient`, timeout, retry policy на инфраструктурном слое.
- Любой write в LAN-режиме:
  - строго API-first, локальные fallback изменения запрещены.

## 7. KPI и критерии готовности
Минимальные целевые показатели:

- UI:
  - p95 `RebuildOrdersGrid` < `120ms`,
  - нет блокировок UI > `250ms` в типовых сценариях.
- API/write:
  - p95 start/stop/create/update/delete < `500ms` без внешней обработки файлов.
- Устойчивость:
  - при недоступности API все write-операции блокируются и явно объясняются пользователю.
- Архитектура:
  - UI не знает про конкретные процессоры/репозитории напрямую, только про `ISender/IMediator`.

## 8. Риски и контрмеры
| Риск | Вероятность | Влияние | Контрмера |
|---|---|---|---|
| Миграция на Mediator затянется из-за большого фасада | Средняя | Высокое | Вертикальные срезы (по use-case), не "big bang" |
| Регрессии в write-scenarios | Средняя | Высокое | Contract + integration regression pack для single/group |
| Лаги останутся после частичной оптимизации | Средняя | Среднее | Сначала P0 фиксы UI, только потом архитектурный слой |
| Непрозрачные ошибки API | Средняя | Среднее | Единый error contract + tray/status telemetry |

## 9. Рекомендация (go/no-go)
`Go`, но в последовательности:
1. Сначала Iteration 1-3 (перфоманс и UI-blocking).
2. Затем Iteration 6-8 (Mediator и clean command/query boundary).

Если сделать наоборот (сначала Mediator), архитектура станет чище, но пользователю все равно будет "тупить".

## 10. Источники
- Mediator (официальный репозиторий): <https://github.com/martinothamar/Mediator>
- Mediator.SourceGenerator (NuGet, актуальная версия): <https://www.nuget.org/packages/Mediator.SourceGenerator/>

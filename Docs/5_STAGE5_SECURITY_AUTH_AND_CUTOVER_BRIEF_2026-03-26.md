# Этап 5: Security/Auth boundary, cutover и production hardening

Дата актуализации: 2026-03-26  
Статус: Completed

## 1. Контекст этапа

К моменту старта этапа 5 в проекте уже реализованы:

1. `Replica.Api` как HTTP boundary для LAN-контура.
2. PostgreSQL + EF Core migrations + автоприменение миграций.
3. `orders`, `order_items`, `order_events`, `users`, `storage_meta`, `order_run_locks`.
4. Клиентские HTTP-gateway для write/run/stop сценариев.
5. Базовая actor-validation на write-path через `X-Current-User`.

Следовательно, этап 5 не является стартом клиент-серверной миграции с нуля. Его цель - довести уже внедренный LAN/API-контур до production-grade security и управляемого cutover.

## 2. Архитектурная цель этапа 5

Сделать `Replica.Api` единственной доверенной точкой управления заказами, пользователями и правами доступа, при этом:

1. убрать разрозненную проверку actor identity из отдельных контроллеров;
2. ввести единый current-user/auth context для API;
3. использовать роли (`Admin`, `Operator`) как серверное правило, а не клиентскую договоренность;
4. завершить cutover away from legacy file truth, оставляя файловый режим только как controlled fallback/rollback path;
5. сохранить совместимость с текущим WinForms-клиентом и LAN-режимом без резкого усложнения эксплуатации.

## 3. Что корректируется относительно исходного брифа

### 3.1 Что принимается без изменений

1. Централизация состояния заказов в PostgreSQL.
2. API как единственная точка входа.
3. Асинхронный клиент через `HttpClient`.
4. Миграция legacy JSON-данных в базу.
5. Запрет прямой клиентской логики по знанию сетевых путей и расположения файлов.

### 3.2 Что меняется по приоритету

1. `JWT` не ставится в критический путь первого security-среза.
   На этапе 5 приоритетнее единый server-side identity boundary на основе уже существующего `X-Current-User`/encoded header + активных пользователей + ролей.
2. `Dual-Write` допускается только как короткий переходный режим.
   Долгоживущий режим `JSON + DB` считается риском расхождения источников истины.
3. `LISTEN/NOTIFY`, Redis Pub/Sub и реактивные шины не входят в первый обязательный срез этапа 5.
   Сначала закрываются auth/authz, cutover, audit, backup, rollback и эксплуатационная управляемость.
4. Бизнес-события и технические логи должны разделяться.
   `order_events` остаётся журналом бизнес-операций, а техническая телеметрия/ошибки должны эволюционировать в отдельный `app_events`/observability контур.

## 4. Целевая модель безопасности

## 4.1 Identity boundary

1. Для защищенных API-endpoints сервер обязан резолвить current user из `X-Current-User` или `X-Current-User-Base64`.
2. Identity resolution должна быть вынесена в единый API-layer service/middleware/filter, а не повторяться в контроллерах.
3. Если users-directory заполнен, actor должен существовать в серверном списке пользователей и быть активным.
4. В bootstrap/rollback-сценариях допустим controlled compatibility mode через конфиг, но не silent bypass.

## 4.2 Authorization boundary

Роли на старте этапа 5:

1. `Admin`
2. `Operator`

Стартовое распределение прав:

1. `Operator`
   - чтение заказов;
   - run/stop;
   - create/update/delete order/item;
   - чтение каталога пользователей, если это требуется текущему клиенту.
2. `Admin`
   - все права `Operator`;
   - управление пользователями и ролями;
   - административные diagnostics/maintenance endpoints;
   - переключение strict modes и эксплуатационные операции.

Примечание:
`GET /api/users` на первом срезе допускается для `Operator`, потому что текущий WinForms-клиент использует его для users directory. Перевод в `Admin-only` возможен позже, когда клиент перестанет зависеть от этого endpoint как от рабочего каталога.

## 5. Целевая модель данных этапа 5

Таблица `users` должна быть доведена до минимально боевой модели:

1. `user_name` - логический ключ;
2. `role` - серверная роль (`Admin` / `Operator`);
3. `is_active`;
4. `updated_at`.

На текущем этапе полноценные password hashes и login flow могут быть отложены до этапа 6, если LAN-контур пока использует trusted environment + header identity.

## 6. Приоритеты реализации

### P1. Unified auth context

1. Вынести resolve/validate actor logic в единый reusable слой.
2. Убрать дублирование проверки actor из контроллеров.
3. Ввести declarative authorization для контроллеров/endpoint groups.

### P2. Role persistence

1. Протянуть `Role` через EF entities, migrations и PostgreSQL store.
2. Перестать подменять server users значением `Operator` по умолчанию при чтении из БД.

### P3. Client compatibility

1. Все клиентские запросы к защищённым endpoint должны отправлять current actor.
2. В первую очередь - `GET /api/users`, затем остальные read endpoints по мере их перевода под auth boundary.

### P4. Controlled cutover

1. Зафиксировать дату/критерии выключения долгоживущего dual-write.
2. Оставить rollback-путь документированным и ограниченным по времени.
3. Явно определить source of truth для каждого режима (`FileSystem`, `LanPostgreSql`, `Api`).

### P5. Audit/observability split

1. Сохранить `order_events` как бизнес-журнал.
2. Подготовить следующий шаг к выделению технических событий в отдельный контур (`app_events`, structured diagnostics, retention).

## 7. Не входит в критический путь этапа 5

1. Полноценный JWT bearer login flow.
2. Refresh tokens / external identity provider.
3. Redis Pub/Sub.
4. PostgreSQL `LISTEN/NOTIFY` как обязательный механизм очередей.
5. Полный toolbox orchestration для всех внешних Python-утилит.

Эти темы остаются допустимыми следующими шагами, но не должны блокировать production hardening текущего API.

## 8. Рабочий план этапа 5

| ID | Шаг | Результат |
|---|---|---|
| E5-P1 | Stage 5 brief + backlog alignment | Документ и кодовая траектория синхронизированы |
| E5-P2 | Unified current-user context | Защищенные endpoints используют единый identity resolver |
| E5-P3 | Role persistence in DB | `users.role` хранится и читается сервером |
| E5-P4 | Role-based authorization | Контроллеры защищены по ролям |
| E5-P5 | Client compatibility patch | Клиент отправляет actor в защищенные read endpoints |
| E5-P6 | Regression + rollout notes | Тесты и короткий cutover/runbook baseline обновлены |

## 9. Риски и контрмеры

1. Регрессия клиента после закрытия read-endpoints.
   Контрмера: сначала адаптировать `GET /api/users` в клиенте, затем включать защиту endpoint.
2. Потеря роли при чтении из PostgreSQL.
   Контрмера: отдельная миграция `users.role` + тесты на roundtrip role.
3. Скрытые bypass-режимы.
   Контрмера: compatibility mode только через явный config flag и с предупреждающим логированием.
4. Затянувшийся dual-write.
   Контрмера: отдельный cutover checkpoint и эксплуатационное решение о выключении legacy truth.
5. Смешение audit и technical logging.
   Контрмера: фиксировать назначение `order_events` и не использовать его как универсальный лог-файл в БД.

## 10. Definition of Done этапа 5

Этап 5 считается закрытым, когда одновременно выполнено:

1. Защищенные API endpoints используют единый current-user/auth слой.
2. Сервер валидирует активность пользователя и его роль централизованно.
3. `users.role` хранится в PostgreSQL и доступна в API/store.
4. WinForms-клиент совместим с новым auth boundary как минимум по `GET /api/users` и текущим write/run endpoints.
5. Regression pack покрывает auth/role сценарии и проходит без `P0/P1` дефектов.

## 11. Следующий шаг после этапа 5

После закрытия этапа 5 можно переходить к одному из двух направлений:

1. Stage 6A: полноценный authN (JWT/API keys, password hashes, login flow, token lifetime).
2. Stage 6B: orchestration/runtime layer (toolbox API, queue orchestration, audit split, reactive signaling).

## 12. Closure Notes (2026-03-26)

1. Auth cutover выполнен: `ReplicaApi:Auth:Mode` переведён в `Strict`.
2. Legacy strict-flag синхронизирован: `ReplicaApi:StrictActorValidation=true`.
3. Живой API подтверждает режим: `GET /live` возвращает `authMode: "Strict"`.
4. Проверка доступа подтверждена:
   - известный активный actor -> `200`;
   - неизвестный actor -> `403`.
5. Операционный checklist зафиксирован:
   - `Docs/ready/5_STRICT_AUTH_CUTOVER_CHECKLIST_2026-03-26.md`.

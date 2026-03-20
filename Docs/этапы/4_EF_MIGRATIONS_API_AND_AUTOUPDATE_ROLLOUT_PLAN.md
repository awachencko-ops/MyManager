# Этап 4: EF migrations, API endpoints и автообновление клиента

Дата актуализации: 2026-03-20
Статус: In progress (этап 3 закрыт, этап 4 выполняется)

## 1. Цель этапа

Завершить технический контур после архитектурного разделения:
1. Поднять EF Core + миграции PostgreSQL.
2. Реализовать рабочие API endpoints для пользователей и заказов.
3. Закрыть аудит изменений и контроль конкурентности.
4. Запустить автообновление WinForms-клиента в LAN (без ручных обновлений на 5 ПК).

## 2. EF Core и миграции (PostgreSQL)

### 2.0 Что уже сделано (факт 2026-03-20)

1. Добавлен `ReplicaDbContext` в `Replica.Api`.
2. Добавлены entity mappings для `orders`, `order_items`, `order_events`, `users`, `storage_meta`.
3. Добавлена baseline migration: `20260320000100_BaselineSchema` (idempotent SQL).
4. Добавлена миграция `20260320000200_OrderRunLocks` для server-side `run/stop` lock-таблицы.
5. На старте API (PostgreSQL mode) выполняется `Database.Migrate()`.
6. `ILanOrderStore` переведён на `EfCoreLanOrderStore` (PostgreSQL mode), in-memory оставлен как fallback.
7. Реализованы endpoints `POST /api/orders/{id}/run` и `POST /api/orders/{id}/stop` с optimistic concurrency и `409 Conflict` при активном запуске.
8. Клиентский `MainForm` в режиме `LanPostgreSql` отправляет `run/stop` через сервисный слой (`LanRunCommandCoordinator` -> `LanOrderRunApiGateway`).
9. Для write-endpoints включена обязательная actor validation (`X-Current-User`, проверка активного пользователя при непустой users-directory).
10. Введён request-level `X-Correlation-Id` middleware + structured request logging scope.
11. Добавлены и проходят тесты на actor validation и middleware correlation (`Replica.VerifyTests`).

### 2.1 Что ещё нужно внедрить

1. Финализировать migration/rollback runbook для эксплуатации.
2. Подтвердить индексы/ограничения на целевой БД (включая `unique(order_id, sequence_no)`, `check(sequence_no > 0)`).

## 3. API endpoints (минимум прод-готовности)

### 3.1 Users

1. `GET /users` — список сотрудников для Login.

### 3.2 Orders

1. `GET /orders?createdBy=...` — список с фильтрацией по автору.
2. `GET /orders/{id}` — карточка контейнера + items.
3. `POST /orders` — создать контейнер заказа.
4. `PATCH /orders/{id}` — обновить контейнер.
5. `POST /orders/{id}/items` — добавить item в заказ.
6. `PATCH /orders/{id}/items/{itemId}` — обновить item.
7. `POST /orders/{id}/items/reorder` — изменение порядка файлов.
8. `POST /orders/{id}/run` — серверный старт обработки заказа.
9. `POST /orders/{id}/stop` — серверная остановка обработки заказа.

### 3.3 Требования к обработке

1. `X-Current-User` обязателен для write-path.
2. При наличии users-directory actor должен быть известным и активным.
3. Конфликт версий возвращает `409 Conflict`.
4. Ошибки валидации возвращают структурированный `400`.

## 4. Аудит и конкурентность

1. `order_events` наполняется на ключевых write/run/stop операциях.
2. Требуется финально зафиксировать эксплуатационный протокол аудита (какие события обязательны, какие проверяем на релизе).
3. В клиенте при 409 выполняется корректная обработка и refresh-сценарий.

## 5. Автообновление клиента (LAN)

### 5.1 Сервер (`Replica.Api`)

1. Папка: `wwwroot/updates/`.
2. Раздача статических файлов через `app.UseStaticFiles()`.
3. API bind на `0.0.0.0:5000`.
4. Артефакты:
   - `update.xml`;
   - `ReplicaClient.zip`;
   - `changelog.txt` (опционально).

### 5.2 Клиент (`Replica.Client`)

1. NuGet: `Autoupdater.NET.Official`.
2. Запуск проверки до `Application.Run(MainForm)`:
   - `AutoUpdater.Start("http://<IP_API>:5000/updates/update.xml")`.
3. Политика обновления:
   - для критичных релизов — mandatory;
   - для обычных — уведомление с подтверждением.

### 5.3 Релизный цикл

1. `dotnet publish` клиента (Release).
2. Упаковка publish в ZIP.
3. Обновление `update.xml` (version/url/changelog/mandatory).
4. Выкладка ZIP + XML в `wwwroot/updates` серверного ПК.
5. Проверка обновления на 1 пилотном клиенте, потом rollout на все 5 ПК.

## 6. Проверка готовности (чеклист)

1. Миграция применена, таблицы и индексы на месте.
2. `GET /users` и `GET /orders` работают в LAN.
3. Фильтр `createdBy` в WinForms отдаёт реальные данные.
4. При конкурентном конфликте API возвращает 409, клиент корректно реагирует.
5. `order_events` наполняется при изменениях.
6. `run/stop` проходит через `order_run_locks`, повторный `run` даёт `409 Conflict`.
7. Write-endpoints отклоняют запросы без валидного `X-Current-User`.
8. Ответы API содержат `X-Correlation-Id`.
9. Автообновление подхватывает новую версию по `update.xml`.

## 7. Definition of Done этапа 4

1. EF + миграции внедрены и воспроизводимы.
2. Минимальные endpoints пользователей и заказов работают стабильно.
3. Concurrency + audit подтверждены тестовыми сценариями.
4. Автообновление клиента реально работает в LAN.
5. Есть короткая эксплуатационная инструкция для релиза.

---

Связь с этапами:
- Вход: `3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`.
- Выход: рабочий цикл релизов и поддержка LAN-клиентов без ручной установки.
- Следующий этап: `5_INSTALLER_AND_DEPENDENCIES_PACKAGING_PLAN.md`.

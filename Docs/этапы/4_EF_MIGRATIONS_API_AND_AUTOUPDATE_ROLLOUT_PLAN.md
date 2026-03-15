# Этап 4: EF migrations, API endpoints и автообновление клиента

Дата актуализации: 2026-03-13
Статус: рабочий план этапа 4

## 1. Цель этапа

Завершить технический контур после Step 1 архитектурного разделения:
1. Поднять EF Core + миграции PostgreSQL.
2. Реализовать рабочие API endpoints для пользователей и заказов.
3. Включить аудит изменений и контроль конкурентности.
4. Запустить автообновление WinForms-клиента в LAN (без ручных обновлений на 5 ПК).

## 2. EF Core и миграции (PostgreSQL)

## 2.1 Что внедряем

1. `DbContext` в `Replica.Api`.
2. Конфигурации сущностей: `Order`, `OrderItem`, `OrderEvent`, `User`.
3. `Version` как concurrency token.
4. Индексы и ограничения:
   - `unique(order_id, sequence_no)`;
   - `check(sequence_no > 0)`;
   - индекс по `CreatedById`/`CreatedByUser`.

## 2.2 Миграции

1. Создать `InitialCreate`.
2. Прогнать миграцию на тестовой БД.
3. Проверить схему и индексы.
4. Подготовить rollback-скрипт (минимум для последней миграции).

## 3. API endpoints (минимум прод-готовности)

## 3.1 Users

1. `GET /users` — список сотрудников для Login.

## 3.2 Orders

1. `GET /orders?createdBy=...` — список с фильтрацией по автору.
2. `GET /orders/{id}` — карточка контейнера + items.
3. `POST /orders` — создать контейнер заказа.
4. `PATCH /orders/{id}` — обновить контейнер.
5. `POST /orders/{id}/items` — добавить item в заказ.
6. `PATCH /orders/{id}/items/{itemId}` — обновить item.
7. `POST /orders/{id}/items/reorder` — изменение порядка файлов.

## 3.3 Требования к обработке

1. Автор заказа берётся из `X-Current-User` и валидируется по `Users`.
2. Конфликт версий возвращает `409 Conflict`.
3. Ошибки валидации возвращают структурированный `400`.

## 4. Аудит и конкурентность

1. Включить перехват `SaveChanges`/pipeline для записи `order_events`.
2. Логировать операции insert/update/delete для `Order` и `OrderItem`.
3. Для каждого события сохранять:
   - `actor` (кто изменил),
   - `event_type`,
   - `payload` (jsonb),
   - timestamp.
4. В клиенте при 409 показывать понятное уведомление и перезагружать запись.

## 5. Автообновление клиента (LAN)

## 5.1 Сервер (`Replica.Api`)

1. Папка: `wwwroot/updates/`.
2. Раздача статических файлов через `app.UseStaticFiles()`.
3. API bind на `0.0.0.0:5000`.
4. Артефакты:
   - `update.xml`,
   - `ReplicaClient.zip`,
   - `changelog.txt` (опционально).

## 5.2 Клиент (`Replica.Client`)

1. NuGet: `Autoupdater.NET.Official`.
2. Запуск проверки до `Application.Run(MainForm)`:
   - `AutoUpdater.Start("http://<IP_API>:5000/updates/update.xml")`.
3. Политика обновления:
   - для критичных релизов — mandatory;
   - для обычных — уведомление с подтверждением.

## 5.3 Релизный цикл

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
6. Автообновление подхватывает новую версию по `update.xml`.

## 7. Definition of Done этапа 4

1. EF + миграции внедрены и воспроизводимы.
2. Минимальные endpoints пользователей и заказов работают стабильно.
3. Concurrency + audit подтверждены тестовыми сценариями.
4. Автообновление клиента реально работает в LAN.
5. Есть короткая эксплуатационная инструкция для релиза.

---

Связь с этапами:
- Вход: `3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`
- Выход: рабочий цикл релизов и поддержка LAN-клиентов без ручной установки.
- Следующий этап: `5_INSTALLER_AND_DEPENDENCIES_PACKAGING_PLAN.md`.

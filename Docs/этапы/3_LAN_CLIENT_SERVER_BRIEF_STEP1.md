# Этап 3: LAN client-server brief (Step 1 implementation)

Дата актуализации: 2026-03-13
Статус: рабочий план этапа 3

## 1. Цель этапа

Запустить архитектурный переход на LAN:
- `MyManager.Client` (WinForms)
- `MyManager.Api` (ASP.NET Core)
- PostgreSQL

Сфокусироваться на **Step 1**: структура solution и вынесение общих моделей.

## 2. Целевая структура solution

1. `MyManager.Shared` (Class Library)
   - DTO, Enums, domain-классы (`Order`, `OrderItem`, `User`, `OrderEvent`).
   - Без ссылок на UI и EF.

2. `MyManager.Api` (Web API)
   - Ссылка на `Shared`.
   - EF Core + Npgsql.
   - Бизнес-логика, фильтры, аудит, concurrency.

3. `MyManager.Client` (WinForms)
   - Ссылка на `Shared`.
   - Только HTTP к API, без прямого доступа к БД.

## 3. Пользователи и авторство

1. Таблица `Users` (`Id`, `Name`, `Role`).
2. Login-окно получает сотрудников из API.
3. `SessionContext` хранит текущего пользователя клиента.
4. `DelegatingHandler` добавляет `X-Current-User`.
5. При создании заказа API заполняет автора (`CreatedById`, `CreatedByUser`).
6. Фильтр UI «Кем создан» работает через `GET /orders?createdBy=...`.

## 4. Технические требования к API

1. API слушает `0.0.0.0:5000` (LAN).
2. Оптимистичная конкурентность (`Version` + 409 Conflict).
3. Аудит через `order_events` (insert/update/delete order/item).
4. Минимум контроллеров: `UsersController`, `OrdersController`.

## 5. Границы Step 1 (что делаем сейчас)

1. Создаём проекты `Shared` и `Api`.
2. Настраиваем references между проектами.
3. Выносим модели `Order`, `OrderItem`, `User`, `OrderEvent` в `Shared`.
4. Убеждаемся, что у `Order` есть поля авторства (`CreatedBy...`).

## 6. Что НЕ входит в Step 1

1. Полная авторизация/безопасность.
2. SaveChanges interceptor и полный аудит (это следующий шаг).
3. Полная реализация UI-фильтров клиента.
4. Автообновление клиента (идёт отдельным потоком после API-базиса).

## 7. Definition of Done этапа 3 (Step 1)

1. В solution присутствуют 3 проекта (`Shared/Api/Client` целевая схема).
2. Доменные модели вынесены в Shared.
3. Слои разделены (клиент не ходит напрямую в PostgreSQL).
4. Подготовлена база для следующего шага: EF migration + контроллеры.

---

Связь с этапами:
- Вход: `2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`
- Продолжение: `4_EF_MIGRATIONS_API_AND_AUTOUPDATE_ROLLOUT_PLAN.md`.

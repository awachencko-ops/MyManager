# MyManager LAN Client-Server Brief (Step 1 focus)

Дата: 2026-03-13
Статус: согласованный бриф (на базе текущего плана + рекомендаций коллеги)

## 1) Подтверждение понимания

Да, задача понята: мы стыкуем текущий план `group-order` + PostgreSQL с целевой клиент-серверной архитектурой в одном solution и готовим старт реализации с фокусом на **Шаг 1**.

## 2) Что берем без изменений (совпадает с текущим планом)

1. `Order` — контейнер, `OrderItem` — вложенные файлы.
2. Поддержка `group-order` логики в UI/данных.
3. Аудит `order_events` обязателен.
4. Защита от конкурентного редактирования через `Version`.
5. Для LAN лучше единый API/service слой, а не прямой доступ WinForms к PostgreSQL.

## 3) Что добавляем из предложений коллеги

### 3.1 Структура solution (3 проекта)

1. `MyManager.Shared` (Class Library)
   - DTO, Enums, доменные классы (`Order`, `OrderItem`, `User`, `OrderEvent`)
   - Без зависимостей от UI и EF Core.

2. `MyManager.Api` (ASP.NET Core Web API)
   - Ссылка на `Shared`.
   - EF Core + Npgsql (Code-First).
   - Контроллеры и бизнес-правила.

3. `MyManager.Client` (WinForms)
   - Ссылка на `Shared`.
   - Нет прямого доступа к БД.
   - Все операции через `HttpClient` -> `MyManager.Api`.

### 3.2 Пользователи и авторство заказа

1. Таблица `Users` в БД (`Id`, `Name`, `Role`).
2. Вход в WinForms через Login-окно со списком пользователей из API.
3. `SessionContext` хранит текущего пользователя на клиенте.
4. `DelegatingHandler` добавляет `X-Current-User` ко всем API-запросам.
5. `Order.CreatedByUser` (или `CreatedById`) — обязательное поле.
6. API заполняет автора из `X-Current-User` при создании заказа.
7. Фильтр "Кем создан" в UI переводим с заглушки на реальный `GET /orders?createdBy=...`.

## 4) Что нужно подправить/уточнить (важные огрехи)

1. **Безопасность заголовка `X-Current-User`:**
   - для LAN MVP допустимо, но доверять ему напрямую рискованно;
   - минимум: whitelist пользователей в API + валидация, что имя существует в `Users`.

2. **`CreatedByUser` vs `CreatedById`:**
   - лучше хранить оба: `CreatedById` (FK, основной), `CreatedByUser` (денормализованный display-name опционально).

3. **`Version` concurrency:**
   - использовать optimistic concurrency в EF (`[ConcurrencyCheck]`/`IsConcurrencyToken`),
   - при конфликте возвращать 409 Conflict.

4. **Аудит `order_events`:**
   - логировать insert/update/delete по `Order` и `OrderItem`;
   - хранить `actor`, `event_type`, `payload` (jsonb), timestamp.

5. **Границы Step 1:**
   - Login UI, полноценные фильтры, SaveChanges interceptor — это Step 3/4;
   - в Step 1 мы только готовим каркас проектов и Shared-модели.

## 5) Единый согласованный запуск (roadmap)

1. **Шаг 1 (сейчас):**
   - создать `Shared` и `Api`, настроить references;
   - вынести модели `Order`, `OrderItem`, `User`, `OrderEvent` в `Shared`;
   - добавить в `Order` поле `CreatedByUser` (минимум) или `CreatedById`+`CreatedByUser` (предпочтительно).

2. **Шаг 2:**
   - DbContext + конфигурации EF Core;
   - индексы/уникальности (`unique(order_id, sequence_no)`, index by creator);
   - concurrency token `Version`;
   - миграция `InitialCreate`.

3. **Шаг 3:**
   - `UsersController`, `OrdersController`;
   - фильтрация `GET /orders?createdBy=...`;
   - аудит через перехват SaveChanges;
   - API bind `0.0.0.0:5000`.

4. **Шаг 4:**
   - Login-окно в WinForms;
   - `SessionContext`, `DelegatingHandler`, бейдж текущего пользователя;
   - подключение UI-фильтра "Кем создан" к реальному API.

## 6) Definition of Ready именно для Step 1

1. В solution есть проекты `MyManager.Shared`, `MyManager.Api`.
2. References настроены корректно.
3. Доменные модели вынесены в Shared.
4. В `Order` есть поле автора (`CreatedByUser` минимум).
5. Client не зависит от БД напрямую на уровне архитектурного решения.

---

Итог: предложения коллеги **совместимы** с текущим планом `group-order + PostgreSQL`; критичных противоречий нет. Рекомендуем стартовать с Step 1 в границах каркаса и моделей, а авторизацию/аудит/фильтры включать следующими шагами.

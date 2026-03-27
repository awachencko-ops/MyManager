# Replica Stage 1 Progress (Security Foundation)

Дата: 2026-03-27  
Статус: In progress

## Scope этого инкремента

Первый рабочий инкремент по `REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26`, фокус только на backend security foundation:

1. Token lifecycle (`login/refresh/revoke`) на API.
2. Token validation в едином current-user middleware.
3. DB-контур для auth-аудита и сессий.
4. Unit-tests на token validation и role-resolution через bearer.

## Выполнено

1. Добавлен токен-сервис `ReplicaApiTokenService`:
   - issue access token,
   - validate bearer/api-key token,
   - refresh по `session_id`,
   - revoke по `session_id`.
2. Расширен `api/auth`:
   - `POST /api/auth/login`,
   - `POST /api/auth/refresh`,
   - `POST /api/auth/revoke`,
   - `GET /api/auth/me` теперь возвращает `authScheme` и `sessionId`.
3. Добавлена поддержка bearer/api-key в `ReplicaApiCurrentUserContext`:
   - `Authorization: Bearer ...`,
   - `X-Api-Key: ...`,
   - fallback на legacy `X-Current-User` сохранен.
4. Добавлены таблицы и EF mapping:
   - `auth_sessions`,
   - `auth_audit_events`.
5. Добавлена миграция:
   - `20260327000100_AuthSessionsAndAudit`.
6. Покрытие тестами:
   - `ReplicaApiTokenServiceTests`,
   - расширение `OrdersControllerActorValidationTests` для bearer resolution.

## Что осталось по Stage 1

1. Полноценный client login flow + secure token storage.
2. Прокидка bearer/api-key в текущие LAN HTTP gateways по умолчанию.
3. Integration pack для `401/403/200` матрицы по всем mutating endpoint.
4. Явные command-handler role guards (не только controller boundary).

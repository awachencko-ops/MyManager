# Replica Stage 1 Progress (Security Foundation)

Дата: 2026-03-27  
Статус: In progress

## Scope этого инкремента

В работе два последовательных инкремента по `REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26`:

1. Backend security foundation.
2. Client bearer propagation + secure local session storage.

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
7. Client-side auth session store:
   - добавлен `ILanApiAuthSessionStore`,
   - реализация `DpapiLanApiAuthSessionStore` (Windows DPAPI, per-user),
   - fallback `InMemoryLanApiAuthSessionStore`.
8. LAN gateways (`identity/write/run`) теперь:
   - используют `Authorization: Bearer` при наличии активной сессии,
   - при `401` на bearer очищают сессию и выполняют retry через `X-Current-User`.
9. `LanApiIdentityService` получил bootstrap сессии:
   - опциональный `allowSessionBootstrap`,
   - при необходимости делает `POST /api/auth/login`,
   - сохраняет access token и повторяет `GET /api/auth/me` через bearer.
10. Обновлен runtime composition:
   - единый `DpapiLanApiAuthSessionStore` прокинут в identity/write/run gateways.
11. Client token refresh policy:
   - добавлен превентивный refresh (`POST /api/auth/refresh`) при близком истечении токена,
   - refresh flow централизован в `LanApiAuthSessionHttpFlow`,
   - policy подключена в identity/write/run gateways перед bearer-запросом,
   - покрыта unit-тестами для `LanApiIdentityService`, `LanOrderWriteApiGateway`, `LanOrderRunApiGateway`.
12. UI session management:
   - в карточке пользователя добавлена индикация состояния сессии (`AuthScheme`),
   - добавлено действие `Войти/Выйти` (manual session bootstrap/logout),
   - `LanApiIdentityService` расширен `LogoutAsync` c `POST /api/auth/revoke`,
   - при logout локальная bearer-сессия очищается, auto-bootstrap можно включить вручную через `Войти`.
13. Integration authorization matrix pack:
   - добавлен `ApiMutatingEndpointsAuthorizationMatrixTests`,
   - покрыты mutating endpoints Orders/Auth/Users по сценариям `401/403/allowed`,
   - отдельно подтверждено, что admin-endpoint (`UsersController.UpsertUser`) не доступен роли `Operator`.

## Что осталось по Stage 1

1. Явные command-handler role guards (не только controller boundary).

## Stage 1 closure update (2026-03-27)

1. Added explicit store-level actor role guards for `UpsertUser` in all store implementations (`InMemory`, `EfCore`, `PostgreSql`) to enforce admin-only mutation at command boundary, not only at controller boundary.
2. Added verify coverage for boundary guard outcomes (operator rejected, bootstrap actor allowed).
3. Stage 1 blocking scope is now closed for current roadmap baseline.

<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.
Date: 2026-03-26

## Change applied

1. `ReplicaApi:Auth:Mode` switched to `Strict`.
2. Legacy flag `ReplicaApi:StrictActorValidation` set to `true`.
3. `Replica.Api` restarted with updated config.

Current live check:

- `GET /live` -> `authMode: "Strict"`.

## Pre-checks before user acceptance

1. Active users exist in `users` table with valid roles (`Admin` or `Operator`).
2. Current client actor is mapped to a known active server user.
3. `GET /api/auth/me` with current actor returns `200`.
4. `GET /api/orders` with current actor returns `200`.
5. `GET /api/orders` with unknown actor returns `403` (expected in strict mode).

## Smoke checklist for operator

1. Open client and verify connection status is green.
2. Create new order.
3. Edit order fields.
4. Run selected order.
5. Stop selected order.
6. Delete order.
7. Open user profile panel and verify role label is resolved.

## Rollback plan

If strict mode blocks real users in production:

1. Set `ReplicaApi:Auth:Mode` back to `Compatibility`.
2. Restart `Replica.Api`.
3. Confirm `GET /live` reports `authMode: "Compatibility"`.
4. Fix users/roles mapping and retry strict cutover in a controlled window.

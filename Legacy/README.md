# Legacy Zone Rules

`Legacy/` is a temporary quarantine area for old code during migration.

## Entry Rules

1. Only move code into `Legacy/` if it is still needed at runtime but blocks target architecture.
2. Every moved unit must include a TODO marker with owner and planned removal step/date.
3. No new features are implemented in `Legacy/`.

## Exit Rules

1. Replace legacy dependency with `Features/*` or `Infrastructure/*` implementation.
2. Remove all runtime references from production paths.
3. Cover replacement with tests (unit/integration/UI smoke where applicable).
4. Delete migrated legacy code and update architecture audit.

## Definition Of Done (Legacy cleanup)

`Legacy/` can be removed when:

1. There are no runtime references to `Legacy/*`.
2. All migrated flows are covered by automated tests.
3. Architecture audit marks legacy risks as closed.

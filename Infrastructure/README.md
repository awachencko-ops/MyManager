# Infrastructure Layout

`Infrastructure/` contains concrete adapters and external integrations.

## Current areas

1. `Infrastructure/Storage/Orders` - file system and PostgreSQL repositories + repository factory.

## Rule

Application layer depends only on ports/interfaces.
Infrastructure depends on application ports and provides concrete implementations.

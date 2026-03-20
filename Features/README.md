# Features Layout

Feature modules are organized as vertical slices:

- `Features/<FeatureName>/UI`
- `Features/<FeatureName>/Application`
- `Features/<FeatureName>/Domain`

## Current migration status

`Orders` is the first migrated feature slice.

## Port rule

Feature ports (for example, `IOrdersRepository`) live in `Features/<FeatureName>/Application/Ports`.
Implementations live in `Infrastructure/*`.

<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.
# Docs Entry Point

Единая точка входа в документацию:

- `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/00_REPLICA_MASTER_DOC_MAP_2026-03-26.md`
- `Docs/REPLICA_DESIGN_SYSTEM_2026-03-27.md`

Пакет по миграции таблицы заказов на OLV (2026-04-02):
- `Docs/REPLICA_ORDERS_GRID_AUDIT_OLV_2026-04-02.md`
- `Docs/REPLICA_ORDERS_GRID_STRUCTURE_2026-04-02.md`
- `Docs/REPLICA_ORDERS_GRID_FUNCTION_BINDINGS_2026-04-02.md`
- `Docs/REPLICA_ORDERS_GRID_DESIGN_2026-04-02.md`
- `Docs/REPLICA_ORDERS_GRID_MIGRATION_PLAN_OLV_2026-04-02.md`
- `Docs/REPLICA_ORDERS_GRID_PARITY_CHECKLIST_2026-04-02.md`

Обязательный режим внедрения OLV:
- Новая таблица разрабатывается в отдельном окне, без изменения рабочего окна со старой таблицей.
- Текущая таблица (`dgvJobs`) остается основным рабочим инструментом на всем периоде разработки.
- Полноценный перенос в рабочее окно выполняется только после полного функционального паритета новой таблицы.
- Перед переносом в рабочее окно обязательно отдельное согласование с владельцем продукта (вами).

Закрытый пакет плана Stage 1-6 (архив):
- `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md`

Архив прежнего контура (перенесено после согласования):
- `Docs/archive/2026-03-26_pre_new_architecture/`

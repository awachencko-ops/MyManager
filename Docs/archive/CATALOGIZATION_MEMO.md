# Памятка по каталогизации (текущее состояние)

Дата фиксации: 2026-03-11

## 1) Рабочие пути приложения

- Папка хранения заказов: `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\Orders`
- Папка временных файлов: `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\Orders\TempReplica`
- Папка архива (Дедушка): `\\NAS\work\Temp\!!!Дедушка`
- История заказов: `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\AppData\history.json`
- Общий лог: `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\AppData\manager.log`
- Логи заказов: `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\AppData\order-logs`

## 2) Единый рабочий каталог обработки (single)

Используется один рабочий temp-контур:

- `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\Orders\TempReplica\in`
- `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\Orders\TempReplica\prepress`
- `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\Orders\TempReplica\print`

## 3) Контур HotFolder (критично, не удалять)

Базовый контур:

- `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\WARNING NOT DELETE`

Подкаталоги:

- `...\\WARNING NOT DELETE\\HotImposing` (перенесено из `C:\HotImposing`)
- `...\\WARNING NOT DELETE\\PitStop` (перенесено из `C:\PitStop`)

## 4) Конфиги сценариев (централизовано на NAS)

- `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\Config\imposing_configs.json`
- `\\NAS\work\Первая Чукотская Типография\Шкалы\Replica BASEFOLDER\Config\pitstop_actions.json`

Пути внутри этих JSON уже переписаны на NAS-контур `WARNING NOT DELETE`.

## 5) Важные правила эксплуатации

- Не удалять и не переименовывать папку `WARNING NOT DELETE`.
- Не менять вручную структуру `in/prepress/print` без обновления логики обработки.
- Изменения путей делать через настройки приложения (или отдельный миграционный скрипт с бэкапом).

## Технические решения (Этап 1-4 реализации)

### 1. Сортировка файлов в заказе
- **Решение:** Сортировка хранится **глобально** (применяется ко всем заказам через `AppSettings`)
- **Формат:** Минимум сортировка по имени и дате добавления
- **Реализация:** `OrderFileItem` содержит `SequenceNo` для FIFO-порядка, сортировка — отдельная настройка в UI

### 2. Формат поля `Variant`
- **Решение:** Использовать **справочник** (enum или список предопределённых значений)
- **Примеры:** Размер (A4, A3), вид (цветной, чёрно-белый), версия (draft, final)
- **Реализация:** `VariantDictionary` в настройках или база данных; UI с выпадающим списком

### 3. Ручная перестановка `SequenceNo`
- **Решение:** **ДА, разрешить** пользователю менять порядок файлов вручную (drag-and-drop)
- **Механизм:** При перетаскивании переиндексируются `SequenceNo` всех файлов в заказе
- **Опция:** Можно добавить `AllowManualSequenceReordering` в `AppSettings` (по умолчанию `true`)

### 4. Статус заказа при частичной ошибке
- **Решение:** **? Частично готово (X/Y)** — статус заказа отражает реальный прогресс
- **Примеры:**
  - 3 файла успешно, 2 ошибка ? `? Частично готово (3/5)`
  - Все файлы успешны ? `? Готово`
  - Все файлы в ошибке ? `?? Ошибка`
- **Логика:** Агрегированный статус пересчитывается после каждого файла

### 5. Дубликаты имён в одной стадии
- **Решение:** **Автопереименование** вместо блокирующей ошибки
- **Механизм:** При сохранении файла в стадию проверить наличие, добавить суффикс `_1`, `_2` и т.д.
- **Пример:** `файл.pdf` + `файл.pdf` ? `файл.pdf` и `файл_1.pdf`
- **Когда:** Проверка перед сохранением в `SourcePath`, `PreparedPath`, `PrintPath`

---

## Детальный план реализации (Этап 1-4)

### Этап 1 — Data/Storage (приоритет: ВЫСОКИЙ, ~3-4 часа)

#### Задача 1.1: Создать `OrderFileItem` класс
- **Файл:** `OrderFileItem.cs` (новый)
- **Содержит:**
  - `ItemId` (Guid.NewGuid().ToString("N"))
  - `ClientFileLabel` (основной файл для клиента)
  - `Variant` (справочник: A4, A3, цветной, ч/б и т.д.)
  - `SourcePath`, `PreparedPath`, `PrintPath` (для каждого файла свой pipeline)
  - `TechnicalFiles` (List<string> для вспомогательных файлов)
  - `FileStatus` (? Ожидание, ?? В работе, ? Готово, ?? Ошибка)
  - `LastReason` (текст ошибки)
  - `UpdatedAt` (время последнего изменения)
  - `SequenceNo` (long, для FIFO-порядка)

#### Задача 1.2: Расширить `OrderData` класс
- **Файл:** `OrderData.cs`
- **Изменить:**
  - Добавить `public List<OrderFileItem> Items { get; set; } = new();`
  - Оставить старые поля (`SourcePath`, `PreparedPath`, `PrintPath`) для **обратной совместимости**
  - Поле `Status` остаётся, но теперь это **агрегированный статус** (вычисляется из Items)

#### Задача 1.3: Расширить `AppSettings`
- **Файл:** `AppSettings.cs`
- **Добавить:**
  ```csharp
  public bool AllowManualSequenceReordering { get; set; } = true;
  public int MaxParallelism { get; set; } = 4; // 0 = без ограничений
  public string DefaultOrderSortBy { get; set; } = "SequenceNo"; // или "Name", "Date"
  public List<string> VariantDictionary { get; set; } = new() { "A4", "A3", "цветной", "ч/б", "draft", "final" };
  public bool AutoRenameOnDuplicate { get; set; } = true;
  ```

#### Задача 1.4: Реализовать миграцию `history.json`
- **Файл:** Новый класс `HistoryMigrationService.cs`
- **Логика:**
  1. При загрузке истории: если `Items.Count == 0`, создавать их из старых полей:
     - Из `SourcePath` ? создать `OrderFileItem` со статусом в зависимости от наличия `PreparedPath` и `PrintPath`
     - Если `SourcePath` пуст ? оставить Items пустым
  2. При сохранении: синхронизировать старые поля с `Items[0]` (для обратной совместимости)
  3. Резервная копия: при первой миграции создавать `history.json.bak`
  4. Обновить `Form1.LoadHistory()` и `Form1.SaveHistory()` для работы с миграцией

#### Задача 1.5: Обновить логирование
- **Файл:** `Form1.cs` (метод `AppendOrderStatusLog`)
- **Добавить:** логирование по `ItemId` и `SequenceNo`, если работаем с Items

---

### Этап 2 — UI групп и рабочей строки (приоритет: ВЫСОКИЙ, ~8-10 часов)

#### Задача 2.1: Иерархическое отображение в таблице
- **Текущая структура:** `gridOrders` с колонками (Id, Status, SourcePath, PreparedPath, PrintPath, PitStop, Imposing)
- **Нужно:**
  - Добавить в первую колонку кнопку expand/collapse (? / ?) для развёртывания Items
  - Или использовать `TreeView` вместо `DataGridView` (более сложно, но правильнее)
  - **Рекомендация:** начать с `DataGridView` + кастомный renderer для expand/collapse

#### Задача 2.2: «Рабочая строка» автодобавления
- **Поведение:**
  - При разворачивании заказа показывается список Items
  - Внизу всегда есть пустая строка (ItemId = empty)
  - Когда пользователь тащит файл на пустую строку:
    - Создаётся новый `OrderFileItem`
    - Автоматически добавляется новая пустая строка

#### Задача 2.3: Drag-and-drop для переставления файлов
- **Механизм:**
  - Пользователь перетаскивает Item на другое место в списке
  - Переиндексируются `SequenceNo` всех Items в заказе
  - UI обновляется

#### Задача 2.4: Контекстное меню «Превратить в группу»
- **Для однофайловых заказов:**
  - Если `Items.Count == 0` и `SourcePath` не пуст:
    - Показать пункт меню: «Превратить в группу»
    - При клике: создать `OrderFileItem` из текущих путей + добавить пустую строку

---

### Этап 3 — Pipeline и batch (приоритет: ВЫСОКИЙ, ~8-10 часов)

#### Задача 3.1: Переписать `OrderProcessor.RunAsync()`
- **Текущее:** обрабатывает одну цепочку SourcePath ? PreparedPath ? PrintPath
- **Новое:** обработка списка `order.Items` параллельно
- **Логика:**
  1. Если `Items.Count > 0` — обработать каждый Item последовательно или параллельно (смотри MaxParallelism)
  2. Если `Items.Count == 0` — fallback на старую логику (старые поля)
  3. Для каждого Item: обновлять `FileStatus` и `LastReason`
  4. После каждого Item: пересчитывать агрегированный статус заказа

#### Задача 3.2: Диалог выбора перед запуском
- **Новый файл:** `RunModeDialog.cs` (форма)
- **Варианты:**
  1. Обработать все файлы (по умолчанию)
  2. Обработать только выбранные (если есть выделение)
  3. Применить одинаковые операции / разные операции
- **Вызов:** `Form1.RunForOrderAsync()` ? показать диалог ? вызвать processor

#### Задача 3.3: Реализовать агрегированный статус
- **Новый метод:** `OrderData.CalculateAggregatedStatus()`
- **Логика:**
  ```
  Если Items.Count == 0:
    Вернуть старый Status (для обратной совместимости)
  
  Посчитать:
    successCount = Items.Count(x => x.FileStatus == "? Готово")
    errorCount = Items.Count(x => x.FileStatus == "?? Ошибка")
    inProgressCount = Items.Count(x => x.FileStatus == "?? В работе")
  
  Если errorCount == Items.Count:
    Status = "?? Ошибка"
  Иначе если successCount == Items.Count:
    Status = "? Готово"
  Иначе если inProgressCount > 0:
    Status = $"?? В работе ({successCount + inProgressCount}/{Items.Count})"
  Иначе:
    Status = $"? Частично готово ({successCount}/{Items.Count})"
  ```
- **Вызов:** после каждого изменения FileStatus в процессоре или UI

#### Задача 3.4: Массовые операции
- **Методы в `Form1`:**
  - `ApplyBatchPitStop()` — к выбранным Items
  - `ApplyBatchImposing()` — к выбранным Items
  - `BatchCopyToNextStage()` — копировать SourcePath ? PreparedPath для всех выбранных
  - `BatchValidatePaths()` — проверить наличие файлов во всех Items

#### Задача 3.5: Обновить UI для прогресса
- **В таблице:** показывать статус для каждого Item (может быть под-строка или иконка)
- **В статус-баре:** показывать текущий прогресс (X/Y файлов обработано)

---

### Этап 4 — Производительность и стабилизация (приоритет: СРЕДНИЙ, ~4-6 часов)

#### Задача 4.1: Параллельная обработка с SemaphoreSlim
- **Файл:** `OrderProcessor.cs`
- **Добавить:** SemaphoreSlim для ограничения параллелизма
  ```csharp
  private SemaphoreSlim _parallelismLimiter;
  
  public OrderProcessor(string rootPath, int maxParallelism = 4)
  {
      int limit = maxParallelism == 0 ? Environment.ProcessorCount : maxParallelism;
      _parallelismLimiter = new SemaphoreSlim(limit, limit);
  }
  ```
- **Логика в RunAsync:** обрабатывать Items параллельно с ограничением

#### Задача 4.2: Оптимизация для больших заказов
- Потоковая обработка без загрузки всех Items в памяти сразу
- Lazy-loading для UI (показывать по N Items за раз)

#### Задача 4.3: Модульные тесты
- `OrderFileItemTests.cs` — создание, валидация SequenceNo
- `OrderDataAggregationTests.cs` — правильный расчёт статусов
- `HistoryMigrationTests.cs` — миграция старого формата
- `DuplicateFileNameTests.cs` — автопереименование

#### Задача 4.4: Профилирование
- Тестирование на заказах с 50+ файлами
- Оптимизация IO-операций

---

## Существующие компоненты, которые НЕ меняются

- ? `ConfigService.cs` — остаётся как есть
- ? `ActionConfig.cs`, `ImposingConfig.cs` — остаются как есть
- ? `PitStopSelectForm.cs`, `ImposingSelectForm.cs` — остаются как есть
- ? `Logger.cs` — может потребоваться расширение для логирования по ItemId
- ? Общая архитектура обработки файлов остаётся

---

## Ожидаемые файлы после реализации

**Новые файлы:**
- `OrderFileItem.cs` (класс Item)
- `HistoryMigrationService.cs` (миграция JSON)
- `RunModeDialog.cs` / `RunModeDialog.Designer.cs` (диалог выбора режима)
- Возможно `OrderStatusAggregator.cs` (вспомогательный класс для расчёта статусов)

**Изменённые файлы:**
- `OrderData.cs` (+ List<OrderFileItem>)
- `AppSettings.cs` (+ новые поля)
- `Form1.cs` (+ расширенная UI, новые методы для Items)
- `OrderProcessor.cs` (+ параллельная обработка Lists)
- Возможно `Form1.Designer.cs` (если меняется UI таблицы)

**Без изменений:**
- ConfigService.cs
- ActionConfig.cs, ImposingConfig.cs
- PitStopSelectForm.cs, ImposingSelectForm.cs
- И т.д.

---

## Соответствие требованиям из MULTI_FILE_ORDER_STRATEGY.md

- ? Бизнес-модель: заказ как контейнер файлов (Items)
- ? Режим обработки: с выбором перед запуском, обработка всех по умолчанию
- ? Параллельная обработка: конфигурируемая (MaxParallelism)
- ? Миграция старой истории: с резервной копией и откатом
- ? Частичный статус: ? Частично готово (X/Y)
- ? FIFO + ручная перестановка: SequenceNo + drag-and-drop
- ? Обратная совместимость: старые поля остаются, используются для миграции
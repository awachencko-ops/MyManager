## 🎯 Технические решения (Подтверждены)

### 1. Сортировка файлов в заказе
- **Решение:** Сортировка хранится **глобально** (применяется ко всем заказам через `AppSettings`)

### 2. Формат поля `Variant`
- **Решение:** Использовать **справочник** (список строк в `AppSettings.VariantDictionary`)

### 3. Ручная перестановка `SequenceNo`
- **Решение:** **ДА, разрешить** пользователю менять порядок файлов drag-and-drop

### 4. Статус заказа при частичной ошибке
- **Решение:** **⚠ Частично готово (X/Y)** — статус отражает реальный прогресс

### 5. Дубликаты имён в одной стадии
- **Решение:** **Автопереименование** (`_1`, `_2`, и т.д.)

---

## 📋 ИСПРАВЛЕННЫЙ ПЛАН РЕАЛИЗАЦИИ (Этапы 1-4)

### ⭐ Этап 1 — Data/Storage (ПРИОРИТЕТ: КРИТИЧЕСКИЙ, ~2-3 часа)


#### Задача 1.1: Создать `OrderFileItem.cs` класс
- **Новый файл:** `OrderFileItem.cs`
- **Содержит:**
  ```csharp
  public class OrderFileItem
  {
      public string ItemId { get; set; } = Guid.NewGuid().ToString("N");
      public string ClientFileLabel { get; set; } = "";
      public string Variant { get; set; } = "";
      public string SourcePath { get; set; } = "";
      public string PreparedPath { get; set; } = "";
      public string PrintPath { get; set; } = "";
      public List<string> TechnicalFiles { get; set; } = new();
      public string FileStatus { get; set; } = "⚪ Ожидание";
      public string LastReason { get; set; } = "";
      public DateTime UpdatedAt { get; set; } = DateTime.Now;
      public long SequenceNo { get; set; }
  }
  ```
- **Время:** ~15 мин

#### Задача 1.2: Расширить `OrderData.cs`
- **Изменить:** Добавить `public List<OrderFileItem> Items { get; set; } = new();`
- **Важно:** Оставить старые поля (`SourcePath`, `PreparedPath`, `PrintPath`) для обратной совместимости
- **Время:** ~10 мин

#### Задача 1.3: Расширить `AppSettings.cs`
- **Добавить новые поля:**
  ```csharp
  public bool AllowManualSequenceReordering { get; set; } = true;
  public int MaxParallelism { get; set; } = 4;
  public string DefaultOrderSortBy { get; set; } = "SequenceNo";
  public List<string> VariantDictionary { get; set; } = new() { "A4", "A3", "Цветной", "Ч/Б", "draft", "final" };
  public bool AutoRenameOnDuplicate { get; set; } = true;
  ```
- **Время:** ~10 мин

#### Задача 1.4: Миграция старых заказов (УПРОЩЁННО)
- **Файл:** Встроить в `Form1.LoadHistory()` (не создавать отдельный сервис)
- **Логика:**
  ```csharp
  // После загрузки каждого заказа из JSON:
  foreach (var order in _orderHistory)
  {
      // Если Items пуст, но есть старые пути — мигрируем
      if (order.Items.Count == 0 && !string.IsNullOrEmpty(order.SourcePath))
      {
          var item = new OrderFileItem
          {
              SourcePath = order.SourcePath,
              PreparedPath = order.PreparedPath,
              PrintPath = order.PrintPath,
              SequenceNo = 0
          };
          order.Items.Add(item);
      }
  }
  ```
- **Резервная копия:** При первом запуске новой версии копируем `history.json` → `history.json.bak`
- **Время:** ~20 мин

#### Задача 1.5: Метод агрегирования статуса в `OrderData.cs`
- **Добавить метод:**
  ```csharp
  public void RefreshAggregatedStatus()
  {
      if (Items.Count == 0)
          return; // Используем старый Status
      
      var successCount = Items.Count(x => x.FileStatus == "✅ Готово");
      var errorCount = Items.Count(x => x.FileStatus == "🔴 Ошибка");
      var inProgressCount = Items.Count(x => x.FileStatus == "🟡 В работе");
      
      if (errorCount == Items.Count)
          Status = "🔴 Ошибка";
      else if (successCount == Items.Count)
          Status = "✅ Готово";
      else if (inProgressCount > 0)
          Status = $"🟡 В работе ({successCount + inProgressCount}/{Items.Count})";
      else
          Status = $"⚠ Частично готово ({successCount}/{Items.Count})";
  }
  ```
- **Время:** ~15 мин

**Итого Этап 1:** ~1 час 20 минут (+ время на компиляцию и тестирование)

---

### ⭐ Этап 2 — UI иерархии заказов (ПРИОРИТЕТ: ВЫСОКИЙ, ~6-8 часов)

#### Задача 2.1: Модификация таблицы для показа Items (УПРОЩЁННО)
- **Текущая ситуация:** `gridOrders` показывает только заказы, каждая строка = 1 заказ
- **Новый подход:** 
  - Если заказ имеет `Items.Count > 0` → показываем Items как отдельные строки со смещением
  - Если `Items.Count == 0` → показываем заказ как раньше (обратная совместимость)
  - Первая строка заказа = кнопка expand/collapse + название заказа
  - Следующие строки = Items (с зелёным фоном или отступом для визуального отличия)

**Реализация:**
- **Не менять структуру `DataGridView`**
- **Добавить метод `RefreshGridWithHierarchy()`:**
  - Перепостраивает `gridOrders.Rows` с учётом иерархии Items
  - Сохраняет состояние expand/collapse в памяти (Dictionary<orderId, isExpanded>)
  - При клике на кнопку expand/collapse — пересчитываем строки

- **Время:** ~2-3 часа (логика перестройки таблицы — самая сложная часть)

#### Задача 2.2: Обработка drag-and-drop файлов в Items
- **Текущее поведение:** drag-and-drop в ячейку заказа → добавляет файл в `SourcePath`
- **Новое поведение:**
  - Если заказ развёрнут и пользователь тащит файл на Item → обновляет этот Item
  - Если заказ развёрнут и пользователь тащит файл **ниже всех Items** (на пустую строку) → создаёт новый Item
  - Если заказ свёрнут → добавляет как раньше в `SourcePath` (fallback)

- **Время:** ~1-1.5 часа

#### Задача 2.3: Контекстное меню для Items
- **Добавить пункты:**
  - Удалить Item
  - Копировать Item в следующую стадию
  - Переместить выше/ниже (для изменения SequenceNo)
  - Свойства Item (редактирование Variant, Label)

- **Время:** ~1.5-2 часа

#### Задача 2.4: Меню «Превратить в группу»
- **Для однофайловых заказов:** ПКМ → «Превратить в группу»
- **Логика:** Если `Items.Count == 0` и `SourcePath` не пуст:
  - Создаём Item из текущих путей
  - Перезагружаем таблицу в expand-режиме
  
- **Время:** ~30 мин

**Итого Этап 2:** ~6-8 часов (это реалистично для UI с таблицей)

---

### ⭐ Этап 3 — Batch-обработка и параллелизм (ПРИОРИТЕТ: ВЫСОКИЙ, ~6-8 часов)

#### Задача 3.1: Переписать `OrderProcessor.RunAsync()` для Items
- **Текущее:** обрабатывает одного заказа целиком (SourcePath → PreparedPath → PrintPath)
- **Новое:**
  1. Если `order.Items.Count > 0` → обработать каждый Item последовательно
  2. Если `order.Items.Count == 0` → fallback на старую логику
  
**Псевдокод:**
```csharp
public async Task RunAsync(OrderData order, CancellationToken ct)
{
    var settings = AppSettings.Load();
    
    if (order.Items.Count > 0)
    {
        // Новая логика: обработка Items
        foreach (var item in order.Items.OrderBy(x => x.SequenceNo))
        {
            item.FileStatus = "🟡 В работе";
            try
            {
                await ProcessItemAsync(item, order, settings, ct);
                item.FileStatus = "✅ Готово";
            }
            catch (Exception ex)
            {
                item.FileStatus = "🔴 Ошибка";
                item.LastReason = ex.Message;
            }
            
            order.RefreshAggregatedStatus();
            OnStatusChanged?.Invoke(order.Id, order.Status, "Обновлён статус");
        }
    }
    else
    {
        // Старая логика для обратной совместимости
        await RunLegacyAsync(order, ct);
    }
}

private async Task ProcessItemAsync(OrderFileItem item, OrderData order, AppSettings settings, CancellationToken ct)
{
    // Логика обработки одного Item (копирование, PitStop, Imposing, и т.д.)
    // Аналогична текущей логике, но работает с item.SourcePath вместо order.SourcePath
}
```

- **Время:** ~2-3 часа

#### Задача 3.2: Диалог выбора режима обработки (УПРОЩЁННО)
- **Вместо отдельной формы:** добавляем в контекстное меню
  - ПКМ → Run → выпадающее меню:
    - `Обработать все файлы`
    - `Обработать только выбранные` (если есть выделенные Items)
    - (если выбранный заказ имеет `Items.Count == 0`, показываем только первый вариант)

- **Время:** ~1 час

#### Задача 3.3: Простая параллельная обработка
- **Не использовать `SemaphoreSlim`** (слишком сложно)
- **Использовать `Task.WhenAll()` с простым ограничением:**
  ```csharp
  // Получаем MaxParallelism из settings
  int batchSize = settings.MaxParallelism == 0 
      ? Environment.ProcessorCount 
      : settings.MaxParallelism;
  
  // Обрабатываем Items батчами
  for (int i = 0; i < order.Items.Count; i += batchSize)
  {
      var batch = order.Items.Skip(i).Take(batchSize);
      var tasks = batch.Select(item => ProcessItemAsync(item, order, settings, ct));
      await Task.WhenAll(tasks);
  }
  ```
- **Время:** ~1.5 часа

#### Задача 3.4: Обновление UI в процессе обработки
- **Текущее:** `OnStatusChanged` событие → обновляет статус в таблице
- **Новое:** 
  - Добавляем callback для обновления статуса Item-а
  - При изменении `FileStatus` Item-а → перерисовываем строку таблицы
  - В статус-баре показываем прогресс: `Обработано X/Y файлов`

- **Время:** ~1-1.5 часа

**Итого Этап 3:** ~6-8 часов

---

### ⭐ Этап 4 — Оптимизация и тесты (ПРИОРИТЕТ: СРЕДНИЙ, ~3-4 часа)

#### Задача 4.1: Вспомогательные функции
- `GetUniqueFileName()` — автопереименование при дубликатах
- `GetAvailableSequenceNo()` — получить следующий номер для FIFO
- `ValidateItemPaths()` — проверка файлов в Item-е

- **Время:** ~1 час

#### Задача 4.2: Улучшения производительности
- Оптимизация `RefreshGridWithHierarchy()` для больших заказов (50+ Items)
- Кэширование состояния expand/collapse
- Lazy-loading таблицы (показываем сначала заказы, Items загружаем по требованию)

- **Время:** ~1 час

#### Задача 4.3: Базовые тесты (опционально)
- `OrderFileItemTests.cs` — создание Item, SequenceNo
- `OrderDataAggregationTests.cs` — расчёт агрегированного статуса

- **Время:** ~1-1.5 часа

**Итого Этап 4:** ~3-4 часа (полностью опционально для MVP)

---

## 📊 Сводная таблица реализации

| Этап | Задачи | Реалист. время | Статус | Комментарий |
|------|--------|---|---|---|                                                                                                                                                                                                                                                   
| **1. Data/Storage** | 1.1-1.5 | ~1-2 часа | 🔵 TODO | Основа всего; критично для обратной совместимости |
| **2. UI иерархии** | 2.1-2.4 | ~6-8 часов | 🔵 TODO | Самая сложная часть (переестройка таблицы) |
| **3. Batch & Pipeline** | 3.1-3.4 | ~6-8 часов | 🔵 TODO | Обработка Items, параллелизм, диалоги |
| **4. Оптимизация** | 4.1-4.3 | ~2-3 часа | 🔵 ОПЦИОНАЛЬНО | Для MVP можно пропустить |
| **ИТОГО (MVP)** | — | **~13-18 часов** | — | Полная многофайловая поддержка без излишеств |

---

## 🏗️ Существующие компоненты (не менять)
- ✅ `ActionConfig.cs`, `ImposingConfig.cs` — остаются как есть
- ✅ `PitStopSelectForm.cs`, `ImposingSelectForm.cs` — остаются как есть
- ✅ `Logger.cs` — может потребоваться расширение для логирования по ItemId
- ✅ Общая архитектура обработки файлов остаётся

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

- ✅ Бизнес-модель: заказ как контейнер файлов (Items)
- ✅ Режим обработки: с выбором перед запуском, обработка всех по умолчанию
- ✅ Параллельная обработка: конфигурируемая (MaxParallelism)
- ✅ Миграция старой истории: с резервной копией и откатом
- ✅ Частичный статус: ⚠ Частично готово (X/Y)
- ✅ FIFO + ручная перестановка: SequenceNo + drag-and-drop
- ✅ Обратная совместимость: старые поля остаются, используются для миграции     
# Предлагаемая система группировки классов MyManager

Цель: перейти от плоской структуры файлов к предсказуемой архитектуре, где легко найти:
- **Models** (данные),
- **Services** (бизнес-логика, I/O, инфраструктура),
- **UI** (переиспользуемые UI-компоненты),
- **Forms** (окна WinForms),
- и отдельно поддерживать новую главную форму **MainForm.cs**.

## 1) Целевая структура папок

```text
MyManager/
  Models/
    AppSettings.cs
    ActionConfig.cs
    ImposingConfig.cs
    OrderData.cs
    OrderFileItem.cs

  Services/
    ConfigService.cs
    OrderProcessor.cs
    PdfWatermark.cs
    Logger.cs
    StoragePaths.cs
    UI/
      OrderGridContextMenu.cs

  Forms/
    MainForm.cs
    MainForm.Designer.cs
    MainForm.resx

    Form1.cs
    Form1.Designer.cs
    Form1.resx

    ActionManagerForm.cs
    ActionManagerForm.Designer.cs
    ActionManagerForm.resx

    ImposingManagerForm.cs
    ImposingManagerForm.Designer.cs
    ImposingManagerForm.resx

    ImposingSelectForm.cs
    ImposingSelectForm.Designer.cs
    ImposingSelectForm.resx

    PitStopSelectForm.cs
    PitStopSelectForm.Designer.cs
    PitStopSelectForm.resx

    OrderForm.cs
    OrderForm.Designer.cs
    OrderForm.resx

    CopyForm.cs
    CopyForm.Designer.cs
    CopyForm.resx

    SimpleOrderForm.cs
    SimpleOrderForm.Designer.cs
    SimpleOrderForm.resx

    SettingsDialogForm.cs
    SettingsDialogForm.resx
    OrderLogViewerForm.cs

  UI/
    Fonts/
      SimpleFontResolver.cs

  Program.cs
  MyManager.csproj
```

> Примечание: `SettingsDialogForm.cs` сейчас без `.Designer.cs` — это допустимо, если форма собрана вручную.

## 2) Правила по слоям

### Models
- Только структуры данных и простые валидации.
- Без WinForms и без работы с файлами/сетью.

### Services
- Сценарии обработки заказов, конфиги, логирование, файлы и PDF.
- Могут зависеть от `Models`.
- Не должны зависеть от конкретных `Form`.

### UI
- Переиспользуемые визуальные/околoвизуальные элементы (резолвер шрифтов, контекстные меню, helper-контролы).
- Допустима зависимость от WinForms.

### Forms
- Только код окон и оркестрация пользовательских действий.
- Сложная логика выносится в `Services`.

## 3) Переход к MainForm как новой главной форме

### Этап A. Совместное существование
1. Оставить `Form1` в проекте как legacy-экран.
2. Развивать навигацию и shell-поведение в `MainForm`.
3. Новые функции добавлять только через `MainForm` + `Services`.

### Этап B. Точка переключения
1. В `Program.cs` заменить:
   - `Application.Run(new Form1());`
   - на `Application.Run(new MainForm());`
2. Провести smoke-проверку ключевых пользовательских сценариев.

### Этап C. Декомпозиция Form1
1. Извлечь из `Form1` независимые блоки в `Services`.
2. Перенести оставшиеся диалоги в `Forms` и навигацию через `MainForm`.
3. После полного переноса — удалить `Form1`.

## 4) Практический порядок рефакторинга по коммитам

1. **Коммит 1: только перемещения файлов по папкам** (без изменения логики).
2. **Коммит 2: namespace-выравнивание** (`MyManager.Models`, `MyManager.Services`, `MyManager.Forms`, `MyManager.UI`).
3. **Коммит 3: переключение startup на MainForm**.
4. **Коммит 4+: перенос логики из Form1 в Services малыми шагами**.

Такой порядок снижает риск: если что-то ломается, легко понять, на каком шаге.

## 5) Минимальные соглашения по именованию

- Модели: существительные (`OrderData`, `AppSettings`).
- Сервисы: суффикс `Service`/`Processor` (`ConfigService`, `OrderProcessor`).
- Формы: суффикс `Form`.
- UI-компоненты: говорящие имена (`OrderGridContextMenu`, `SimpleFontResolver`).

## 6) Что это даст

- Быстрый поиск классов по назначению.
- Более безопасный переход с legacy `Form1` на `MainForm`.
- Проще покрывать сервисы тестами, не затрагивая WinForms-слой.

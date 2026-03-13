# MyManager — универсальный бриф: MainForm migration → group-order → PostgreSQL/LAN → автообновление клиента

Дата: 2026-03-13
Статус: единый рабочий документ

## 1. Объединённая цель

Собираем единый трек работ:
1. Закрыть текущий этап стабилизации `MainForm` (`single-first`).
2. Реализовать `group-order` (контейнер заказа + вложенные files/items) без ломки текущей логики.
3. Перейти на клиент-сервер в LAN: `MyManager.Client` (WinForms) -> `MyManager.Api` (ASP.NET Core) -> PostgreSQL.
4. Убрать ручные обходы по 5 ПК через автоматические обновления клиента.

## 2. Базовые принципы (согласовано)

1. `Order` = контейнер с полями менеджера:
   - `OrderNumber`;
   - `ManagerOrderDate`.
2. `OrderItem` = файл внутри заказа:
   - `PrepressArrivalAt`;
   - стадийные пути, статус, операции, `SequenceNo`.
3. Типы для интерфейса:
   - `single-order` (1 item),
   - `group-order` (2+ items).
4. Метки типа заказа обязательны для UI (цвет/поведение строки).
5. Источник истины по файлам и стадиям — item-level.
6. Обязательны аудит `order_events` и optimistic concurrency (`Version`).

## 3. UI/UX правила для group-order

1. Строка `group-order` имеет другой цвет плашки.
2. ЛКМ по строке контейнера: expand/collapse списка files.
3. В контейнерной строке показываем:
   - номер заказа,
   - дату оформления менеджером.
4. В строках files показываем операционные поля файла + при необходимости контекст контейнера.
5. Кнопка toolbar: **«Добавить файл в заказ»**.

## 4. Целевая архитектура solution (LAN)

1. `MyManager.Shared` (Class Library): DTO/Enums/Domain (`Order`, `OrderItem`, `User`, `OrderEvent`) — без UI/EF.
2. `MyManager.Api` (ASP.NET Core Web API): EF Core + Npgsql, бизнес-логика, фильтры, аудит, concurrency.
3. `MyManager.Client` (WinForms): только HTTP к API, без прямого доступа к PostgreSQL.

## 5. Пользователи и авторство заказа

1. Таблица `Users`: `Id`, `Name`, `Role`.
2. Login-окно в клиенте получает список пользователей из API.
3. Клиент хранит текущего пользователя в `SessionContext`.
4. `DelegatingHandler` добавляет `X-Current-User` ко всем запросам.
5. Заказ хранит автора (`CreatedById` + `CreatedByUser` как display).
6. API при создании заказа берёт автора из заголовка и валидирует по `Users`.
7. Фильтр UI «Кем создан» = реальный `GET /orders?createdBy=...`.

## 6. PostgreSQL/EF (ядро требований)

1. `orders`, `order_items`, `order_events`, `users`.
2. Ограничения:
   - `unique(order_id, sequence_no)`;
   - индекс по автору (`CreatedByUser`/`CreatedById`);
   - `Version` как concurrency token;
   - `check(sequence_no > 0)`.
3. При конфликте версии API возвращает **409 Conflict**.

## 7. Автообновление WinForms-клиента (новый обязательный блок)

Выбранный стек: **Autoupdater.NET.Official**.
Сервер обновлений: наш `MyManager.Api` (раздаёт `update.xml` + ZIP клиента).

### 7.1 Шаг 1 (API): статические файлы обновления

Папка:
- `MyManager.Api/wwwroot/updates/`

Что кладём туда:
- `update.xml`
- `MyManagerClient.zip`

Код для `Program.cs` в `MyManager.Api`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseRouting();
app.UseStaticFiles(); // чтобы раздавать wwwroot/updates/*

app.MapControllers();

app.Run("http://0.0.0.0:5000");
```

### 7.2 Шаблон `wwwroot/updates/update.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<item>
  <version>1.2.0</version>
  <url>http://192.168.1.10:5000/updates/MyManagerClient.zip</url>
  <changelog>http://192.168.1.10:5000/updates/changelog.txt</changelog>
  <mandatory>
    <value>true</value>
    <minVersion>1.0.0</minVersion>
  </mandatory>
</item>
```

### 7.3 Шаг 2 (Client): запуск AutoUpdater до `Application.Run`

Установить NuGet:
- `Autoupdater.NET.Official`

Код для `Program.cs` в `MyManager.Client`:

```csharp
using AutoUpdaterDotNET;
using System.Globalization;

[STAThread]
static void Main()
{
    ApplicationConfiguration.Initialize();

    // Русская локаль интерфейса автообновления
    AutoUpdater.ParseUpdateInfoEvent += args =>
    {
        args.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
    };

    // Принудительное закрытие перед обновлением (не держим lock файлов)
    AutoUpdater.ApplicationExitEvent += () =>
    {
        Application.Exit();
    };

    AutoUpdater.Start("http://192.168.1.10:5000/updates/update.xml");

    Application.Run(new MainForm());
}
```

> Примечание: в зависимости от версии библиотеки имена событий/настроек могут отличаться; если сигнатуры не совпадут, оставляем `AutoUpdater.Start(...)` как обязательный минимум и подстраиваемся под актуальный API пакета.

## 8. Как выпускать релиз, чтобы 5 клиентов обновлялись автоматически

1. Собрать клиент:
   - `dotnet publish MyManager.Client -c Release -r win-x64 --self-contained false`
2. Взять содержимое publish-папки и упаковать в `MyManagerClient.zip`.
3. Обновить в `update.xml`:
   - `<version>` (увеличить),
   - `<url>` на новый ZIP,
   - `<changelog>` (по желанию),
   - `<mandatory>` по политике релиза.
4. Скопировать `update.xml` и ZIP в `MyManager.Api/wwwroot/updates/` на серверном ПК.
5. Перезапустить API (или просто заменить файлы, если static hosting уже активен).
6. При следующем старте клиенты увидят новую версию и обновятся без ручной установки на каждом ПК.

## 9. Порядок работ (единый, практический)

1. Закрыть single-first regression + baseline.
2. Подготовить 3-проектную структуру solution (`Shared/Api/Client`).
3. Вынести доменные модели и поля авторства.
4. Поднять EF Core + миграцию `InitialCreate`.
5. Реализовать API фильтрации по автору + аудит + concurrency.
6. Реализовать login/session/header на клиенте.
7. Подключить автообновление через `wwwroot/updates` + AutoUpdater.
8. Прогнать LAN smoke-check на 2–3 клиентах, затем раскатка на 5 ПК.

## 10. Риски и минимальные контрмеры

1. Риск spoofing `X-Current-User` -> валидация имени в `Users`, ограничение по LAN и журналирование actor/IP.
2. Риск конфликтов при параллельном редактировании -> `Version` + 409 + перезагрузка записи в UI.
3. Риск повреждения обновления -> хранить предыдущий ZIP и откатный `update.xml`.
4. Риск простоя из-за обязательного апдейта -> mandatory включать только для критичных релизов.

---

Этот документ заменяет необходимость смотреть несколько разрозненных заметок при запуске следующего этапа.

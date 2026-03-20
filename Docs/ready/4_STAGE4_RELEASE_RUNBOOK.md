# Stage 4 Release Runbook (LAN AutoUpdate)

Дата: 2026-03-20
Статус: рабочая инструкция этапа 4

## 1. Подготовка артефактов клиента

1. Выполнить publish клиента:
   - `dotnet publish Replica.csproj -c Release -r win-x64 --self-contained false`
2. Упаковать содержимое publish-папки в `ReplicaClient.zip`.

## 2. Подготовка update feed

1. Открыть `Replica.Api/wwwroot/updates/update.xml`.
2. Обновить поля:
   - `<version>` -> целевая версия релиза;
   - `<url>` -> LAN URL до `ReplicaClient.zip`;
   - `<changelog>` -> LAN URL до `changelog.txt`.
3. Обновить `changelog.txt` (кратко: что изменилось).
4. Разместить в `Replica.Api/wwwroot/updates`:
   - `update.xml`;
   - `ReplicaClient.zip`;
   - `changelog.txt`.

## 3. Проверка на сервере

1. Запустить `Replica.Api`.
2. Проверить доступность feed:
   - `GET http://<LAN-IP>:5000/updates/update.xml` -> `200`.
3. Проверить, что `ReplicaClient.zip` скачивается по URL из `update.xml`.

## 4. Проверка на клиенте

1. В клиенте включить LAN PostgreSQL режим и корректный `LAN API base URL`.
2. Перезапустить клиент.
3. На старте клиент должен проверить `.../updates/update.xml`.
4. На пилотном ПК подтвердить успешное обновление.

## 5. Rollout

1. Сначала 1 пилотный ПК.
2. Затем остальные ПК по очереди.
3. Если обнаружен дефект, откатить `update.xml` на предыдущую стабильную версию.

<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
# OLV Fork Notes (2026-04-03)

## Что сделано
1. NuGet `ObjectListView.Repack.Core3` заменен на локальный fork-источник:
`lib/ObjectListViewRepack/cs/trunk/ObjectListView/ObjectListView2019Core3.csproj`.
2. В `Replica.csproj` удален `PackageReference` на OLV и добавлен `ProjectReference` на локальный fork.

## Патч в форке
Файл: `lib/ObjectListViewRepack/cs/trunk/ObjectListView/TreeListView.cs`  
Метод: `EnsureTreeRendererPresent(TreeRenderer renderer)`

Добавлено поведение:
1. Если задан `renderer.Column` и колонка присутствует в `Columns`, tree-renderer закрепляется именно за этой колонкой.
2. Tree-renderer снимается с других колонок.
3. Старое поведение (`column 0`) используется только как fallback.

## Зачем нужен патч
Без патча `TreeListView` принудительно назначал `TreeRenderer` в первую колонку, что затирало кастомный renderer статуса и мешало добиться визуального паритета со старой таблицей.

## Важно
`lib/**` исключен из компиляции основного приложения как исходники (`Compile Remove`), чтобы файлы форка не попадали в `Replica` напрямую.

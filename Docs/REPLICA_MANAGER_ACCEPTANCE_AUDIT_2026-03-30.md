# Replica Manager Acceptance Audit (2026-03-30)

Status: Draft for review (fixes are not applied yet)
Date: 2026-03-30
Role model: "typography manager as end-user"

## 1. Goal

Compare expected behavior (from roadmap/checklists) with current runtime behavior and record mismatches in one place.

This document records findings only.
No code fixes are included in this step.

## 2. Baseline (expected behavior)

Reference sources used for expectation baseline:

1. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/00_REPLICA_MASTER_DOC_MAP_2026-03-26.md`
2. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md`
3. `Docs/archive/2026-03-26_pre_new_architecture/ready/1_SINGLE_ORDER_REGRESSION_CHECKLIST.md`
4. Existing regression suites in `tests/Replica.VerifyTests` and `tests/Replica.UiSmokeTests`

## 3. Test run scope

## 3.1 Commands executed

```powershell
dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj --no-build --filter "FullyQualifiedName~OrderEditRunWorkflowSmokeTests|FullyQualifiedName~OrderDeletionWorkflowServiceTests|FullyQualifiedName~OrderFileRenameRemoveCommandServiceTests|FullyQualifiedName~OrderEditorMutationServiceTests|FullyQualifiedName~OrderItemDeleteCommandServiceTests"
dotnet test tests/Replica.UiSmokeTests/Replica.UiSmokeTests.csproj --no-build --filter "FullyQualifiedName~MainFormCoreRegressionTests|FullyQualifiedName~MainFormSmokeTests" --logger "trx;LogFileName=ui-smoke-manager-acceptance-2026-03-30.trx"
```

## 3.2 Result summary

1. `Replica.VerifyTests` subset: 27 passed, 0 failed.
2. `Replica.UiSmokeTests` subset: 21 passed, 14 failed.
3. UI smoke evidence file:
`tests/Replica.UiSmokeTests/TestResults/ui-smoke-manager-acceptance-2026-03-30.trx`

## 3.3 Scenarios explicitly requested by owner

Requested scenarios and current status:

1. Create order, assign number/date, edit number/date: Covered by
`OrderEditRunWorkflowSmokeTests` (`passed`).
2. Add files to preparation by multiple paths: Covered by
`OrderEditRunWorkflowSmokeTests` (`passed`).
3. Delete files (physical and logical, preparation/print): Covered by
`OrderDeletionWorkflowServiceTests` + workflow tests (`passed`).
4. Replace print file with another: Covered by
`OrderEditRunWorkflowSmokeTests` + rename/remove tests (`passed`).
5. Move print file to archive path: Covered by
`OrderEditRunWorkflowSmokeTests` (`passed`).

Note: this passed block is service/API-level acceptance.
UI-level acceptance still has open deviations below.

## 4. Found mismatches (UI acceptance)

## 4.1 Critical block: Group-order UI flow unstable in manager regression

Priority: High
Scope: Expand/collapse, item row selection/context, header/item rendering, drag-drop

Failed SR tests:

1. `SR13_GroupOrder_ExpandCollapse_ShowsAndHidesItemRows`
2. `SR15_GroupOrder_ItemSelection_DisablesAddFile_AndActionsStayAtContainerLevel`
3. `SR17_GroupOrder_ItemRowSelection_IsDetectedAsItemDeletionTarget_NotOrderContainer`
4. `SR18_GroupOrder_HeaderRow_DoesNotMirrorFirstItemStageFields`
5. `SR19_GroupOrder_ItemContext_ResolvesOrderAndItem`
6. `SR20_GroupOrder_ItemRows_UseLightFolderZebraPalette`
7. `SR21_GroupStatus_IsTechnicalAndHiddenFromUiFilters`
8. `SR22_GroupOrder_RowClick_TogglesOnlyWhenRowWasAlreadySelected`
9. `SR24_GroupOrder_DragDrop_ToSingleOrder_MovesSourceAndClearsOrigin`
10. `SR25_MissingFileCell_ShowsRedText_WithoutChangingBackground`

Observed symptoms:

1. Multiple tests fail with `Sequence contains no matching element`.
2. Baseline row expectation breaks (`expected single row`, actual collection contains many rows).

Business impact:

1. Risk of incorrect manager behavior in multi-file/group order daily operations.
2. UI predictability for selection, drag/drop, and visual feedback is not guaranteed by current acceptance run.

## 4.2 High: Drag-drop copy between item rows mismatch

Priority: High
Test: `SR23_GroupOrder_DragDrop_BetweenItems_CopiesWithoutClearingSource`

Expected:
`2` source files copied into target item flow.

Actual:
`0` files in assertion path.

Business impact:
Potential file movement/copy inconsistency for production handoff between items.

## 4.3 Medium: Filter behavior mismatch

Priority: Medium
Test: `SR08_SR09_SR10_Filters_WorkForStatusUserOrderAndDates`

Expected:
Filtering by status/user/date returns deterministic rows (example expected `["2002"]`).

Actual:
Empty result (`[]`) for expected case.

Business impact:
Manager cannot trust filter panel for operational triage.

## 4.4 Medium: Tray alert count mismatch

Priority: Medium
Test: `SR11_TrayIndicators_UpdateStatsConnectionDiskErrorsAndProgress`

Expected:
Alert indicator contains `1`.

Actual:
Alert indicator contains `2`.

Business impact:
Potentially noisy/wrong operational signal in tray status.

## 4.5 Medium: Probe diagnostics fallback mismatch

Priority: Medium
Test: `SR12G_ProbeLanServer_PushDiagnosticsOptionalFallback_DoesNotBreakSnapshot(pushDiagnosticsStatusCode: 404)`

Expected:
Snapshot value `-1` in this fallback branch.

Actual:
Snapshot value `9`.

Business impact:
Probe health interpretation may diverge from documented fallback behavior.

## 5. Technical note (important for triage)

There is a strong signal that part of the UI smoke suite is not fully isolated from live LAN runtime:

1. `MainFormTestHarness` creates `new AppSettings` without explicit override of `OrdersStorageBackend`/`LanApiBaseUrl` (`tests/Replica.UiSmokeTests/MainFormTestHarness.cs:193`).
2. Defaults are LAN mode and `http://localhost:5000/` (`Models/AppSettings.cs:87`, `Models/AppSettings.cs:89`, `Models/AppSettings.cs:46`).

This can mix local acceptance with live server state and inflate row-based assertions.
So for each mismatch above, triage should mark:

1. `Confirmed product bug`, or
2. `Test-isolation defect`, or
3. `Combined`.

## 6. Proposed next step (requires owner approval)

If approved, next pass will be:

1. Stabilize acceptance environment isolation for UI smoke (without changing business behavior yet).
2. Re-run full manager acceptance pack.
3. Produce confirmed bug list with direct fix plan per item (one-by-one with your sign-off).

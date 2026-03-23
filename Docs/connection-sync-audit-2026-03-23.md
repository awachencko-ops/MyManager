# Connection And Sync Audit

Date: 2026-03-23

## Scope

Checked the main connection and synchronization paths in the repository:

- WinForms client LAN health probing and local API recovery
- Client direct PostgreSQL history load/save
- API write/run commands over HTTP
- PostgreSQL and `history.json` mirroring/fallback logic
- Users directory source/cache/fallback refresh
- Version refresh between storage and in-memory models

## Current topology

In `LanPostgreSql` mode the application uses two separate transport paths at the same time:

1. UI connection status is based on HTTP probes to `Replica.Api` (`/live`, `/ready`, `/slo`).
2. History load/save still talks directly to PostgreSQL from the desktop client.

That means "API is reachable" and "state is synchronized" are not the same condition.

## Confirmed findings

### 1. Health probe is overly sensitive to transient latency and can report false disconnects

Files:

- [OrdersWorkspaceForm.State.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.State.cs#L40)
- [OrdersWorkspaceForm.StatusStrip.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.StatusStrip.cs#L307)
- [OrdersWorkspaceForm.StatusStrip.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.StatusStrip.cs#L434)
- [OrdersWorkspaceForm.StatusStrip.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.StatusStrip.cs#L683)

What happens:

- The probe client timeout is only `5s`.
- The client probes `live`, then `ready`, then `slo` sequentially.
- The status indicator marks the server as disconnected when `!snapshot.ApiReachable || !snapshot.IsReady`.

Impact:

- A short PostgreSQL stall or slow `/ready` response is enough to flip the UI into `↻ Сервер: не подключен`.
- This matches the reported symptom "connection drops from time to time" even when the API process itself is still alive.

Evidence:

- `.codex_api_stdout.log` contains `Now listening on: http://[::]:5000` and `Application started.`
- So at least part of the observed instability is likely detection instability, not only process death.

Recommendation:

- Probe endpoints in parallel, not sequentially.
- Use separate thresholds for "API process reachable" and "DB ready".
- Add hysteresis: require 2 consecutive failures before showing disconnected.
- Raise probe timeout or make it endpoint-specific.

### 2. Repository snapshot refresh updates only order versions, not item versions

Files:

- [OrderStorageVersionSyncService.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\Application\Services\OrderStorageVersionSyncService.cs#L9)
- [OrdersWorkspaceForm.OrdersLifecycle.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.OrdersLifecycle.cs#L855)

What happens:

- `TryRefreshRepositorySnapshotFromStorage` reloads storage state.
- The sync service copies only `OrderData.StorageVersion`.
- `OrderFileItem.StorageVersion` is never refreshed.

Impact:

- After external changes or partial refreshes, local item versions stay stale.
- Next item update/delete can fail with version conflicts even though the client "refreshed from storage".

Recommendation:

- Extend version sync to update item versions by `orderInternalId + itemId`.
- Add a regression test for multi-item orders where only item versions change.

### 3. PostgreSQL failures silently fork state into `history.json`

Files:

- [OrdersHistoryRepositoryCoordinator.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\Application\Services\OrdersHistoryRepositoryCoordinator.cs#L36)
- [OrdersHistoryRepositoryCoordinator.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\Application\Services\OrdersHistoryRepositoryCoordinator.cs#L73)

What happens:

- On PostgreSQL load failure, the coordinator silently falls back to `history.json` and returns success.
- On PostgreSQL save failure, it falls back to `history.json` for all errors except explicit concurrency conflicts.

Impact:

- The app can keep working against stale file data while the API and other clients keep writing to PostgreSQL.
- This creates split-brain behavior between LAN state and local file state.
- Later sync may merge only by missing order IDs, not by newest version or last writer.

Why this matters:

- This is the highest-risk synchronization problem in the current design.
- It can preserve user workflow short-term, but it also hides infrastructure problems and makes divergence harder to detect.

Recommendation:

- In `LanPostgreSql` mode, do not silently downgrade to file save/load unless the user explicitly chooses offline mode.
- Surface a visible "degraded/offline mirror mode" state in UI if fallback is unavoidable.
- If fallback remains, write an explicit conflict journal instead of pretending sync is healthy.

### 4. Local API recovery can miss already running `dotnet Replica.Api.dll` processes

Files:

- [OrdersWorkspaceForm.StatusStrip.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.StatusStrip.cs#L589)
- [OrdersWorkspaceForm.StatusStrip.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.StatusStrip.cs#L628)

What happens:

- Recovery checks only `Process.GetProcessesByName("Replica.Api")`.
- But the fallback launch path starts the API as `dotnet "Replica.Api.dll"`.
- In that case the process name is normally `dotnet`, not `Replica.Api`.

Impact:

- Manual recovery can attempt to start a second API instance even when one is already running.
- That can cause port-bind failures or confusing "recovery" behavior.

Recommendation:

- Detect listeners by probing the port first.
- Or inspect `dotnet` command line / main module path before launching another instance.

### 5. API bind settings are effectively hardcoded to port 5000

File:

- [Replica.Api/Program.cs](C:\Users\user\Desktop\MyManager 1.0.1\Replica.Api\Program.cs#L15)

What happens:

- `appsettings.json` contains `ReplicaApi:BindAddress` and `ReplicaApi:Port`.
- The runtime ignores them and always does `options.ListenAnyIP(5000)`.

Impact:

- If client settings or deployment expect another port, the API and client can drift.
- This is a configuration integrity problem and makes troubleshooting harder.

Recommendation:

- Read bind address and port from configuration.
- Validate mismatch between API bind settings and client `LanApiBaseUrl` on startup.

## Secondary risks

### Background timer can trigger storage writes unrelated to user actions

Files:

- [OrdersWorkspaceForm.StatusStrip.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.StatusStrip.cs#L106)
- [OrdersWorkspaceForm.UsersDirectory.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.UsersDirectory.cs#L49)

Notes:

- The tray timer can call `SaveHistory()` indirectly from hash backfill and users refresh.
- In LAN mode that means PostgreSQL writes may happen in the background even when the user is idle.
- This increases the chance of conflicts and makes failures feel "random".

### Connection status reflects API health, not end-to-end sync health

Files:

- [OrdersWorkspaceForm.StatusStrip.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.StatusStrip.cs#L307)
- [OrdersWorkspaceForm.OrdersLifecycle.cs](C:\Users\user\Desktop\MyManager 1.0.1\Features\Orders\UI\OrdersWorkspace\Core\OrdersWorkspaceForm.OrdersLifecycle.cs#L181)

Notes:

- UI status is green/red based on HTTP probe results.
- Actual history persistence may still fail independently in the client-side PostgreSQL repository path.

## Most likely explanation for the intermittent drop symptom

The reported "connection falls from time to time" is likely a mix of two categories:

1. Real transient readiness failures:
   - PostgreSQL stalls or temporary DB unavailability make `/ready` fail.
2. Detection-side false negatives:
   - Sequential probes with a `5s` timeout make the UI flip to disconnected during short slowdowns.

This explains why the UI can show disconnects while the API process itself is still up.

## Recommended fix order

1. Stabilize health detection:
   - parallel probing, longer timeout budget, consecutive-failure threshold, explicit `api-up` vs `db-ready`.
2. Stop silent split-brain fallback in LAN mode:
   - make fallback visible and explicit.
3. Sync item versions during storage refresh:
   - otherwise conflicts will continue after recovery.
4. Fix localhost recovery process detection:
   - avoid duplicate `dotnet` starts.
5. Align API bind config with client settings:
   - eliminate port/config drift.

## Verification notes

Verified locally:

- API startup log shows the server has successfully listened on port `5000`.
- Existing targeted test run for broader sync/gateway coverage was partially blocked because `Replica.exe` is currently running and holding the build output file.
- This means some validation remains blocked until the running desktop app is closed.

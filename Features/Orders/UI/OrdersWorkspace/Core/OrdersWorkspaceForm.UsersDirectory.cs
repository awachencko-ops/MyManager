using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void InitializeUsersDirectory()
        {
            _usersDirectoryLastRefreshAt = DateTime.MinValue;
            RefreshUsersDirectory(forceRefresh: true, refreshGrid: false);
        }

        private void RefreshUsersDirectoryIfNeeded()
        {
            RefreshUsersDirectory(forceRefresh: false, refreshGrid: true);
        }

        private void RefreshUsersDirectory(bool forceRefresh, bool refreshGrid)
        {
            QueueUsersDirectoryRefresh(forceRefresh, refreshGrid);
        }

        private async void QueueUsersDirectoryRefresh(bool forceRefresh, bool refreshGrid)
        {
            try
            {
                await RefreshUsersDirectoryCoreAsync(forceRefresh, refreshGrid);
            }
            catch (Exception ex)
            {
                Logger.Warn($"USERS-DIRECTORY | refresh-failed | {ex.Message}");
            }
        }

        private async Task RefreshUsersDirectoryCoreAsync(bool forceRefresh, bool refreshGrid)
        {
            if (Interlocked.CompareExchange(ref _usersDirectoryRefreshInProgress, 1, 0) != 0)
                return;

            var timer = Stopwatch.StartNew();
            var refreshSource = "unknown";

            try
            {
                var nowUtc = DateTime.UtcNow;
                if (!forceRefresh
                    && nowUtc - _usersDirectoryLastRefreshAt < TimeSpan.FromMilliseconds(UsersDirectoryRefreshIntervalMs))
                {
                    return;
                }

                _usersDirectoryLastRefreshAt = nowUtc;

                var fallbackUsers = _filterUsers.Count > 0
                    ? _filterUsers
                    : _users;
                var fallbackUsersSnapshot = fallbackUsers.ToList();

                UsersDirectoryService.LoadResult loadResult;
                var lanBackendEnabled = _ordersStorageBackend == OrdersStorageMode.LanPostgreSql;
                if (lanBackendEnabled)
                {
                    var apiLoadResult = await TryLoadUsersDirectoryFromLanApiAsync();
                    if (apiLoadResult.IsSuccess)
                    {
                        refreshSource = "lan-api";
                        loadResult = new UsersDirectoryService.LoadResult
                        {
                            Users = apiLoadResult.Users,
                            ServerUsersByDisplayName = apiLoadResult.ServerUsersByDisplayName,
                            LoadedFromSource = true,
                            LoadedFromCache = false,
                            StatusText = apiLoadResult.StatusText
                        };
                    }
                    else
                    {
                        refreshSource = "lan-api-fallback";
                        var statusText = string.IsNullOrWhiteSpace(apiLoadResult.StatusText)
                            ? "Пользователи: API недоступен, список не обновлен"
                            : apiLoadResult.StatusText;
                        loadResult = new UsersDirectoryService.LoadResult
                        {
                            Users = fallbackUsersSnapshot.ToList(),
                            ServerUsersByDisplayName = BuildDefaultServerUsersMap(fallbackUsersSnapshot),
                            LoadedFromSource = false,
                            LoadedFromCache = false,
                            StatusText = statusText
                        };
                    }
                }
                else
                {
                    refreshSource = "files";
                    loadResult = await Task.Run(() =>
                        UsersDirectoryService.Load(_usersSourceFilePath, _usersCacheFilePath, fallbackUsersSnapshot));
                }

                if (Disposing || IsDisposed)
                    return;

                _usersLoadedFromCache = loadResult.LoadedFromCache;
                _usersDirectoryStatusText = loadResult.StatusText;

                var nextUsers = loadResult.Users.Count > 0
                    ? loadResult.Users
                    : fallbackUsersSnapshot;
                var nextServerUsers = loadResult.ServerUsersByDisplayName.Count > 0
                    ? loadResult.ServerUsersByDisplayName
                    : BuildDefaultServerUsersMap(nextUsers);

                RefreshCurrentUserProfile(forceRefresh);
                if (AreUserListsEqual(_filterUsers, nextUsers) && AreUserMappingsEqual(_serverUsersByDisplayName, nextServerUsers))
                    return;

                _users.Clear();
                _users.AddRange(nextUsers);
                _filterUsers.Clear();
                _filterUsers.AddRange(nextUsers);
                _serverUsersByDisplayName.Clear();
                foreach (var entry in nextServerUsers)
                    _serverUsersByDisplayName[entry.Key] = entry.Value;

                var selectedUsersChanged = RemoveUnavailableSelectedUsers();
                var historyChanged = NormalizeOrderUsersInHistory();
                if (historyChanged)
                    SaveHistory();

                if (refreshGrid || selectedUsersChanged || historyChanged)
                    HandleOrdersGridChanged();
                else
                    RefreshUserFilterChecklist();
            }
            finally
            {
                timer.Stop();
                Interlocked.Exchange(ref _usersDirectoryRefreshInProgress, 0);
                if (timer.Elapsed.TotalMilliseconds >= 350d)
                {
                    Logger.Warn(
                        $"UI-PERF | op=users-directory-refresh | source={refreshSource} | elapsedMs={timer.Elapsed.TotalMilliseconds:F1}");
                }
            }
        }

        private async Task<(bool IsSuccess, IReadOnlyList<string> Users, IReadOnlyDictionary<string, string> ServerUsersByDisplayName, string StatusText)> TryLoadUsersDirectoryFromLanApiAsync()
        {
            if (_ordersStorageBackend != OrdersStorageMode.LanPostgreSql || !ShouldUseLanRunApi())
            {
                return (false, Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "Пользователи: LAN API выключен");
            }

            if (!TryResolveLanApiBaseUri(_lanApiBaseUrl, out var baseUri))
            {
                return (false, Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "Пользователи: API URL не задан");
            }

            try
            {
                var usersUri = new Uri(baseUri, "api/users");
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, usersUri);
                AddLanApiActorHeaders(request, ResolveLanApiActor());
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));
                using var response = await _lanStatusHttpClient.SendAsync(request, timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), $"Пользователи: API вернул {(int)response.StatusCode}");
                }

                var payload = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var apiUsers = System.Text.Json.JsonSerializer.Deserialize<List<LanApiUserContract>>(
                    payload,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<LanApiUserContract>();

                var userList = new List<string>();
                var userMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var apiUser in apiUsers)
                {
                    if (apiUser == null || !apiUser.IsActive || string.IsNullOrWhiteSpace(apiUser.Name))
                        continue;

                    var normalizedName = apiUser.Name.Trim();
                    if (!userMap.ContainsKey(normalizedName))
                        userList.Add(normalizedName);

                    userMap[normalizedName] = normalizedName;
                }

                if (userList.Count == 0)
                {
                    return (false, Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "Пользователи: API вернул пустой список");
                }

                return (true, userList, userMap, $"Пользователи: API ({usersUri.Host}:{usersUri.Port})");
            }
            catch (OperationCanceledException)
            {
                return (false, Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "Пользователи: API timeout");
            }
            catch
            {
                return (false, Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "Пользователи: API недоступен");
            }
        }
        private string GetDefaultUserName()
        {
            if (!string.IsNullOrWhiteSpace(_currentUserName))
            {
                foreach (var configuredUser in _filterUsers)
                {
                    if (string.Equals(configuredUser, _currentUserName, StringComparison.OrdinalIgnoreCase))
                        return configuredUser;
                }
            }

            if (_filterUsers.Count > 0)
                return _filterUsers[0];

            if (_users.Count > 0)
                return _users[0];

            return UserIdentityResolver.DefaultDisplayName;
        }

        private static void AddLanApiActorHeaders(System.Net.Http.HttpRequestMessage request, string actor)
        {
            if (request == null || string.IsNullOrWhiteSpace(actor))
                return;

            var normalizedActor = actor.Trim();
            if (CurrentUserHeaderCodec.RequiresEncoding(normalizedActor))
            {
                request.Headers.TryAddWithoutValidation(
                    CurrentUserHeaderCodec.HeaderName,
                    CurrentUserHeaderCodec.BuildAsciiFallback(normalizedActor));
                request.Headers.TryAddWithoutValidation(
                    CurrentUserHeaderCodec.EncodedHeaderName,
                    CurrentUserHeaderCodec.Encode(normalizedActor));
                return;
            }

            request.Headers.TryAddWithoutValidation(CurrentUserHeaderCodec.HeaderName, normalizedActor);
        }

        private bool NormalizeOrderUsersInHistory()
        {
            var changed = false;
            foreach (var order in _orderHistory.Where(x => x != null))
            {
                var normalized = NormalizeOrderUserName(order.UserName);
                if (string.Equals(order.UserName, normalized, StringComparison.Ordinal))
                    continue;

                order.UserName = normalized;
                changed = true;
            }

            return changed;
        }

        private string NormalizeOrderUserName(string? rawUserName)
        {
            if (string.IsNullOrWhiteSpace(rawUserName))
                return GetDefaultUserName();

            var normalized = rawUserName.Trim();
            foreach (var configuredUser in _filterUsers)
            {
                if (string.Equals(configuredUser, normalized, StringComparison.OrdinalIgnoreCase))
                    return configuredUser;
            }

            return normalized;
        }

        private static Dictionary<string, string> BuildDefaultServerUsersMap(IEnumerable<string> users)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in users ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(user))
                    continue;

                var displayName = user.Trim();
                map[displayName] = UserIdentityResolver.ResolveServerUserName(displayName);
            }

            return map;
        }

        private bool RemoveUnavailableSelectedUsers()
        {
            if (_selectedFilterUsers.Count == 0)
                return false;

            var changed = false;
            foreach (var userName in _selectedFilterUsers.ToList())
            {
                if (_filterUsers.Any(x => string.Equals(x, userName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _selectedFilterUsers.Remove(userName);
                changed = true;
            }

            return changed;
        }

        private static bool AreUserListsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
                return false;

            for (var index = 0; index < left.Count; index++)
            {
                if (string.Equals(left[index], right[index], StringComparison.OrdinalIgnoreCase))
                    continue;

                return false;
            }

            return true;
        }

        private static bool AreUserMappingsEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        {
            if (left.Count != right.Count)
                return false;

            foreach (var entry in left)
            {
                if (!right.TryGetValue(entry.Key, out var rightValue))
                    return false;

                if (!string.Equals(entry.Value, rightValue, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private sealed class LanApiUserContract
        {
            public string Name { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }
    }
}




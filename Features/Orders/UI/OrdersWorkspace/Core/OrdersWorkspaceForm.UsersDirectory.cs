using System;
using System.Collections.Generic;
using System.Linq;

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
            var nowUtc = DateTime.UtcNow;
            if (!forceRefresh &&
                nowUtc - _usersDirectoryLastRefreshAt < TimeSpan.FromMilliseconds(UsersDirectoryRefreshIntervalMs))
            {
                return;
            }

            _usersDirectoryLastRefreshAt = nowUtc;

            var fallbackUsers = _filterUsers.Count > 0
                ? _filterUsers
                : _users;

            var loadResult = UsersDirectoryService.Load(_usersSourceFilePath, _usersCacheFilePath, fallbackUsers);
            _usersLoadedFromCache = loadResult.LoadedFromCache;
            _usersDirectoryStatusText = loadResult.StatusText;

            var nextUsers = loadResult.Users.Count > 0
                ? loadResult.Users
                : fallbackUsers;
            var nextServerUsers = loadResult.ServerUsersByDisplayName.Count > 0
                ? loadResult.ServerUsersByDisplayName
                : BuildDefaultServerUsersMap(nextUsers);
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
    }
}


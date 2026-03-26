using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Replica
{
    internal static class UsersDirectoryService
    {
        internal sealed class UserEntry
        {
            public string DisplayName { get; init; } = string.Empty;
            public string ServerName { get; init; } = string.Empty;
        }

        internal sealed class LoadResult
        {
            public IReadOnlyList<string> Users { get; init; } = Array.Empty<string>();
            public IReadOnlyDictionary<string, string> ServerUsersByDisplayName { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public bool LoadedFromSource { get; init; }
            public bool LoadedFromCache { get; init; }
            public string StatusText { get; init; } = string.Empty;
        }

        public static LoadResult Load(string sourceFilePath, string cacheFilePath, IEnumerable<string> fallbackUsers)
        {
            var fallback = NormalizeEntries(fallbackUsers?.Select(userName => CreateUserEntry(userName, null)));

            if (TryReadUsersFile(sourceFilePath, out var sourceUsers, out var sourceError))
            {
                if (sourceUsers.Count == 0)
                {
                    WriteCache(cacheFilePath, fallback);
                    return new LoadResult
                    {
                        Users = fallback.Select(entry => entry.DisplayName).ToList(),
                        ServerUsersByDisplayName = ToServerUsersMap(fallback),
                        LoadedFromSource = true,
                        StatusText = $"Пользователи: источник пуст, использован fallback ({Path.GetFileName(sourceFilePath)})"
                    };
                }

                WriteCache(cacheFilePath, sourceUsers);
                return new LoadResult
                {
                    Users = sourceUsers.Select(entry => entry.DisplayName).ToList(),
                    ServerUsersByDisplayName = ToServerUsersMap(sourceUsers),
                    LoadedFromSource = true,
                    StatusText = $"Пользователи: источник ({Path.GetFileName(sourceFilePath)})"
                };
            }

            if (TryReadUsersFile(cacheFilePath, out var cacheUsers, out var cacheError) && cacheUsers.Count > 0)
            {
                return new LoadResult
                {
                    Users = cacheUsers.Select(entry => entry.DisplayName).ToList(),
                    ServerUsersByDisplayName = ToServerUsersMap(cacheUsers),
                    LoadedFromCache = true,
                    StatusText = $"Пользователи: кэш ({Path.GetFileName(cacheFilePath)})"
                };
            }

            var sourceReason = string.IsNullOrWhiteSpace(sourceError) ? "нет доступа к источнику" : sourceError;
            var cacheReason = string.IsNullOrWhiteSpace(cacheError) ? "кэш недоступен" : cacheError;
            return new LoadResult
            {
                Users = fallback.Select(entry => entry.DisplayName).ToList(),
                ServerUsersByDisplayName = ToServerUsersMap(fallback),
                StatusText = $"Пользователи: fallback ({sourceReason}; {cacheReason})"
            };
        }

        private static bool TryReadUsersFile(string? filePath, out List<UserEntry> users, out string? error)
        {
            users = [];
            error = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "путь не задан";
                return false;
            }

            try
            {
                var resolvedPath = Path.GetFullPath(filePath);
                if (!File.Exists(resolvedPath))
                {
                    error = "файл не найден";
                    return false;
                }

                var json = File.ReadAllText(resolvedPath);
                users = ParseUsersWithServerNames(json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static List<string> ParseUsers(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            using var document = JsonDocument.Parse(json);
            return NormalizeEntries(ExtractUsers(document.RootElement))
                .Select(entry => entry.DisplayName)
                .ToList();
        }

        private static List<UserEntry> ParseUsersWithServerNames(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            using var document = JsonDocument.Parse(json);
            return NormalizeEntries(ExtractUsers(document.RootElement));
        }

        private static IEnumerable<UserEntry> ExtractUsers(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    foreach (var value in ExtractUsers(child))
                        yield return value;
                }

                yield break;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return CreateUserEntry(value, null);

                yield break;
            }

            if (element.ValueKind != JsonValueKind.Object)
                yield break;

            if (TryGetPropertyIgnoreCase(element, "users", out var usersProperty))
            {
                foreach (var value in ExtractUsers(usersProperty))
                    yield return value;

                yield break;
            }

            if (TryGetPropertyIgnoreCase(element, "items", out var itemsProperty))
            {
                foreach (var value in ExtractUsers(itemsProperty))
                    yield return value;

                yield break;
            }

            var userName = TryGetStringProperty(element, "name")
                ?? TryGetStringProperty(element, "userName")
                ?? TryGetStringProperty(element, "username")
                ?? TryGetStringProperty(element, "displayName")
                ?? TryGetStringProperty(element, "fullName")
                ?? TryGetStringProperty(element, "title");
            var serverName = TryGetStringProperty(element, "serverName")
                ?? TryGetStringProperty(element, "serverUserName")
                ?? TryGetStringProperty(element, "serverLogin")
                ?? TryGetStringProperty(element, "login")
                ?? TryGetStringProperty(element, "actor");
            if (!string.IsNullOrWhiteSpace(userName))
                yield return CreateUserEntry(userName, serverName);
        }

        private static string? TryGetStringProperty(JsonElement element, string propertyName)
        {
            if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
                return null;

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement property)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var candidate in element.EnumerateObject())
                {
                    if (!string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    property = candidate.Value;
                    return true;
                }
            }

            property = default;
            return false;
        }

        private static List<UserEntry> NormalizeEntries(IEnumerable<UserEntry>? users)
        {
            var normalizedUsers = new List<UserEntry>();
            var uniqueUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var user in users ?? Enumerable.Empty<UserEntry>())
            {
                if (user == null || string.IsNullOrWhiteSpace(user.DisplayName))
                    continue;

                var normalizedUser = CreateUserEntry(user.DisplayName, user.ServerName);
                if (uniqueUsers.Add(normalizedUser.DisplayName))
                    normalizedUsers.Add(normalizedUser);
            }

            return normalizedUsers;
        }

        private static UserEntry CreateUserEntry(string? displayName, string? serverName)
        {
            var normalizedDisplayName = UserIdentityResolver.ResolveDisplayUserName(displayName);
            return new UserEntry
            {
                DisplayName = normalizedDisplayName,
                ServerName = UserIdentityResolver.ResolveServerUserName(normalizedDisplayName, serverName)
            };
        }

        private static IReadOnlyDictionary<string, string> ToServerUsersMap(IEnumerable<UserEntry> users)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in users)
                map[user.DisplayName] = user.ServerName;

            return map;
        }

        private static void WriteCache(string cacheFilePath, IReadOnlyCollection<UserEntry> users)
        {
            if (string.IsNullOrWhiteSpace(cacheFilePath))
                return;

            try
            {
                var resolvedPath = Path.GetFullPath(cacheFilePath);
                var cacheFolder = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(cacheFolder))
                    Directory.CreateDirectory(cacheFolder);

                var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(resolvedPath, json);
            }
            catch
            {
                // Cache write must not break the UI flow.
            }
        }
    }
}

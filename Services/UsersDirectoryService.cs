using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Replica
{
    internal static class UsersDirectoryService
    {
        internal sealed class LoadResult
        {
            public IReadOnlyList<string> Users { get; init; } = Array.Empty<string>();
            public bool LoadedFromSource { get; init; }
            public bool LoadedFromCache { get; init; }
            public string StatusText { get; init; } = string.Empty;
        }

        public static LoadResult Load(string sourceFilePath, string cacheFilePath, IEnumerable<string> fallbackUsers)
        {
            var fallback = NormalizeUsers(fallbackUsers);

            if (TryReadUsersFile(sourceFilePath, out var sourceUsers, out var sourceError))
            {
                if (sourceUsers.Count == 0)
                {
                    WriteCache(cacheFilePath, fallback);
                    return new LoadResult
                    {
                        Users = fallback,
                        LoadedFromSource = true,
                        StatusText = $"Пользователи: источник пуст, использован fallback ({Path.GetFileName(sourceFilePath)})"
                    };
                }

                WriteCache(cacheFilePath, sourceUsers);
                return new LoadResult
                {
                    Users = sourceUsers,
                    LoadedFromSource = true,
                    StatusText = $"Пользователи: источник ({Path.GetFileName(sourceFilePath)})"
                };
            }

            if (TryReadUsersFile(cacheFilePath, out var cacheUsers, out var cacheError) && cacheUsers.Count > 0)
            {
                return new LoadResult
                {
                    Users = cacheUsers,
                    LoadedFromCache = true,
                    StatusText = $"Пользователи: кэш ({Path.GetFileName(cacheFilePath)})"
                };
            }

            var sourceReason = string.IsNullOrWhiteSpace(sourceError) ? "нет доступа к источнику" : sourceError;
            var cacheReason = string.IsNullOrWhiteSpace(cacheError) ? "кэш недоступен" : cacheError;
            return new LoadResult
            {
                Users = fallback,
                StatusText = $"Пользователи: fallback ({sourceReason}; {cacheReason})"
            };
        }

        private static bool TryReadUsersFile(string? filePath, out List<string> users, out string? error)
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
                users = ParseUsers(json);
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
            return NormalizeUsers(ExtractUsers(document.RootElement));
        }

        private static IEnumerable<string> ExtractUsers(JsonElement element)
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
                    yield return value.Trim();

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
            if (!string.IsNullOrWhiteSpace(userName))
                yield return userName.Trim();
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

        private static List<string> NormalizeUsers(IEnumerable<string> users)
        {
            var normalizedUsers = new List<string>();
            var uniqueUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var user in users ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(user))
                    continue;

                var normalizedUser = user.Trim();
                if (uniqueUsers.Add(normalizedUser))
                    normalizedUsers.Add(normalizedUser);
            }

            return normalizedUsers;
        }

        private static void WriteCache(string cacheFilePath, IReadOnlyCollection<string> users)
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

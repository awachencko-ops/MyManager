using System;
using System.Collections.Generic;

namespace Replica
{
    internal static class UserIdentityResolver
    {
        public const string DefaultDisplayName = "Андрей";
        public const string DefaultServerName = "Andrew";

        private static readonly IReadOnlyDictionary<string, string> KnownServerNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DefaultDisplayName] = DefaultServerName
            };

        public static string ResolveDisplayUserName(string? displayName)
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? DefaultDisplayName
                : displayName.Trim();
        }

        public static string ResolveServerUserName(string? displayName, string? explicitServerName = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitServerName))
                return explicitServerName.Trim();

            var normalizedDisplayName = ResolveDisplayUserName(displayName);
            return KnownServerNames.TryGetValue(normalizedDisplayName, out var serverUserName)
                ? serverUserName
                : normalizedDisplayName;
        }
    }
}

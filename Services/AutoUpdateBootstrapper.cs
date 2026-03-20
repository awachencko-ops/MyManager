using System;
using AutoUpdaterDotNET;

namespace Replica;

public static class AutoUpdateBootstrapper
{
    public static void TryStart(AppSettings settings)
    {
        if (!ShouldStart(settings))
            return;

        var manifestUrl = ResolveManifestUrl(settings!.LanApiBaseUrl);
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return;

        try
        {
            AutoUpdater.Start(manifestUrl);
        }
        catch
        {
            // Best-effort startup check: do not block app launch if update endpoint is unavailable.
        }
    }

    public static bool ShouldStart(AppSettings? settings)
    {
        if (settings == null)
            return false;

        return settings.OrdersStorageBackend == OrdersStorageMode.LanPostgreSql
            && !string.IsNullOrWhiteSpace(settings.LanApiBaseUrl);
    }

    public static string ResolveManifestUrl(string? lanApiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(lanApiBaseUrl))
            return string.Empty;

        if (!Uri.TryCreate(lanApiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return string.Empty;

        var normalizedPath = string.IsNullOrWhiteSpace(baseUri.AbsolutePath)
            ? "/updates/update.xml"
            : $"{baseUri.AbsolutePath.TrimEnd('/')}/updates/update.xml";

        var builder = new UriBuilder(baseUri)
        {
            Path = normalizedPath,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString();
    }
}

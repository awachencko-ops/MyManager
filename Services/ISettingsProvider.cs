using System;

namespace Replica;

public interface ISettingsProvider
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class FileSettingsProvider : ISettingsProvider
{
    public AppSettings Load()
    {
        return AppSettings.Load();
    }

    public void Save(AppSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        settings.Save();
    }
}

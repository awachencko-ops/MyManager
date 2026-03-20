using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ConfigServiceSettingsProviderTests
{
    [Fact]
    public void SavePitStopConfigs_UsesPathFromInjectedSettingsProvider()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-config-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var pitPath = Path.Combine(tempRoot, "custom-pitstop.json");

        var originalProvider = ConfigService.SettingsProvider;
        ConfigService.SettingsProvider = new TestSettingsProvider(new AppSettings
        {
            PitStopConfigFilePath = pitPath,
            ImposingConfigFilePath = Path.Combine(tempRoot, "imposing.json")
        });

        try
        {
            ConfigService.SavePitStopConfigs(new List<ActionConfig>
            {
                new() { Name = "pit-a" }
            });

            Assert.True(File.Exists(pitPath));
            var loaded = ConfigService.GetAllPitStopConfigs();
            Assert.Single(loaded);
            Assert.Equal("pit-a", loaded[0].Name);
        }
        finally
        {
            ConfigService.SettingsProvider = originalProvider;
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void SaveImposingConfigs_UsesPathFromInjectedSettingsProvider()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-config-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var imposingPath = Path.Combine(tempRoot, "custom-imposing.json");

        var originalProvider = ConfigService.SettingsProvider;
        ConfigService.SettingsProvider = new TestSettingsProvider(new AppSettings
        {
            PitStopConfigFilePath = Path.Combine(tempRoot, "pitstop.json"),
            ImposingConfigFilePath = imposingPath
        });

        try
        {
            ConfigService.SaveImposingConfigs(new List<ImposingConfig>
            {
                new() { Name = "imp-a" }
            });

            Assert.True(File.Exists(imposingPath));
            var loaded = ConfigService.GetAllImposingConfigs();
            Assert.Single(loaded);
            Assert.Equal("imp-a", loaded[0].Name);
        }
        finally
        {
            ConfigService.SettingsProvider = originalProvider;
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    private sealed class TestSettingsProvider(AppSettings settings) : ISettingsProvider
    {
        public AppSettings Load()
        {
            return settings;
        }

        public void Save(AppSettings nextSettings)
        {
            if (nextSettings == null)
                throw new ArgumentNullException(nameof(nextSettings));
        }
    }
}

using System.Text.Json;

namespace Dalamud.Configuration
{
    public interface IPluginConfiguration
    {
        int Version { get; set; }
    }
}

namespace Dalamud.Plugin
{
    public interface IDalamudPluginInterface
    {
        void SavePluginConfig(object configuration);
    }

    internal sealed class TestPluginInterface : IDalamudPluginInterface
    {
        public string? LastSavedJson { get; private set; }
        public void SavePluginConfig(object configuration) =>
            LastSavedJson = JsonSerializer.Serialize(configuration);
    }
}

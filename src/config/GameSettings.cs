using System.IO;
using System.Text.Json;
using Godot;

namespace TheUniversalEntertainmentSystem;
using TheUniversalEntertainmentSystem.API;

/// <summary>
/// Data Transfer Object for JSON serialization.
/// </summary>
public class GameSettingsData
{
    // Phase 0 validated at RD=8; default 16 balances visibility and scheduler cost.
    // Ultra (64+) requires Phase 2 LOD/batching optimizations.
    public int RenderDistance { get; set; } = 16;
    public int SimulationDistance { get; set; } = 2;
    public int WorldSeed { get; set; } = 1337;
    public string GeneratorType { get; set; } = "tues:default";
}

/// <summary>
/// A centralized global registry for core engine configurations.
/// Dictates Render Distance and Simulation Distance for highly scalable streaming.
/// Automatically persists to user://settings.json.
/// </summary>
public static class GameSettings
{
    private static GameSettingsData _data = new GameSettingsData();

    public static int RenderDistance => _data.RenderDistance;
    public static int SimulationDistance => _data.SimulationDistance;
    public static int WorldSeed => _data.WorldSeed;
    public static string GeneratorType => _data.GeneratorType;

    public static void Load()
    {
        string path = ProjectSettings.GlobalizePath("user://settings.json");

        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<GameSettingsData>(json);
                if (loaded != null)
                {
                    _data = loaded;
                    Logger.Info($"[GameSettings] Successfully loaded settings from {path}");
                    return;
                }
            }
            catch (System.Exception e)
            {
                Logger.Error($"[GameSettings] Failed to load settings.json: {e.Message}. Falling back to defaults.");
            }
        }
        
        // If file doesn't exist or loading fails, save the defaults so the user has a template
        Save();
    }

    public static void Save()
    {
        string path = ProjectSettings.GlobalizePath("user://settings.json");
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_data, options);
            
            // Ensure the user directory actually exists before writing
            string? directory = Path.GetDirectoryName(path);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json);
            Logger.Info($"[GameSettings] Saved settings to {path}");
        }
        catch (System.Exception e)
        {
            Logger.Error($"[GameSettings] Failed to save settings.json: {e.Message}");
        }
    }
}

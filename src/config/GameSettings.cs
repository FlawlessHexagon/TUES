using System.IO;
using System.Text.Json;
using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Data Transfer Object for JSON serialization.
/// </summary>
public class GameSettingsData
{
    public int RenderDistance { get; set; } = 64;
    public int SimulationDistance { get; set; } = 2;
    public int WorldSeed { get; set; } = 1337;
    public string GeneratorType { get; set; } = "simplex";
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
                    GD.Print($"[GameSettings] Successfully loaded settings from {path}");
                    return;
                }
            }
            catch (System.Exception e)
            {
                GD.PushError($"[GameSettings] Failed to load settings.json: {e.Message}. Falling back to defaults.");
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
            GD.Print($"[GameSettings] Saved settings to {path}");
        }
        catch (System.Exception e)
        {
            GD.PushError($"[GameSettings] Failed to save settings.json: {e.Message}");
        }
    }
}

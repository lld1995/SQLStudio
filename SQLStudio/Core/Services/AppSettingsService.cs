using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SQLStudio.Core.Database;

namespace SQLStudio.Core.Services;

public class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public KnowledgeSettings Knowledge { get; set; } = new();
}

public class DatabaseSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public DatabaseType DatabaseType { get; set; } = DatabaseType.MySQL;
    public string? SelectedDatabase { get; set; }
}

public class AiSettings
{
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string? SelectedModel { get; set; }
}

public class AppSettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SQLStudio");
    
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(SettingsFilePath, json);
        }
        catch (Exception)
        {
            // Silently fail - settings are not critical
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception)
        {
            // Silently fail
        }
    }
}

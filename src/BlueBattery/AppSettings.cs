using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueBattery;

public enum IconSource
{
    /// <summary>Das Tray-Icon zeigt den niedrigsten Akkustand aller Geräte.</summary>
    Lowest,
    /// <summary>Das Tray-Icon zeigt den Akkustand eines bestimmten Geräts.</summary>
    SpecificDevice,
}

public enum DisconnectedDisplay
{
    Grayed,
    Hidden,
}

/// <summary>Benutzereinstellungen, gespeichert unter %APPDATA%\BlueBattery\settings.json.</summary>
public sealed class AppSettings
{
    // Benachrichtigung
    public bool NotifyEnabled { get; set; } = true;
    public int NotifyThreshold { get; set; } = 25;
    public bool NotifyRepeat { get; set; }
    public int NotifyRepeatMinutes { get; set; } = 30;

    // Taskleistensymbol
    public bool ShowPercentOnIcon { get; set; } = true;
    public int CriticalThreshold { get; set; } = 15;
    public int LowThreshold { get; set; } = 25;
    public string CriticalColor { get; set; } = "#E53935";
    public string LowColor { get; set; } = "#FDD835";
    public string NormalColor { get; set; } = "#2E7D32";
    public IconSource IconSource { get; set; } = IconSource.Lowest;
    public Guid? IconDeviceId { get; set; }
    public string? IconDeviceName { get; set; }

    // Getrennte Geräte
    public DisconnectedDisplay DisconnectedDisplay { get; set; } = DisconnectedDisplay.Grayed;

    // Allgemein
    public int RefreshIntervalSeconds { get; set; } = 60;

    [JsonIgnore] public Color CriticalColorValue => ParseColor(CriticalColor, Color.Red);
    [JsonIgnore] public Color LowColorValue => ParseColor(LowColor, Color.Gold);
    [JsonIgnore] public Color NormalColorValue => ParseColor(NormalColor, Color.Green);

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ParseColor(string? value, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(value) ? fallback : ColorTranslator.FromHtml(value); }
        catch { return fallback; }
    }

    // ---- Laden / Speichern -------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueBattery", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (s is not null)
                    return s.Normalize();
            }
        }
        catch
        {
            // Defekte Datei: mit Standardwerten weitermachen.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    private AppSettings Normalize()
    {
        NotifyThreshold = Math.Clamp(NotifyThreshold, 1, 100);
        NotifyRepeatMinutes = Math.Clamp(NotifyRepeatMinutes, 1, 1440);
        CriticalThreshold = Math.Clamp(CriticalThreshold, 0, 99);
        LowThreshold = Math.Clamp(LowThreshold, CriticalThreshold + 1, 100);
        RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 10, 3600);
        return this;
    }
}

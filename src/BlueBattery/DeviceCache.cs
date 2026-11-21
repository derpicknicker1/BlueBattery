using System.Text.Json;

namespace BlueBattery;

/// <summary>
/// Merkt sich den zuletzt bekannten Akkustand jedes Geräts. Windows verwirft den Wert, sobald
/// ein Gerät getrennt wird – ohne diesen Speicher könnten getrennte Geräte nicht angezeigt werden.
/// Gespeichert unter %APPDATA%\BlueBattery\devices.json.
/// </summary>
internal sealed class DeviceCache
{
    private sealed record Entry(string Name, int Level, DateTime LastSeen);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueBattery", "devices.json");

    private readonly Dictionary<Guid, Entry> _entries;

    private DeviceCache(Dictionary<Guid, Entry> entries) => _entries = entries;

    public static DeviceCache Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var entries = JsonSerializer.Deserialize<Dictionary<Guid, Entry>>(File.ReadAllText(FilePath), JsonOptions);
                if (entries is not null)
                    return new DeviceCache(entries);
            }
        }
        catch
        {
            // Defekte Datei: leer beginnen.
        }
        return new DeviceCache([]);
    }

    /// <summary>
    /// Aktualisiert den Speicher mit den aktuellen Werten und liefert die vollständige Liste:
    /// aktuell gemeldete Geräte plus bekannte, inzwischen getrennte Geräte.
    /// </summary>
    public IReadOnlyList<DeviceBattery> Merge(IReadOnlyList<DeviceBattery> current)
    {
        var now = DateTime.Now;
        bool changed = false;
        var result = new List<DeviceBattery>();

        foreach (var d in current)
        {
            if (d.IsConnected)
            {
                _entries[d.ContainerId] = new Entry(d.Name, d.Level, now);
                changed = true;
                result.Add(d with { LastSeen = now });
            }
            else
            {
                // Windows kennt das Gerät noch, aber es ist getrennt (typisch bei Bluetooth LE).
                _entries.TryGetValue(d.ContainerId, out var entry);
                var lastSeen = entry?.LastSeen;
                if (entry is null || entry.Level != d.Level || entry.Name != d.Name)
                {
                    _entries[d.ContainerId] = new Entry(d.Name, d.Level, lastSeen ?? now);
                    changed = true;
                }
                result.Add(d with { LastSeen = lastSeen });
            }
        }

        var reported = current.Select(d => d.ContainerId).ToHashSet();
        foreach (var (id, entry) in _entries)
        {
            if (!reported.Contains(id))
                result.Add(new DeviceBattery(id, entry.Name, entry.Level, IsConnected: false, entry.LastSeen));
        }

        if (changed)
            TrySave();

        return result
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Level)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Vergisst alle Geräte, die gerade nicht verbunden sind.</summary>
    public void ForgetDisconnected(IEnumerable<DeviceBattery> devices)
    {
        foreach (var d in devices.Where(d => !d.IsConnected))
            _entries.Remove(d.ContainerId);
        TrySave();
    }

    private void TrySave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries, JsonOptions));
        }
        catch
        {
            // Nicht kritisch – beim nächsten Durchlauf erneut versuchen.
        }
    }
}

using Windows.Devices.Enumeration;

namespace BlueBattery;

/// <summary>Akkustand eines gekoppelten Bluetooth-Geräts.</summary>
public sealed record DeviceBattery(Guid ContainerId, string Name, int Level, bool IsConnected, DateTime? LastSeen = null);

/// <summary>
/// Liest die Akkustände, die Windows für Bluetooth-Geräte meldet (klassisches Bluetooth über
/// HFP und Bluetooth LE über den Battery Service). Windows legt den Wert in der
/// Geräteeigenschaft DEVPKEY_Bluetooth_Battery am jeweiligen Geräteknoten ab.
/// </summary>
public static class BluetoothBatteryReader
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ContainerIdKey = "System.Devices.ContainerId";

    private const string ContainerConnectedKey = "System.Devices.Connected";

    public static async Task<IReadOnlyList<DeviceBattery>> ReadAsync()
    {
        // 1. Alle vorhandenen Geräteknoten, die einen Akkustand melden.
        var nodes = await DeviceInformation.FindAllAsync(
            "System.Devices.Present:=System.StructuredQueryType.Boolean#True",
            [BatteryKey, ContainerIdKey],
            DeviceInformationKind.Device);

        var batteryByContainer = new Dictionary<Guid, (string Name, int Level)>();
        foreach (var node in nodes)
        {
            if (!node.Properties.TryGetValue(BatteryKey, out var raw) || raw is null)
                continue;
            if (!node.Properties.TryGetValue(ContainerIdKey, out var c) || c is not Guid container)
                continue;

            int level = Math.Clamp(Convert.ToInt32(raw), 0, 100);
            // Meldet ein Gerät über mehrere Knoten, zählt der niedrigste Wert.
            if (!batteryByContainer.TryGetValue(container, out var existing) || level < existing.Level)
                batteryByContainer[container] = (node.Name, level);
        }

        if (batteryByContainer.Count == 0)
            return [];

        // 2. Gerätecontainer für Anzeigename ("Flow Headset" statt "... Hands-Free AG") und
        //    Verbindungsstatus. Bewusst keine AssociationEndpoint-Abfrage: die stößt eine
        //    Bluetooth-Suche an und dauert ~30 s.
        var containers = await DeviceInformation.FindAllAsync(
            "",
            [ContainerConnectedKey],
            DeviceInformationKind.DeviceContainer);

        var containerInfo = new Dictionary<Guid, (string Name, bool Connected)>();
        foreach (var c in containers)
        {
            if (!Guid.TryParse(c.Id, out var id))
                continue;
            bool connected = c.Properties.TryGetValue(ContainerConnectedKey, out var v) && v is true;
            containerInfo[id] = (c.Name, connected);
        }

        // 3. Zusammenführen.
        var result = new List<DeviceBattery>();
        foreach (var (container, battery) in batteryByContainer)
        {
            if (containerInfo.TryGetValue(container, out var info))
            {
                string name = string.IsNullOrWhiteSpace(info.Name) ? battery.Name : info.Name;
                result.Add(new DeviceBattery(container, name, battery.Level, info.Connected));
            }
            else
            {
                // Kein passender Container gefunden: Gerät trotzdem anzeigen.
                result.Add(new DeviceBattery(container, battery.Name, battery.Level, IsConnected: true));
            }
        }

        return result
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Level)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

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
    public static Task<IReadOnlyList<DeviceBattery>> ReadAsync() => Task.Run(Read);

    private static IReadOnlyList<DeviceBattery> Read()
    {
        // 1. Alle vorhandenen Geräteknoten, die einen Akkustand melden.
        var nodes = DeviceQuery.GetObjects(DevObjectType.Device,
            DeviceQuery.BluetoothBattery, DeviceQuery.ContainerId, DeviceQuery.Name, DeviceQuery.IsPresent);

        var batteryByContainer = new Dictionary<Guid, (string Name, int Level)>();
        foreach (var node in nodes)
        {
            if (node.Get<int>(DeviceQuery.BluetoothBattery) is not int raw)
                continue;
            if (node.Get<bool>(DeviceQuery.IsPresent) == false)
                continue;
            if (node.Get<Guid>(DeviceQuery.ContainerId) is not Guid container)
                continue;

            int level = Math.Clamp(raw, 0, 100);
            // Meldet ein Gerät über mehrere Knoten, zählt der niedrigste Wert.
            if (!batteryByContainer.TryGetValue(container, out var existing) || level < existing.Level)
                batteryByContainer[container] = (node.GetString(DeviceQuery.Name) ?? "Unbekanntes Gerät", level);
        }

        if (batteryByContainer.Count == 0)
            return [];

        // 2. Gerätecontainer für Anzeigename ("Flow Headset" statt "... Hands-Free AG") und
        //    Verbindungsstatus. Bewusst keine AssociationEndpoint-Abfrage: die stößt eine
        //    Bluetooth-Suche an und dauert ~30 s.
        var containers = DeviceQuery.GetObjects(DevObjectType.DeviceContainer,
            DeviceQuery.Name, DeviceQuery.ContainerIsConnected);

        var containerInfo = new Dictionary<Guid, (string? Name, bool Connected)>();
        foreach (var c in containers)
        {
            if (!Guid.TryParse(c.Id, out var id))
                continue;
            containerInfo[id] = (c.GetString(DeviceQuery.Name), c.Get<bool>(DeviceQuery.ContainerIsConnected) ?? false);
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

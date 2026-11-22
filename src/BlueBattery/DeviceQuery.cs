using System.Runtime.InteropServices;

namespace BlueBattery;

/// <summary>Schlüssel einer Geräteeigenschaft (entspricht DEVPROPKEY).</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct DevPropKey(Guid FormatId, uint PropertyId);

internal enum DevObjectType
{
    DeviceContainer = 2,
    Device = 3,
}

/// <summary>Ein von <see cref="DeviceQuery"/> geliefertes Objekt mit seinen Eigenschaftswerten.</summary>
internal sealed record DevObject(string Id, IReadOnlyDictionary<DevPropKey, object> Properties)
{
    public T? Get<T>(DevPropKey key) where T : struct =>
        Properties.TryGetValue(key, out var v) && v is T t ? t : null;

    public string? GetString(DevPropKey key) =>
        Properties.TryGetValue(key, out var v) ? v as string : null;
}

/// <summary>
/// Dünne Hülle um DevGetObjects (cfgmgr32.dll, ab Windows 10 1809). Das ist dieselbe Abfrage,
/// die auch Windows.Devices.Enumeration.DeviceInformation intern nutzt – aber ohne die
/// 24 MB große WinRT-Projektion (Microsoft.Windows.SDK.NET.dll).
/// </summary>
internal static partial class DeviceQuery
{
    public static readonly DevPropKey Name = new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);
    public static readonly DevPropKey IsPresent = new(new Guid("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"), 5);
    public static readonly DevPropKey ContainerId = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    public static readonly DevPropKey ContainerIsConnected = new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 55);
    public static readonly DevPropKey BluetoothBattery = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);

    public static unsafe List<DevObject> GetObjects(DevObjectType type, params DevPropKey[] keys)
    {
        var requested = stackalloc DevPropCompKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            requested[i] = new DevPropCompKey { Key = keys[i], Store = 0 /* DEVPROP_STORE_SYSTEM */ };

        uint count = 0;
        DevObjectNative* objects = null;
        int hr = DevGetObjects((int)type, 0, (uint)keys.Length, requested, 0, null, &count, &objects);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        try
        {
            var result = new List<DevObject>((int)count);
            for (int i = 0; i < count; i++)
            {
                ref readonly var obj = ref objects[i];
                var props = new Dictionary<DevPropKey, object>();
                for (int j = 0; j < obj.PropertyCount; j++)
                {
                    ref readonly var p = ref obj.Properties[j];
                    if (ReadValue(p) is { } value)
                        props[p.CompKey.Key] = value;
                }
                result.Add(new DevObject(Marshal.PtrToStringUni(obj.ObjectId) ?? "", props));
            }
            return result;
        }
        finally
        {
            if (objects is not null)
                DevFreeObjects(count, objects);
        }
    }

    private static unsafe object? ReadValue(in DevProperty p)
    {
        if (p.Buffer == null || p.BufferSize == 0)
            return null;

        // Nur einfache Werte; Arrays/Listen (Modifikator-Bits) werden nicht benötigt.
        return p.Type switch
        {
            0x03 => (int)*(byte*)p.Buffer,                       // DEVPROP_TYPE_BYTE
            0x07 => (int)*(uint*)p.Buffer,                       // DEVPROP_TYPE_UINT32
            0x0D => *(Guid*)p.Buffer,                            // DEVPROP_TYPE_GUID
            0x11 => *(byte*)p.Buffer != 0,                       // DEVPROP_TYPE_BOOLEAN
            0x12 => Marshal.PtrToStringUni((nint)p.Buffer),      // DEVPROP_TYPE_STRING
            _ => null,
        };
    }

    // ---- Native Strukturen (x64- und x86-tauglich durch sequentielles Layout) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropCompKey
    {
        public DevPropKey Key;
        public int Store;
        public nint LocaleName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DevProperty
    {
        public DevPropCompKey CompKey;
        public uint Type;
        public uint BufferSize;
        public void* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DevObjectNative
    {
        public int ObjectType;
        public nint ObjectId;
        public uint PropertyCount;
        public DevProperty* Properties;
    }

    [LibraryImport("cfgmgr32.dll")]
    private static unsafe partial int DevGetObjects(
        int objectType,
        int queryFlags,
        uint requestedPropertyCount,
        DevPropCompKey* requestedProperties,
        uint filterExpressionCount,
        void* filter,
        uint* objectCount,
        DevObjectNative** objects);

    [LibraryImport("cfgmgr32.dll")]
    private static unsafe partial void DevFreeObjects(uint objectCount, DevObjectNative* objects);
}

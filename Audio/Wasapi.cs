using System.Runtime.InteropServices;

namespace GuitarBassTuner.Audio;

internal sealed record CaptureEndpoint(string Id, string Name, bool IsRocksmith);

internal sealed record CaptureEndpoints(CaptureEndpoint[] Devices, string? DefaultId, int ErrorCode = 0)
{
    public CaptureEndpoint? Rocksmith => Devices.FirstOrDefault(device => device.IsRocksmith);

    public CaptureEndpoint? Find(string? id) =>
        Devices.FirstOrDefault(device => string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase));
}

internal static class Wasapi
{
    public const uint EventCallback = 0x00040000;
    public const uint DataDiscontinuity = 0x1;
    public const uint SilentBuffer = 0x2;
    private const int CaptureFlow = 1;
    private const int ConsoleRole = 0;
    private const uint ActiveDevice = 1;
    private const uint AllComContexts = 23;
    private const ushort StringVariant = 31;
    public const int DeviceNotFound = unchecked((int)0x80070490);
    private static readonly PropertyKey DeviceFriendlyName = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly PropertyKey InterfaceFriendlyName = new(new("026E516E-B814-414B-83CD-856D6FEF4822"), 2);

    public static CaptureEndpoints ListCaptureEndpoints()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection? devices = null;
        try
        {
            Check(enumerator.EnumAudioEndpoints(CaptureFlow, ActiveDevice, out devices));
            Check(devices.GetCount(out uint count));
            var endpoints = new List<CaptureEndpoint>();
            for (uint index = 0; index < count; index++)
            {
                Check(devices.Item(index, out IMMDevice device));
                try
                {
                    endpoints.Add(Describe(device));
                }
                finally
                {
                    Release(device);
                }
            }
            return new(endpoints.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(device => device.Id, StringComparer.Ordinal).ToArray(), DefaultCaptureId(enumerator));
        }
        finally
        {
            Release(devices);
            Release(enumerator);
        }
    }

    public static IMMDevice? OpenCaptureEndpoint(string id)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        try
        {
            int result = enumerator.GetDevice(id, out device);
            if (result == DeviceNotFound)
                return null;

            Check(result);
            Check(device.GetState(out uint state));
            if (state != ActiveDevice)
                return null;

            IMMDevice endpoint = device;
            device = null;
            return endpoint;
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    private static string? DefaultCaptureId(IMMDeviceEnumerator enumerator)
    {
        int result = enumerator.GetDefaultAudioEndpoint(CaptureFlow, ConsoleRole, out IMMDevice device);
        if (result == DeviceNotFound)
            return null;

        Check(result);
        try
        {
            return ReadId(device);
        }
        finally
        {
            Release(device);
        }
    }

    private static string ReadId(IMMDevice device)
    {
        Check(device.GetId(out IntPtr pointer));
        try
        {
            return Marshal.PtrToStringUni(pointer)!;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static CaptureEndpoint Describe(IMMDevice device)
    {
        string id = ReadId(device);
        if (device.OpenPropertyStore(0, out IPropertyStore properties) < 0)
            return new(id, "录音设备", false);

        try
        {
            string name = ReadProperty(properties, DeviceFriendlyName);
            string adapter = ReadProperty(properties, InterfaceFriendlyName);
            bool isRocksmith = IsRocksmithName(name) || IsRocksmithName(adapter);
            return new(id, name.Length > 0 ? name : adapter.Length > 0 ? adapter : "录音设备", isRocksmith);
        }
        finally
        {
            Release(properties);
        }
    }

    private static bool IsRocksmithName(string name) =>
        name.Contains("Rocksmith", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Real Tone Cable", StringComparison.OrdinalIgnoreCase);

    private static string ReadProperty(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(in key, out PropVariant value) < 0)
            return "";

        try
        {
            return value.Type == StringVariant ? Marshal.PtrToStringUni(value.Pointer) ?? "" : "";
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    public static IAudioClient Activate(IMMDevice device)
    {
        Guid id = typeof(IAudioClient).GUID;
        Check(device.Activate(in id, AllComContexts, IntPtr.Zero, out IAudioClient client));
        return client;
    }

    public static IAudioCaptureClient GetCaptureClient(IAudioClient client)
    {
        Guid id = typeof(IAudioCaptureClient).GUID;
        Check(client.GetService(in id, out IAudioCaptureClient capture));
        return capture;
    }

    public static void Check(int result) => Marshal.ThrowExceptionForHR(result, new IntPtr(-1));

    public static void Release(object? reference)
    {
        if (reference is not null)
            Marshal.ReleaseComObject(reference);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort Type;
        private ushort reserved1, reserved2, reserved3;
        public IntPtr Pointer;
        private IntPtr padding;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(in Guid id, uint context, IntPtr activation, out IAudioClient instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint flags, long duration, long period, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(in Guid id, out IAudioCaptureClient instance);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong time);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}

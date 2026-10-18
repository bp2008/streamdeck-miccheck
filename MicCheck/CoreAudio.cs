using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MicCheck;

// Minimal Windows Core Audio (MMDevice / WASAPI) interop using source-generated COM, so the plugin can be
// compiled with Native AOT. Method order in each interface must match the native vtable.

internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

internal static class DeviceState
{
    public const int Active = 0x1;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    [PreserveSig] int GetDevice(string id, out IMMDevice device);
}

[GeneratedComInterface, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig] int Activate(in Guid iid, int clsCtx, nint activationParams, out nint iface);
    [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore store);
    [PreserveSig] int GetId(out string id);
    [PreserveSig] int GetState(out int state);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;
    public PropertyKey(Guid fmt, int pid) { FormatId = fmt; PropertyId = pid; }
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public nint Pointer;
}

[GeneratedComInterface, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetAt(int index, out PropertyKey key);
    [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
}

[GeneratedComInterface, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
internal partial interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float peak);
}

[GeneratedComInterface, Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(nint notify);
    [PreserveSig] int UnregisterControlChangeNotify(nint notify);
    [PreserveSig] int GetChannelCount(out int count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, in Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, in Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(int channel, float levelDb, in Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, in Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(int channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[GeneratedComInterface, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal partial interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig] int GetAudioSessionControl(nint sessionGuid, int streamFlags, out nint sessionControl);
    [PreserveSig] int GetSimpleAudioVolume(nint sessionGuid, int streamFlags, out nint audioVolume);
    // IAudioSessionManager2
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
}

[GeneratedComInterface, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal partial interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
}

internal enum AudioSessionState { Inactive = 0, Active = 1, Expired = 2 }

[GeneratedComInterface, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
internal partial interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName(out nint name);
    [PreserveSig] int SetDisplayName(nint name, nint ctx);
    [PreserveSig] int GetIconPath(out nint path);
    [PreserveSig] int SetIconPath(nint path, nint ctx);
    [PreserveSig] int GetGroupingParam(out Guid grouping);
    [PreserveSig] int SetGroupingParam(nint grouping, nint ctx);
    [PreserveSig] int RegisterAudioSessionNotification(nint notify);
    [PreserveSig] int UnregisterAudioSessionNotification(nint notify);
    // IAudioSessionControl2
    [PreserveSig] int GetSessionIdentifier(out nint id);
    [PreserveSig] int GetSessionInstanceIdentifier(out nint id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
}

[GeneratedComInterface, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
internal partial interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);
    [PreserveSig] int GetMixFormat(out nint format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
}

internal static partial class CoreAudio
{
    private const int ClsCtxAll = 23;
    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, int clsCtx, in Guid iid, out nint obj);
    [LibraryImport("ole32.dll")] private static partial int PropVariantClear(ref PropVariant pv);

    public static IMMDeviceEnumerator CreateEnumerator()
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(MMDeviceEnumeratorClsid, 0, ClsCtxAll, typeof(IMMDeviceEnumerator).GUID, out nint ptr));
        return Wrap<IMMDeviceEnumerator>(ptr);
    }

    public static T Activate<T>(IMMDevice device) where T : class
    {
        Marshal.ThrowExceptionForHR(device.Activate(typeof(T).GUID, ClsCtxAll, 0, out nint ptr));
        return Wrap<T>(ptr);
    }

    /// <summary>Wraps a raw COM pointer (taking over its reference) as a managed interface.</summary>
    private static T Wrap<T>(nint ptr) where T : class
    {
        try { return (T)ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None); }
        finally { Marshal.Release(ptr); }
    }

    /// <summary>Releases the native object now instead of waiting for the garbage collector.</summary>
    public static void Release(object? comObject) => (comObject as ComObject)?.FinalRelease();

    public static string GetId(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.GetId(out string id));
        return id;
    }

    public static string GetFriendlyName(IMMDevice device)
    {
        try
        {
            if (device.OpenPropertyStore(0, out IPropertyStore store) != 0 || store == null) return "";
            if (store.GetValue(FriendlyNameKey, out PropVariant pv) != 0) return "";
            try { return pv.VarType == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(pv.Pointer) ?? "" : ""; }
            finally { PropVariantClear(ref pv); Release(store); }
        }
        catch { return ""; }
    }
}

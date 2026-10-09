using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WidgetScreen
{
    public sealed class AudioSession
    {
        public string Id, Name;
        public float Volume;
        public bool Muted;
    }
    public sealed class AudioSnapshot
    {
        public string Device;
        public float Volume;
        public bool Muted;
        public bool? MicrophoneMuted;
        public bool SessionsAvailable = true;
        public List<AudioSession> Sessions = new List<AudioSession>();
    }

    public sealed class AudioService : IDisposable
    {
        private IMMDeviceEnumerator enumerator;
        private IMMDevice output;
        private IAudioEndpointVolume endpoint;
        private IAudioMeterInformation meter;
        private string deviceId;
        private const uint Context = 23;

        public AudioService() { enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator(); }
        private static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        private void Connect()
        {
            IMMDevice device = null;
            try
            {
                Check(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                string id; Check(device.GetId(out id));
                if (id == deviceId && endpoint != null) return;
                Disconnect();
                output = device; device = null;
                object volumeObject;
                Guid volumeGuid = typeof(IAudioEndpointVolume).GUID;
                Check(output.Activate(ref volumeGuid, Context, IntPtr.Zero, out volumeObject));
                endpoint = (IAudioEndpointVolume)volumeObject;
                object meterObject; Guid meterGuid = typeof(IAudioMeterInformation).GUID;
                if (output.Activate(ref meterGuid, Context, IntPtr.Zero, out meterObject) >= 0) meter = (IAudioMeterInformation)meterObject;
                deviceId = id;
            }
            catch { Disconnect(); throw; }
            finally { Release(device); }
        }

        private void Disconnect()
        {
            Release(meter); Release(endpoint); Release(output);
            meter = null; endpoint = null; output = null; deviceId = null;
        }

        public AudioSnapshot Read()
        {
            try
            {
                Connect();
                var snapshot = new AudioSnapshot();
                Check(endpoint.GetMasterVolumeLevelScalar(out snapshot.Volume));
                Check(endpoint.GetMute(out snapshot.Muted));
                snapshot.Device = FriendlyName(output);
                try { WithMicrophone(volume => { bool muted; Check(volume.GetMute(out muted)); snapshot.MicrophoneMuted = muted; }); } catch (COMException) { } catch (UnauthorizedAccessException) { }
                try { VisitSessions((control, volume) => {
                    int state; Check(control.GetState(out state));
                    if (state == 2) return;
                    string id, name; uint pid;
                    Check(control.GetSessionInstanceIdentifier(out id));
                    Check(control.GetDisplayName(out name));
                    Check(control.GetProcessId(out pid));
                    if (pid == 0) name = "System sounds";
                    else if (String.IsNullOrWhiteSpace(name) || name.StartsWith("@", StringComparison.Ordinal))
                    {
                        try { using (var process = Process.GetProcessById((int)pid)) name = process.ProcessName; }
                        catch (ArgumentException) { name = "Audio application"; }
                    }
                    float level; bool mute;
                    Check(volume.GetMasterVolume(out level)); Check(volume.GetMute(out mute));
                    snapshot.Sessions.Add(new AudioSession { Id = id, Name = name, Volume = level, Muted = mute });
                }); }
                catch (COMException) { snapshot.SessionsAvailable = false; }
                catch (UnauthorizedAccessException) { snapshot.SessionsAvailable = false; }
                return snapshot;
            }
            catch (COMException) { Disconnect(); throw; }
        }

        public float Peak()
        {
            if (meter == null) return 0;
            float peak;
            return meter.GetPeakValue(out peak) >= 0 ? peak : 0;
        }

        public void SetVolume(double value) { Connect(); Guid context = Guid.Empty; Check(endpoint.SetMasterVolumeLevelScalar((float)Math.Max(0, Math.Min(1, value)), ref context)); }
        public void ToggleMute() { Connect(); bool mute; Check(endpoint.GetMute(out mute)); Guid context = Guid.Empty; Check(endpoint.SetMute(!mute, ref context)); }
        public void ToggleMicrophone() { WithMicrophone(volume => { bool mute; Check(volume.GetMute(out mute)); Guid context = Guid.Empty; Check(volume.SetMute(!mute, ref context)); }); }

        public void SetSessionVolume(string id, double value)
        {
            Connect();
            VisitSessions((control, volume) => { string current; Check(control.GetSessionInstanceIdentifier(out current)); if (current == id) { Guid context = Guid.Empty; Check(volume.SetMasterVolume((float)Math.Max(0, Math.Min(1, value)), ref context)); } });
        }
        public void ToggleSessionMute(string id)
        {
            Connect();
            VisitSessions((control, volume) => { string current; Check(control.GetSessionInstanceIdentifier(out current)); if (current == id) { bool mute; Check(volume.GetMute(out mute)); Guid context = Guid.Empty; Check(volume.SetMute(!mute, ref context)); } });
        }

        private void WithMicrophone(Action<IAudioEndpointVolume> action)
        {
            IMMDevice microphone = null; object volume = null;
            try
            {
                Check(enumerator.GetDefaultAudioEndpoint(1, 2, out microphone));
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                Check(microphone.Activate(ref iid, Context, IntPtr.Zero, out volume));
                action((IAudioEndpointVolume)volume);
            }
            finally { Release(volume); Release(microphone); }
        }

        private void VisitSessions(Action<IAudioSessionControl2, ISimpleAudioVolume> action)
        {
            object managerObject = null; IAudioSessionEnumerator sessions = null;
            try
            {
                Guid iid = typeof(IAudioSessionManager2).GUID;
                Check(output.Activate(ref iid, Context, IntPtr.Zero, out managerObject));
                var manager = (IAudioSessionManager2)managerObject;
                Check(manager.GetSessionEnumerator(out sessions));
                int count; Check(sessions.GetCount(out count));
                for (int index = 0; index < count; index++)
                {
                    IAudioSessionControl control = null;
                    try { Check(sessions.GetSession(index, out control)); action((IAudioSessionControl2)control, (ISimpleAudioVolume)control); }
                    catch (COMException) { /* Sessions may end between enumeration and access. */ }
                    finally { Release(control); }
                }
            }
            finally { Release(sessions); Release(managerObject); }
        }

        private static string FriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                Check(device.OpenPropertyStore(0, out store));
                var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                PropVariant value; Check(store.GetValue(ref key, out value));
                try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : "Default output"; }
                finally { PropVariantClear(ref value); }
            }
            catch (COMException) { return "Default output"; }
            finally { Release(store); }
        }
        public void Dispose() { Disconnect(); Release(enumerator); enumerator = null; }

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
            [PreserveSig] int VolumeStepUp(ref Guid context);
            [PreserveSig] int VolumeStepDown(ref Guid context);
            [PreserveSig] int QueryHardwareSupport(out uint mask);
            [PreserveSig] int GetVolumeRange(out float minimum, out float maximum, out float increment);
        }
        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
            [PreserveSig] int GetMeteringChannelCount(out int count);
            [PreserveSig] int GetChannelsPeakValues(int count, IntPtr values);
            [PreserveSig] int QueryHardwareSupport(out int mask);
        }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(ref Guid session, uint flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(ref Guid session, uint flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
            [PreserveSig] int RegisterSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
            [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr notification);
            [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
        }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl control);
        }
        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid grouping);
            [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        }
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid grouping);
            [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }
        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid context);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}

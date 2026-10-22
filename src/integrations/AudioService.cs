using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace WidgetScreen
{
    public sealed class AudioDevice
    {
        public string Id, Name;
        public uint FormFactor;
        public bool IsDefault;
    }
    public sealed class AudioSession
    {
        public string Id, Name, DeviceId;
        public uint ProcessId;
        public float Volume;
        public bool Muted, Active;
    }
    public sealed class AudioSnapshot
    {
        public string Device, DeviceId;
        public float Volume;
        public bool Muted;
        public bool? MicrophoneMuted;
        public bool SessionsAvailable = true;
        public bool CanRouteApps;
        public List<AudioSession> Sessions = new List<AudioSession>();
        public List<AudioDevice> Devices = new List<AudioDevice>();
    }

    public sealed class AudioService : IDisposable
    {
        private IMMDeviceEnumerator enumerator;
        private IMMDevice output;
        private IAudioEndpointVolume endpoint;
        private IAudioMeterInformation meter;
        private string deviceId;
        private Dictionary<string, string> sessionDevices = new Dictionary<string, string>();
        private IntPtr policyFactory;
        private bool policyChecked;
        private SetPersistedEndpoint setAppEndpoint;
        private GetPersistedEndpoint getAppEndpoint;
        private const uint Context = 23;
        private const string PolicyPrefix = @"\\?\SWD#MMDEVAPI#", RenderInterface = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
        // Output names and form factors by id. Opening a property store is the slow part of a read, and names
        // don't change; the cache is dropped when Windows reports a device change.
        private readonly Dictionary<string, AudioDevice> knownDevices = new Dictionary<string, AudioDevice>(StringComparer.OrdinalIgnoreCase);
        private Notifications notifications;
        private volatile bool devicesChanged;
        // Raised on a COM thread when an output appears, disappears, changes state or becomes the default.
        public event Action DevicesChanged;

        public AudioService()
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try { notifications = new Notifications(this); Check(enumerator.RegisterEndpointNotificationCallback(notifications)); }
            catch (Exception) { notifications = null; /* Without notifications the 2 s poll still catches changes. */ }
        }
        private static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        private static bool Same(string first, string second) { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }

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
                if (devicesChanged) { devicesChanged = false; knownDevices.Clear(); Disconnect(); }
                Connect();
                var snapshot = new AudioSnapshot();
                Check(endpoint.GetMasterVolumeLevelScalar(out snapshot.Volume));
                Check(endpoint.GetMute(out snapshot.Muted));
                snapshot.Device = Known(deviceId, output).Name; snapshot.DeviceId = deviceId;
                try { WithMicrophone(volume => { bool muted; Check(volume.GetMute(out muted)); snapshot.MicrophoneMuted = muted; }); } catch (COMException) { } catch (UnauthorizedAccessException) { }
                var found = new List<AudioSession>();
                try { ReadSessions(output, deviceId, found); }
                catch (COMException) { snapshot.SessionsAvailable = false; }
                catch (UnauthorizedAccessException) { snapshot.SessionsAvailable = false; }
                try
                {
                    VisitOutputs((id, device) => {
                        var known = Known(id, device);
                        snapshot.Devices.Add(new AudioDevice { Id = id, Name = known.Name, FormFactor = known.FormFactor, IsDefault = id == deviceId });
                        // Apps routed to another output keep their sessions on it. A failing output only hides its own apps.
                        if (id != deviceId && snapshot.SessionsAvailable)
                        {
                            try { ReadSessions(device, id, found); } catch (COMException) { } catch (UnauthorizedAccessException) { }
                        }
                    });
                }
                catch (COMException) { } catch (UnauthorizedAccessException) { }
                if (!snapshot.Devices.Any(device => device.IsDefault)) snapshot.Devices.Insert(0, new AudioDevice { Id = deviceId, Name = snapshot.Device, FormFactor = Known(deviceId, output).FormFactor, IsDefault = true });
                var devices = new Dictionary<string, string>();
                foreach (var session in found) devices[session.Id] = session.DeviceId;
                sessionDevices = devices;
                snapshot.Sessions = Ordered(VisibleSessions(found, deviceId, AppOutput));
                snapshot.CanRouteApps = RoutingReady();
                return snapshot;
            }
            catch (COMException) { Disconnect(); throw; }
        }

        private AudioDevice Known(string id, IMMDevice device)
        {
            AudioDevice known;
            if (!knownDevices.TryGetValue(id, out known)) { known = new AudioDevice { Id = id, Name = FriendlyName(device), FormFactor = FormFactor(device) }; knownDevices[id] = known; }
            return known;
        }

        // Apps that are playing first, then idle ones, System sounds last; otherwise in the order Windows lists them.
        public static List<AudioSession> Ordered(IList<AudioSession> sessions)
        {
            return sessions.Where(s => s.ProcessId != 0 && s.Active).Concat(sessions.Where(s => s.ProcessId != 0 && !s.Active)).Concat(sessions.Where(s => s.ProcessId == 0)).ToList();
        }

        // Windows keeps an idle session on the old output after an app moves, and a System sounds session on every
        // output. Show each app where it plays (or where it is routed while idle) and System sounds on the default only.
        public static List<AudioSession> VisibleSessions(IList<AudioSession> sessions, string defaultDeviceId, Func<uint, string> appOutput)
        {
            var visible = new List<AudioSession>();
            var routed = new Dictionary<uint, string>();
            foreach (var session in sessions)
            {
                if (session.ProcessId == 0) { if (Same(session.DeviceId, defaultDeviceId)) visible.Add(session); continue; }
                if (session.Active) { visible.Add(session); continue; }
                var siblings = sessions.Where(other => other.ProcessId == session.ProcessId).ToList();
                var playing = siblings.Where(other => other.Active).Select(other => other.DeviceId).ToList();
                if (playing.Count > 0) { if (playing.Any(id => Same(id, session.DeviceId))) visible.Add(session); continue; }
                if (siblings.All(other => Same(other.DeviceId, session.DeviceId))) { visible.Add(session); continue; }
                string target;
                if (!routed.TryGetValue(session.ProcessId, out target))
                {
                    try { target = appOutput == null ? null : appOutput(session.ProcessId); } catch (Exception) { target = null; }
                    if (String.IsNullOrEmpty(target)) target = defaultDeviceId;
                    routed[session.ProcessId] = target;
                }
                if (Same(session.DeviceId, target) || !siblings.Any(other => Same(other.DeviceId, target))) visible.Add(session);
            }
            return visible;
        }

        public float Peak()
        {
            if (meter == null) return 0;
            float peak;
            return meter.GetPeakValue(out peak) >= 0 ? peak : 0;
        }

        public void SetVolume(double value) { Connect(); Guid context = Guid.Empty; Check(endpoint.SetMasterVolumeLevelScalar((float)Math.Max(0, Math.Min(1, value)), ref context)); }
        public void StepVolume(double delta) { Connect(); float level; Check(endpoint.GetMasterVolumeLevelScalar(out level)); SetVolume(level + delta); }
        public void ToggleMute() { Connect(); bool mute; Check(endpoint.GetMute(out mute)); Guid context = Guid.Empty; Check(endpoint.SetMute(!mute, ref context)); }
        public void ToggleMicrophone() { WithMicrophone(volume => { bool mute; Check(volume.GetMute(out mute)); Guid context = Guid.Empty; Check(volume.SetMute(!mute, ref context)); }); }

        public void SetSessionVolume(string id, double value)
        {
            WithSession(id, volume => { Guid context = Guid.Empty; Check(volume.SetMasterVolume((float)Math.Max(0, Math.Min(1, value)), ref context)); });
        }
        public void ToggleSessionMute(string id)
        {
            WithSession(id, volume => { bool mute; Check(volume.GetMute(out mute)); Guid context = Guid.Empty; Check(volume.SetMute(!mute, ref context)); });
        }

        // Makes an output the Windows default for every role, as the Windows volume flyout does.
        public void SetDefaultOutput(string id)
        {
            object client = null;
            try
            {
                client = new PolicyConfigClient();
                var config = (IPolicyConfig)client;
                foreach (int role in new[] { 0, 1, 2 }) Check(config.SetDefaultEndpoint(id, role));
            }
            finally { Release(client); }
            Connect();
        }

        public bool CanRouteApps { get { return RoutingReady(); } }

        // The output an app was sent to, or null when it follows the Windows default.
        public string AppOutput(uint processId)
        {
            if (processId == 0 || !RoutingReady()) return null;
            IntPtr value = IntPtr.Zero;
            try
            {
                if (getAppEndpoint(policyFactory, processId, 0, 1, out value) < 0 || value == IntPtr.Zero) return null;
                uint length; IntPtr buffer = WindowsGetStringRawBuffer(value, out length);
                return EndpointId(Marshal.PtrToStringUni(buffer, (int)length));
            }
            finally { if (value != IntPtr.Zero) WindowsDeleteString(value); }
        }

        // Sends an app to an output, or back to the Windows default when id is null. Windows remembers it per app.
        public void SetAppOutput(uint processId, string id)
        {
            if (processId == 0 || !RoutingReady()) throw new NotSupportedException("Windows doesn't allow changing this app's output here.");
            IntPtr value = IntPtr.Zero;
            try
            {
                if (!String.IsNullOrEmpty(id)) { string packed = PolicyDeviceId(id); Check(WindowsCreateString(packed, (uint)packed.Length, out value)); }
                foreach (int role in new[] { 0, 1 })
                {
                    int result = setAppEndpoint(policyFactory, processId, 0, role, value);
                    // Clearing an app that already follows the default reports E_INVALIDARG; there is nothing to undo.
                    if (value != IntPtr.Zero || result != unchecked((int)0x80070057)) Check(result);
                }
            }
            finally { if (value != IntPtr.Zero) WindowsDeleteString(value); }
        }

        public static string PolicyDeviceId(string endpointId) { return PolicyPrefix + endpointId + RenderInterface; }
        public static string EndpointId(string policyId)
        {
            if (String.IsNullOrEmpty(policyId)) return null;
            string id = policyId;
            if (id.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase)) id = id.Substring(PolicyPrefix.Length);
            if (id.EndsWith(RenderInterface, StringComparison.OrdinalIgnoreCase)) id = id.Substring(0, id.Length - RenderInterface.Length);
            return id.Length == 0 ? null : id;
        }

        // Per-app output uses the undocumented factory behind Windows' "App volume and device preferences" page.
        // See docs/decisions/005-mixer-output-routing.md for the interface layout and its risks.
        private bool RoutingReady()
        {
            if (policyChecked) return policyFactory != IntPtr.Zero;
            policyChecked = true;
            IntPtr name = IntPtr.Zero;
            try
            {
                const string className = "Windows.Media.Internal.AudioPolicyConfig";
                if (WindowsCreateString(className, (uint)className.Length, out name) < 0) return false;
                foreach (string iid in new[] { "ab3d4648-e242-459f-b02f-541c70306324", "2a59116d-6c4f-45e0-a74f-707e3fef9258" })
                {
                    Guid guid = new Guid(iid); IntPtr factory;
                    if (RoGetActivationFactory(name, ref guid, out factory) < 0 || factory == IntPtr.Zero) continue;
                    IntPtr table = Marshal.ReadIntPtr(factory);
                    setAppEndpoint = (SetPersistedEndpoint)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, 25 * IntPtr.Size), typeof(SetPersistedEndpoint));
                    getAppEndpoint = (GetPersistedEndpoint)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, 26 * IntPtr.Size), typeof(GetPersistedEndpoint));
                    policyFactory = factory;
                    return true;
                }
            }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            finally { if (name != IntPtr.Zero) WindowsDeleteString(name); }
            return false;
        }

        private void WithSession(string id, Action<ISimpleAudioVolume> action)
        {
            Connect();
            bool done = false;
            Action<IMMDevice> visit = device => VisitSessions(device, (control, volume) => {
                if (done) return;
                string current; Check(control.GetSessionInstanceIdentifier(out current));
                if (current == id) { done = true; action(volume); }
            });
            string known;
            if (sessionDevices.TryGetValue(id, out known) && known != deviceId)
            {
                IMMDevice device = null;
                try { if (enumerator.GetDevice(known, out device) >= 0) visit(device); }
                finally { Release(device); }
            }
            else visit(output);
            if (!done) VisitOutputs((other, device) => { if (!done) visit(device); });
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

        private void VisitOutputs(Action<string, IMMDevice> action)
        {
            IMMDeviceCollection devices = null;
            try
            {
                Check(enumerator.EnumAudioEndpoints(0, 1, out devices));
                uint count; Check(devices.GetCount(out count));
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice device = null;
                    try { Check(devices.Item(index, out device)); string id; Check(device.GetId(out id)); action(id, device); }
                    catch (COMException) { /* Outputs may disappear between enumeration and access. */ }
                    finally { Release(device); }
                }
            }
            finally { Release(devices); }
        }

        private static void ReadSessions(IMMDevice device, string deviceId, List<AudioSession> into)
        {
            VisitSessions(device, (control, volume) => {
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
                into.Add(new AudioSession { Id = id, Name = name, DeviceId = deviceId, ProcessId = pid, Active = state == 1, Volume = level, Muted = mute });
            });
        }

        private static void VisitSessions(IMMDevice device, Action<IAudioSessionControl2, ISimpleAudioVolume> action)
        {
            object managerObject = null; IAudioSessionEnumerator sessions = null;
            try
            {
                Guid iid = typeof(IAudioSessionManager2).GUID;
                Check(device.Activate(ref iid, Context, IntPtr.Zero, out managerObject));
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

        // PKEY_AudioEndpoint_FormFactor: 1 speakers, 3 headphones, 5 headset, 9 display audio; 0 when unknown.
        private static uint FormFactor(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                Check(device.OpenPropertyStore(0, out store));
                var key = new PropertyKey { Format = new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), Id = 0 };
                PropVariant value; Check(store.GetValue(ref key, out value));
                try { return value.Type == 19 ? value.Number : 0; }
                finally { PropVariantClear(ref value); }
            }
            catch (COMException) { return 0; }
            finally { Release(store); }
        }
        public void Dispose()
        {
            if (notifications != null && enumerator != null) { try { enumerator.UnregisterEndpointNotificationCallback(notifications); } catch (Exception) { } notifications = null; }
            Disconnect(); Release(enumerator); enumerator = null;
            if (policyFactory != IntPtr.Zero) { Marshal.Release(policyFactory); policyFactory = IntPtr.Zero; }
        }

        // Windows calls these on its own thread; they only flag the change and tell the owner.
        [ComVisible(true)]
        private sealed class Notifications : IMMNotificationClient
        {
            private readonly AudioService owner;
            public Notifications(AudioService owner) { this.owner = owner; }
            private void Changed()
            {
                owner.devicesChanged = true;
                var handler = owner.DevicesChanged;
                if (handler != null) { try { handler(); } catch (Exception) { } }
            }
            public int OnDeviceStateChanged(string id, uint state) { Changed(); return 0; }
            public int OnDeviceAdded(string id) { Changed(); return 0; }
            public int OnDeviceRemoved(string id) { Changed(); return 0; }
            public int OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == 0) Changed(); return 0; }
            public int OnPropertyValueChanged(string id, PropertyKey key) { return 0; }
        }

        [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMNotificationClient
        {
            [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
            [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
        }

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, uint length, out IntPtr value);
        [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr value);
        [DllImport("combase.dll")] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr value, out uint length);
        [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; [FieldOffset(8)] public uint Number; }

        // IAudioPolicyConfigFactory slots 25 and 26 (after IInspectable's six and nineteen unused methods).
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetPersistedEndpoint(IntPtr self, uint processId, int flow, int role, IntPtr deviceId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPersistedEndpoint(IntPtr self, uint processId, int flow, int role, out IntPtr deviceId);

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback([MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client);
            [PreserveSig] int UnregisterEndpointNotificationCallback([MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] private class PolicyConfigClient { }
        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat();
            [PreserveSig] int GetDeviceFormat();
            [PreserveSig] int ResetDeviceFormat();
            [PreserveSig] int SetDeviceFormat();
            [PreserveSig] int GetProcessingPeriod();
            [PreserveSig] int SetProcessingPeriod();
            [PreserveSig] int GetShareMode();
            [PreserveSig] int SetShareMode();
            [PreserveSig] int GetPropertyValue();
            [PreserveSig] int SetPropertyValue();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            [PreserveSig] int SetEndpointVisibility();
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

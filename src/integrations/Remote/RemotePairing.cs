using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    public sealed class RemoteDevice
    {
        public string Name { get; set; }
        public string TokenHash { get; set; }
        public DateTime PairedUtc { get; set; }
    }

    // Paired phones. A phone pairs with a six-digit code shown in Settings → Phone for five minutes; it then gets a
    // random token. Only a hash of each token is kept, DPAPI-encrypted in remote-devices.dat next to settings.xml.
    public sealed class RemotePairing
    {
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
        private const int MaxFailures = 5;
        private readonly string path;
        private readonly object gate = new object();
        private readonly Func<DateTime> now;
        private List<RemoteDevice> devices = new List<RemoteDevice>();
        private string code;
        private DateTime codeExpires;
        private int failures;
        public string StartupWarning { get; private set; }

        public RemotePairing(string path) : this(path, () => DateTime.UtcNow) { }
        public RemotePairing(string path, Func<DateTime> now)
        {
            this.path = path; this.now = now;
            try
            {
                if (File.Exists(path))
                    devices = new JavaScriptSerializer().Deserialize<List<RemoteDevice>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser))) ?? new List<RemoteDevice>();
            }
            catch (Exception) { devices = new List<RemoteDevice>(); StartupWarning = "Paired phones could not be restored. Pair them again in Settings → Phone."; }
        }

        public static string StorePath(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "remote-devices.dat"); }

        public List<RemoteDevice> Devices { get { lock (gate) return devices.Select(d => new RemoteDevice { Name = d.Name, TokenHash = d.TokenHash, PairedUtc = d.PairedUtc }).ToList(); } }

        public string Code { get { lock (gate) return PairingOpenLocked ? code : null; } }
        public TimeSpan CodeRemaining { get { lock (gate) return PairingOpenLocked ? codeExpires - now() : TimeSpan.Zero; } }
        private bool PairingOpenLocked { get { return code != null && now() < codeExpires; } }

        public string StartPairing()
        {
            byte[] bytes = new byte[4];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            lock (gate)
            {
                code = (BitConverter.ToUInt32(bytes, 0) % 1000000).ToString("000000");
                codeExpires = now() + CodeLifetime; failures = 0;
                return code;
            }
        }

        public void StopPairing() { lock (gate) code = null; }

        // Returns the phone's new token, or throws InvalidOperationException with a message for the phone.
        public string Pair(string offered, string deviceName)
        {
            lock (gate)
            {
                if (!PairingOpenLocked) throw new InvalidOperationException("Pairing isn't open. On the PC, open Settings → Phone and tap Pair a phone.");
                if (!FixedEquals((offered ?? "").Trim(), code))
                {
                    if (++failures >= MaxFailures) code = null;
                    throw new InvalidOperationException(code == null ? "Too many wrong codes. Tap Pair a phone on the PC for a new code." : "That code doesn't match. Check the code on the PC.");
                }
                byte[] bytes = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                string token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                string name = String.IsNullOrWhiteSpace(deviceName) ? "Phone" : deviceName.Trim();
                if (name.Length > 60) name = name.Substring(0, 60);
                var next = new List<RemoteDevice>(devices) { new RemoteDevice { Name = name, TokenHash = Hash(token), PairedUtc = now() } };
                Save(next);
                devices = next; code = null;
                return token;
            }
        }

        public bool Authorize(string token, out string deviceName)
        {
            deviceName = null;
            if (String.IsNullOrEmpty(token)) return false;
            string hash = Hash(token);
            lock (gate)
            {
                foreach (var device in devices)
                    if (FixedEquals(device.TokenHash, hash)) { deviceName = device.Name; return true; }
            }
            return false;
        }

        public void ForgetAll() { lock (gate) { Save(new List<RemoteDevice>()); devices = new List<RemoteDevice>(); } }

        private void Save(List<RemoteDevice> list)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(list)), null, DataProtectionScope.CurrentUser));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }

        public static string Hash(string token)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", "");
        }

        private static bool FixedEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
    }
}

using System;
using System.IO;
using System.Media;
using System.Windows.Media;

namespace WidgetScreen
{
    // Plays the prayer alert on the PC (docs/design/prayer-times.md): the audio file the user chose in Settings →
    // Prayer, or, with none, a short two-note chime made here in code. No recording ships with the app.
    public sealed class PrayerAlert : IDisposable
    {
        private MediaPlayer player;
        private SoundPlayer chime;
        public bool Playing { get; private set; }
        public event Action Stopped;

        public static readonly string[] Extensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac" };
        public static bool Usable(string path)
        {
            return !String.IsNullOrWhiteSpace(path) && Array.IndexOf(Extensions, (Path.GetExtension(path) ?? "").ToLowerInvariant()) >= 0 && File.Exists(path);
        }

        // Returns false when the file couldn't be opened, after playing the chime instead.
        public bool Play(string path)
        {
            Stop();
            if (Usable(path))
            {
                try
                {
                    player = new MediaPlayer { Volume = 1 };
                    player.MediaEnded += (sender, e) => Stop();
                    player.MediaFailed += (sender, e) => { Stop(); PlayChime(); };
                    player.Open(new Uri(Path.GetFullPath(path)));
                    player.Play();
                    Playing = true;
                    return true;
                }
                catch (Exception) { Stop(); }
            }
            PlayChime();
            return String.IsNullOrWhiteSpace(path);
        }

        private void PlayChime()
        {
            try { chime = new SoundPlayer(new MemoryStream(Chime())); chime.Play(); }
            catch (Exception) { }
        }

        public void Stop()
        {
            bool was = Playing;
            if (player != null) { try { player.Stop(); player.Close(); } catch (Exception) { } player = null; }
            if (chime != null) { try { chime.Stop(); chime.Dispose(); } catch (Exception) { } chime = null; }
            Playing = false;
            if (was && Stopped != null) Stopped();
        }

        // Two soft sine notes (E5 then C5), each fading out: a 16-bit mono WAV, about 1.6 seconds.
        public static byte[] Chime()
        {
            const int rate = 22050;
            var notes = new[] { new[] { 659.25, 0.0, 0.8 }, new[] { 523.25, 0.55, 1.6 } };
            int samples = (int)(rate * 1.6);
            var data = new short[samples];
            foreach (var note in notes)
            {
                int start = (int)(note[1] * rate), end = Math.Min(samples, (int)(note[2] * rate));
                for (int i = start; i < end; i++)
                {
                    double t = (i - start) / (double)rate, length = (end - start) / (double)rate;
                    double envelope = Math.Min(1, t / 0.01) * Math.Pow(1 - t / length, 2);
                    data[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, data[i] + 9000 * envelope * Math.Sin(2 * Math.PI * note[0] * t)));
                }
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); writer.Write(36 + samples * 2);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); writer.Write(samples * 2);
                foreach (short sample in data) writer.Write(sample);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public void Dispose() { Stopped = null; Stop(); }
    }
}

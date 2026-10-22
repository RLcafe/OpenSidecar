using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace WidgetScreen
{
    public sealed class MediaSnapshot
    {
        public bool Available, Playing, CanPlayPause, CanPrevious, CanNext, CanSeek;
        public string Title, Artist, Source, Key;
        public TimeSpan Position, Duration;
        public BitmapImage Artwork;
    }
    public sealed class MediaService
    {
        private GlobalSystemMediaTransportControlsSessionManager manager;
        private GlobalSystemMediaTransportControlsSession session;
        private string artworkKey = "";
        private BitmapImage artwork;

        public async Task<MediaSnapshot> ReadAsync()
        {
            if (manager == null) manager = await Await(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            session = manager.GetCurrentSession();
            if (session == null) { artworkKey = ""; artwork = null; return new MediaSnapshot(); }
            var properties = await Await(session.TryGetMediaPropertiesAsync());
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            string key = session.SourceAppUserModelId + "|" + properties.Title + "|" + properties.Artist;
            if (key != artworkKey)
            {
                artworkKey = key; artwork = null;
                if (properties.Thumbnail != null)
                {
                    try
                    {
                        using (var stream = await Await(properties.Thumbnail.OpenReadAsync()))
                        {
                            if (stream.Size < 8 * 1024 * 1024)
                            {
                                using (var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)))
                                {
                                    uint size = await Await<uint>(reader.LoadAsync((uint)stream.Size));
                                    byte[] bytes = new byte[size]; reader.ReadBytes(bytes);
                                    using (var input = new MemoryStream(bytes))
                                    {
                                        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                                        image.DecodePixelWidth = 320; image.StreamSource = input; image.EndInit(); image.Freeze(); artwork = image;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception) { /* Some media apps publish unavailable thumbnails. Metadata remains useful. */ }
                }
            }
            var position = timeline.Position - timeline.StartTime;
            var duration = timeline.EndTime - timeline.StartTime;
            if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromDays(1)) position += TimeSpan.FromSeconds(elapsed.TotalSeconds * (playback.PlaybackRate ?? 1));
            }
            return new MediaSnapshot {
                Available = true, Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPlayPause = playback.Controls.IsPlayPauseToggleEnabled, CanPrevious = playback.Controls.IsPreviousEnabled, CanNext = playback.Controls.IsNextEnabled,
                CanSeek = playback.Controls.IsPlaybackPositionEnabled && duration > TimeSpan.Zero, Key = key,
                Title = String.IsNullOrWhiteSpace(properties.Title) ? "Untitled media" : properties.Title,
                Artist = String.IsNullOrWhiteSpace(properties.Artist) ? properties.AlbumTitle : properties.Artist,
                Source = session.SourceAppUserModelId, Duration = duration,
                Position = position < TimeSpan.Zero ? TimeSpan.Zero : (duration > TimeSpan.Zero && position > duration ? duration : position), Artwork = artwork
            };
        }
        public async Task<bool> CommandAsync(string command)
        {
            if (session == null) return false;
            if (command == "previous") return await Await(session.TrySkipPreviousAsync());
            if (command == "next") return await Await(session.TrySkipNextAsync());
            return await Await(session.TryTogglePlayPauseAsync());
        }
        public async Task<bool> SeekAsync(string expectedKey, double positionMs)
        {
            var current = manager == null ? null : manager.GetCurrentSession();
            if (current == null || !current.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled) return false;
            var properties = await Await(current.TryGetMediaPropertiesAsync());
            string key = current.SourceAppUserModelId + "|" + properties.Title + "|" + properties.Artist;
            if (key != expectedKey) return false;
            var timeline = current.GetTimelineProperties();
            double duration = (timeline.EndTime - timeline.StartTime).TotalMilliseconds;
            if (duration <= 0 || Double.IsNaN(positionMs) || Double.IsInfinity(positionMs)) return false;
            long ticks = (timeline.StartTime + TimeSpan.FromMilliseconds(Math.Max(0, Math.Min(duration - 1, positionMs)))).Ticks;
            return await Await(current.TryChangePlaybackPositionAsync(ticks));
        }

        // A short name for the app behind a media session, from its AppUserModelId: "Spotify", "Chrome", "VLC", "Media
        // Player"… Unknown ids are tidied (publisher hash and ".exe" dropped, last segment kept) rather than hidden.
        public static string SourceName(string appUserModelId)
        {
            string id = (appUserModelId ?? "").Trim();
            if (id.Length == 0) return "";
            string lower = id.ToLowerInvariant();
            if (lower.Contains("spotify")) return "Spotify";
            if (lower.Contains("msedge") || lower.Contains("microsoftedge")) return "Edge";
            if (lower.Contains("chrome")) return "Chrome";
            if (lower.Contains("firefox") || lower.Contains("308046b0af4a39cb")) return "Firefox";
            if (lower.Contains("brave")) return "Brave";
            if (lower.Contains("opera")) return "Opera";
            if (lower.Contains("vlc")) return "VLC";
            if (lower.Contains("zunemusic") || lower.Contains("microsoft.media.player") || lower.Contains("mediaplayer")) return "Media Player";
            if (lower.Contains("zunevideo")) return "Films & TV";
            if (lower.Contains("youtubemusic") || lower.Contains("youtube music")) return "YouTube Music";
            if (lower.Contains("tidal")) return "Tidal";
            if (lower.Contains("deezer")) return "Deezer";
            if (lower.Contains("applemusic") || lower.Contains("apple music") || lower.Contains("itunes")) return "Apple Music";
            if (lower.Contains("foobar")) return "foobar2000";
            if (lower.Contains("musicbee")) return "MusicBee";
            if (lower.Contains("winamp")) return "Winamp";
            if (lower.Contains("groove")) return "Groove";
            if (lower.Contains("discord")) return "Discord";
            if (lower.Contains("steam")) return "Steam";
            // "Publisher.AppName_hash!App" → "AppName"; "C:\…\player.exe" or "player.exe" → "player".
            string name = id;
            int bang = name.IndexOf('!'); if (bang > 0) name = name.Substring(0, bang);
            int underscore = name.LastIndexOf('_'); if (underscore > 0 && name.Length - underscore <= 14) name = name.Substring(0, underscore);
            int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/')); if (slash >= 0) name = name.Substring(slash + 1);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            int dot = name.LastIndexOf('.'); if (dot >= 0 && dot < name.Length - 1) name = name.Substring(dot + 1);
            return name.Length > 24 ? name.Substring(0, 24) : name;
        }

        // Bridge the OS's split WinMD contracts directly; no downloaded SDK projection is required.
        private static Task<T> Await<T>(Windows.Foundation.IAsyncOperation<T> operation)
        {
            var completion = new TaskCompletionSource<T>();
            operation.Completed = (result, status) => {
                try
                {
                    if (status == Windows.Foundation.AsyncStatus.Completed) completion.TrySetResult(result.GetResults());
                    else if (status == Windows.Foundation.AsyncStatus.Canceled) completion.TrySetCanceled();
                    else completion.TrySetException(result.ErrorCode ?? new InvalidOperationException("Media operation failed."));
                }
                catch (Exception e) { completion.TrySetException(e); }
                finally { result.Close(); }
            };
            return completion.Task;
        }
    }
}

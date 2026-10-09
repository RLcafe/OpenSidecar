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

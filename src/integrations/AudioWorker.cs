using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetScreen
{
    // The sound mixer's COM work runs on one background thread (an MTA, where the MMDevice API is at home), so
    // enumerating outputs and sessions never stalls the dashboard. Calls are queued in order and answered as Tasks;
    // the AudioService itself is created and used only on that thread (decision 011).
    public sealed class AudioWorker : IDisposable
    {
        private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        private readonly Thread thread;
        private AudioService service;
        private bool disposed;
        // Raised on a COM thread when an output appears, disappears or becomes the default.
        public event Action DevicesChanged;

        public AudioWorker()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "Widget Screen audio" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        private void Loop()
        {
            foreach (var work in queue.GetConsumingEnumerable()) { try { work(); } catch (Exception) { } }
            if (service != null) { try { service.Dispose(); } catch (Exception) { } service = null; }
        }

        private AudioService Service()
        {
            if (service == null)
            {
                service = new AudioService();
                service.DevicesChanged += () => { var handler = DevicesChanged; if (handler != null) handler(); };
            }
            return service;
        }

        public Task<T> Run<T>(Func<AudioService, T> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (disposed) { completion.SetException(new ObjectDisposedException("AudioWorker")); return completion.Task; }
            queue.Add(() => {
                try { completion.SetResult(work(Service())); }
                catch (Exception e)
                {
                    // A device that went away mid-call: start from a fresh service next time.
                    if ((e is COMException || e is InvalidCastException) && service != null) { try { service.Dispose(); } catch (Exception) { } service = null; }
                    completion.SetException(e);
                }
            });
            return completion.Task;
        }

        public Task Run(Action<AudioService> work) { return Run(a => { work(a); return true; }); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; queue.CompleteAdding();
        }
    }
}

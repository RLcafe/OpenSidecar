using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetScreen
{
    // .NET Framework tries a host's addresses one at a time, IPv6 first. On networks where IPv6
    // is assigned but not routed, each attempt waits about 21 s before IPv4 is tried, which
    // outlasts our request timeouts while browsers (which race both) work fine. Skip IPv6 for
    // hosts that also have an IPv4 address; IPv6-only hosts are still reached over IPv6.
    public sealed class PreferIPv4Handler : HttpClientHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var point = ServicePointManager.FindServicePoint(request.RequestUri);
            if (point.BindIPEndPointDelegate == null) point.BindIPEndPointDelegate = Bind;
            return base.SendAsync(request, cancellationToken);
        }

        private static IPEndPoint Bind(ServicePoint point, IPEndPoint remote, int retryCount)
        {
            IPAddress[] addresses;
            try { addresses = remote.AddressFamily == AddressFamily.InterNetworkV6 ? Dns.GetHostAddresses(point.Address.Host) : new IPAddress[0]; }
            catch (Exception) { addresses = new IPAddress[0]; }
            // Throwing here makes the ServicePoint move on to the host's next address.
            if (SkipAddress(remote.Address, addresses)) throw new SocketException((int)SocketError.AddressFamilyNotSupported);
            return null;
        }

        public static bool SkipAddress(IPAddress remote, IPAddress[] hostAddresses)
        {
            return remote.AddressFamily == AddressFamily.InterNetworkV6 && hostAddresses != null && hostAddresses.Any(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
    }
}

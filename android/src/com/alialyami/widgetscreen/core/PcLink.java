package com.alialyami.widgetscreen.core;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.HttpURLConnection;
import java.net.InetAddress;
import java.net.SocketTimeoutException;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

// The phone's side of the link to the Windows app (decision 008): pairing, the dashboard state, commands, the
// Spotify proxy and cover art, over plain HTTP on the local network. Pure Java, so it is tested on the desktop.
public final class PcLink {
    public static final int PORT = 43830, DISCOVERY_PORT = 43831;
    public static final String DISCOVERY_REQUEST = "WIDGETSCREEN-DISCOVER";

    // A request the PC refused (code is the HTTP status), or offline (code 0).
    public static final class Failure extends Exception {
        public final int code;
        public final boolean noDevice;
        public Failure(int code, String message, boolean noDevice) { super(message); this.code = code; this.noDevice = noDevice; }
        public boolean offline() { return code == 0; }
        public boolean unpaired() { return code == 401; }
    }

    public static final class Found {
        public final String name, host;
        public final int port;
        public Found(String name, String host, int port) { this.name = name; this.host = host; this.port = port; }
        @Override public String toString() { return name + " (" + host + ")"; }
    }

    public final String host;
    public final int port;
    private final String token;

    public PcLink(String host, int port, String token) { this.host = host; this.port = port; this.token = token; }

    // Accepts "192.168.1.20" or "192.168.1.20:43830"; returns { host, port } or null. Only home-network addresses
    // (private, link-local, loopback, or a .local name) are accepted, so a typo can't send the token off the Wi-Fi.
    public static String[] parseAddress(String text) {
        if (text == null) return null;
        String value = text.trim();
        int port = PORT;
        int colon = value.lastIndexOf(':');
        if (colon > 0 && value.indexOf(':') == colon) {
            try { port = Integer.parseInt(value.substring(colon + 1)); } catch (NumberFormatException e) { return null; }
            value = value.substring(0, colon);
        }
        if (port <= 0 || port > 65535 || !value.matches("[A-Za-z0-9.-]{1,253}") || value.startsWith(".") || value.endsWith(".") || !isHomeAddress(value)) return null;
        return new String[] { value, Integer.toString(port) };
    }

    // A private, link-local or loopback IPv4 address, or a name only the local network resolves (.local, localhost).
    public static boolean isHomeAddress(String host) {
        if (host == null) return false;
        String value = host.toLowerCase(java.util.Locale.ROOT);
        if (value.equals("localhost") || value.endsWith(".local")) return true;
        String[] parts = value.split("\\.");
        if (parts.length != 4) return false;
        int[] octets = new int[4];
        for (int i = 0; i < 4; i++) {
            if (!parts[i].matches("[0-9]{1,3}")) return false;
            octets[i] = Integer.parseInt(parts[i]);
            if (octets[i] > 255) return false;
        }
        return octets[0] == 10 || octets[0] == 127 || (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31) || (octets[0] == 192 && octets[1] == 168) || (octets[0] == 169 && octets[1] == 254);
    }

    // How long to wait before asking an unreachable PC again: 5 s, then 15 s, then a minute, so a phone left on a
    // stand doesn't drain its battery knocking every 2 s. A tap or a settings change asks straight away.
    public static long retryDelayMs(int failures) {
        return failures <= 0 ? 0 : failures == 1 ? 5000 : failures == 2 ? 15000 : 60000;
    }

    // Where to send a discovery packet: each interface's own broadcast address (routers and many phones drop the
    // limited broadcast 255.255.255.255), with the limited broadcast last as a fallback.
    public static List<String> broadcastAddresses() {
        List<String> targets = new ArrayList<String>();
        try {
            java.util.Enumeration<java.net.NetworkInterface> interfaces = java.net.NetworkInterface.getNetworkInterfaces();
            while (interfaces != null && interfaces.hasMoreElements()) {
                java.net.NetworkInterface network = interfaces.nextElement();
                try { if (!network.isUp() || network.isLoopback()) continue; } catch (java.net.SocketException e) { continue; }
                for (java.net.InterfaceAddress address : network.getInterfaceAddresses()) {
                    InetAddress broadcast = address.getBroadcast();
                    if (broadcast == null) continue;
                    String text = broadcast.getHostAddress();
                    // Some adapters report the limited broadcast or no address at all; the fallback goes last.
                    if (text == null || text.equals("255.255.255.255") || text.equals("0.0.0.0") || targets.contains(text)) continue;
                    targets.add(text);
                }
            }
        } catch (java.net.SocketException ignored) { }
        targets.add("255.255.255.255");
        return targets;
    }

    private byte[] send(String method, String path, String body, int timeoutMs, int maxBytes) throws Failure {
        HttpURLConnection connection = null;
        try {
            connection = (HttpURLConnection) new URL("http", host, port, path).openConnection();
            connection.setRequestMethod(method);
            connection.setConnectTimeout(Math.min(timeoutMs, 4000));
            connection.setReadTimeout(timeoutMs);
            connection.setUseCaches(false);
            if (token != null) connection.setRequestProperty("Authorization", "Bearer " + token);
            if (body != null) {
                byte[] payload = body.getBytes(StandardCharsets.UTF_8);
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                connection.setFixedLengthStreamingMode(payload.length);
                OutputStream out = connection.getOutputStream();
                try { out.write(payload); } finally { out.close(); }
            }
            int code = connection.getResponseCode();
            InputStream in = code >= 400 ? connection.getErrorStream() : connection.getInputStream();
            byte[] data = read(in, maxBytes);
            if (code >= 200 && code < 300) return data;
            Map<String, Object> error;
            try { error = Json.obj(Json.parse(new String(data, StandardCharsets.UTF_8))); } catch (Exception e) { error = Json.obj(null); }
            String message = Json.field(error, "error");
            throw new Failure(code, message != null ? message : "The PC refused that (" + code + ").", Boolean.TRUE.equals(Json.value(error, "noDevice")));
        } catch (SocketTimeoutException e) {
            throw new Failure(0, "The PC didn't answer in time.", false);
        } catch (IOException e) {
            throw new Failure(0, "Can't reach the PC. Check that it's on, Widget Screen is open with phone control on, and this phone is on the same Wi-Fi.", false);
        } finally { if (connection != null) connection.disconnect(); }
    }

    private static byte[] read(InputStream in, int maxBytes) throws IOException {
        if (in == null) return new byte[0];
        try {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = in.read(buffer)) != -1) {
                out.write(buffer, 0, count);
                if (out.size() > maxBytes) throw new IOException("Too large.");
            }
            return out.toByteArray();
        } finally { in.close(); }
    }

    private Map<String, Object> json(String method, String path, Object body, int timeoutMs) throws Failure {
        byte[] data = send(method, path, body == null ? null : Json.write(body), timeoutMs, 4 * 1024 * 1024);
        try { return Json.obj(Json.parse(new String(data, StandardCharsets.UTF_8))); }
        catch (IllegalArgumentException e) { throw new Failure(502, "The PC sent an answer this app doesn't understand. Update Widget Screen on both.", false); }
    }

    public Map<String, Object> hello() throws Failure { return json("GET", "/hello", null, 4000); }

    // Returns the token; the PC shows the code in Settings → Phone.
    public String pair(String code, String device) throws Failure {
        String token = Json.field(json("POST", "/pair", Json.map("code", code == null ? "" : code.replace(" ", ""), "device", device), 8000), "token");
        if (token == null || token.isEmpty()) throw new Failure(502, "The PC didn't return a pairing. Try again.", false);
        return token;
    }

    public PcState state(List<String> parts) throws Failure {
        StringBuilder query = new StringBuilder();
        for (String part : parts) { if (query.length() > 0) query.append(','); query.append(part); }
        return PcState.parse(json("GET", "/state?parts=" + query, null, 8000));
    }

    // Returns the PC's message (often empty).
    public String command(Map<String, Object> body) throws Failure {
        String message = Json.field(json("POST", "/command", body, 10000), "message");
        return message == null ? "" : message;
    }

    public Map<String, Object> commandResult(Map<String, Object> body) throws Failure { return json("POST", "/command", body, 15000); }

    public Map<String, Object> spotify(Map<String, Object> body) throws Failure { return json("POST", "/spotify", body, 20000); }

    public byte[] art(String key) throws Failure { return send("GET", "/art?key=" + Spotify.encode(key == null ? "" : key), null, 8000, 4 * 1024 * 1024); }

    // Asks every Widget Screen PC on this network to answer; waits up to timeoutMs.
    public static List<Found> discover(int timeoutMs) {
        List<Found> found = new ArrayList<Found>();
        DatagramSocket socket = null;
        try {
            socket = new DatagramSocket();
            socket.setBroadcast(true);
            socket.setSoTimeout(300);
            byte[] ask = DISCOVERY_REQUEST.getBytes(StandardCharsets.UTF_8);
            for (String target : broadcastAddresses()) {
                try { socket.send(new DatagramPacket(ask, ask.length, InetAddress.getByName(target), DISCOVERY_PORT)); }
                catch (IOException ignored) { /* One interface refusing broadcast doesn't stop the others. */ }
            }
            long until = System.currentTimeMillis() + timeoutMs;
            byte[] buffer = new byte[2048];
            while (System.currentTimeMillis() < until) {
                DatagramPacket reply = new DatagramPacket(buffer, buffer.length);
                try { socket.receive(reply); } catch (SocketTimeoutException e) { continue; }
                Found pc = parseDiscovery(new String(reply.getData(), 0, reply.getLength(), StandardCharsets.UTF_8), reply.getAddress().getHostAddress());
                if (pc == null) continue;
                boolean seen = false;
                for (Found other : found) if (other.host.equals(pc.host) && other.port == pc.port) seen = true;
                if (!seen) found.add(pc);
            }
        } catch (IOException ignored) {
        } finally { if (socket != null) socket.close(); }
        return found;
    }

    public static Found parseDiscovery(String text, String host) {
        try {
            Map<String, Object> reply = Json.obj(Json.parse(text));
            if (!"Widget Screen".equals(Json.field(reply, "app"))) return null;
            int port = (int) Json.number(reply, "port");
            if (port <= 0 || port > 65535) return null;
            String name = Json.field(reply, "name");
            return new Found(name == null || name.isEmpty() ? host : name, host, port);
        } catch (IllegalArgumentException e) { return null; }
    }
}

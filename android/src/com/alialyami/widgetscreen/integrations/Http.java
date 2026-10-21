package com.alialyami.widgetscreen.integrations;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Map;

// Plain HttpURLConnection. When a host has IPv6 and IPv4 addresses, Android tries each in turn and the
// connect timeout applies per address, so the short connect timeout keeps an unrouted IPv6 address
// (the reason for PreferIPv4Handler on Windows) from stalling a request.
public final class Http {
    public static final class Response {
        public int code;
        public byte[] bytes;
        public Map<String, List<String>> headers;
        public String text() { return bytes == null ? "" : new String(bytes, StandardCharsets.UTF_8); }
        public String header(String name) {
            if (headers == null) return null;
            for (Map.Entry<String, List<String>> entry : headers.entrySet())
                if (entry.getKey() != null && entry.getKey().equalsIgnoreCase(name) && !entry.getValue().isEmpty()) return entry.getValue().get(0);
            return null;
        }
        public boolean ok() { return code >= 200 && code < 300; }
    }

    public static Response send(String method, String url, Map<String, String> headers, byte[] body, String contentType, int readTimeoutMs, int maxBytes) throws IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
        try {
            connection.setRequestMethod(method);
            connection.setConnectTimeout(6000);
            connection.setReadTimeout(readTimeoutMs);
            connection.setUseCaches(false);
            connection.setInstanceFollowRedirects(true);
            connection.setRequestProperty("Accept", "application/json");
            if (headers != null) for (Map.Entry<String, String> header : headers.entrySet()) connection.setRequestProperty(header.getKey(), header.getValue());
            boolean writes = body != null || method.equals("PUT") || method.equals("POST");
            if (writes) {
                byte[] payload = body == null ? new byte[0] : body;
                connection.setDoOutput(true);
                if (contentType != null) connection.setRequestProperty("Content-Type", contentType);
                connection.setFixedLengthStreamingMode(payload.length);
                OutputStream out = connection.getOutputStream();
                try { out.write(payload); } finally { out.close(); }
            }
            Response response = new Response();
            response.code = connection.getResponseCode();
            response.headers = connection.getHeaderFields();
            InputStream in = response.code >= 400 ? connection.getErrorStream() : connection.getInputStream();
            response.bytes = in == null ? new byte[0] : read(in, maxBytes);
            return response;
        } finally { connection.disconnect(); }
    }

    private static byte[] read(InputStream in, int maxBytes) throws IOException {
        try {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[16384];
            int count;
            while ((count = in.read(buffer)) != -1) {
                out.write(buffer, 0, count);
                if (out.size() > maxBytes) throw new IOException("The response is too large.");
            }
            return out.toByteArray();
        } finally { in.close(); }
    }

    public static String getText(String url) throws IOException {
        Response response = send("GET", url, null, null, null, 12000, 1024 * 1024);
        if (!response.ok()) throw new IOException("HTTP " + response.code);
        return response.text();
    }

    public static byte[] getBytes(String url, int maxBytes) throws IOException {
        Response response = send("GET", url, null, null, null, 10000, maxBytes);
        if (!response.ok()) throw new IOException("HTTP " + response.code);
        return response.bytes;
    }
}

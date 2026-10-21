package com.alialyami.widgetscreen.core;

import java.util.Map;

// One paired PC: where it is, its name and the token it gave this phone. Kept as one value so the saved copy and
// the one in use can't drift apart (a pairing that saved the token but didn't use it once left the phone silent).
public final class PcPairing {
    public final String host, token, name;
    public final int port;

    public PcPairing(String host, int port, String token, String name) {
        this.host = host; this.port = port; this.token = token; this.name = name == null ? "" : name;
    }

    public boolean paired() { return host != null && !host.isEmpty() && token != null && !token.isEmpty(); }

    public PcLink link() { return new PcLink(host, port, token); }

    public String toJson() { return Json.write(Json.map("host", host, "port", port, "token", token, "name", name)); }

    public static PcPairing fromJson(String json) {
        Map<String, Object> saved = Json.obj(Json.parse(json));
        return new PcPairing(Json.field(saved, "host"), Json.has(saved, "port") ? (int) Json.number(saved, "port") : PcLink.PORT, Json.field(saved, "token"), Json.field(saved, "name"));
    }

    public static PcPairing none() { return new PcPairing(null, PcLink.PORT, null, ""); }

    // Asks the PC for its name, pairs with the code it shows, and returns the pairing to keep.
    public static PcPairing pair(String host, int port, String code, String device) throws PcLink.Failure {
        PcLink fresh = new PcLink(host, port, null);
        String name = Json.field(fresh.hello(), "name");
        String token = fresh.pair(code, device);
        return new PcPairing(host, port, token, name == null || name.isEmpty() ? host : name);
    }
}

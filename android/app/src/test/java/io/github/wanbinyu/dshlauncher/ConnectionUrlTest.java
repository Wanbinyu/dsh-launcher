package io.github.wanbinyu.dshlauncher;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import java.net.URI;
import org.junit.Test;

public final class ConnectionUrlTest {
    @Test
    public void addsHttpAndRootPath() {
        assertEquals("http://192.168.1.25:3080/", ConnectionUrl.normalize("192.168.1.25:3080").toString());
    }

    @Test
    public void normalizesHostAndScheme() {
        assertEquals("https://example.com/", ConnectionUrl.normalize("HTTPS://EXAMPLE.COM").toString());
    }

    @Test
    public void comparesEffectivePorts() {
        assertTrue(ConnectionUrl.isSameOrigin(
            URI.create("http://example.com/"), URI.create("http://EXAMPLE.com:80/session")));
        assertFalse(ConnectionUrl.isSameOrigin(
            URI.create("http://example.com/"), URI.create("https://example.com/")));
    }

    @Test
    public void rejectsCredentialsAndNonRootPaths() {
        assertThrows(IllegalArgumentException.class, () -> ConnectionUrl.normalize("http://user:pass@host:3080/"));
        assertThrows(IllegalArgumentException.class, () -> ConnectionUrl.normalize("http://host:3080/api"));
        assertThrows(IllegalArgumentException.class, () -> ConnectionUrl.normalize("ftp://host/"));
    }

    @Test
    public void permitsCleartextOnlyForPrivateHosts() {
        assertEquals("http://10.0.0.8:3080/", ConnectionUrl.normalize("http://10.0.0.8:3080").toString());
        assertEquals("http://dsh-pc.local:3080/", ConnectionUrl.normalize("http://dsh-pc.local:3080").toString());
        assertThrows(IllegalArgumentException.class, () -> ConnectionUrl.normalize("http://example.com:3080"));
        assertEquals("https://example.com/", ConnectionUrl.normalize("https://example.com").toString());
    }
}

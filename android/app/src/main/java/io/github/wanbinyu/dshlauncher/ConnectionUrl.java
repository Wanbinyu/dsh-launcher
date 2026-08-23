package io.github.wanbinyu.dshlauncher;

import java.net.IDN;
import java.net.URI;
import java.net.URISyntaxException;
import java.util.Locale;

final class ConnectionUrl {
    private ConnectionUrl() {
    }

    static URI normalize(String input) {
        String value = input == null ? "" : input.trim();
        if (value.isEmpty()) {
            throw new IllegalArgumentException("empty");
        }
        if (!value.contains("://")) {
            value = "http://" + value;
        }

        try {
            URI parsed = new URI(value);
            String scheme = parsed.getScheme() == null
                ? ""
                : parsed.getScheme().toLowerCase(Locale.ROOT);
            if (!scheme.equals("http") && !scheme.equals("https")) {
                throw new IllegalArgumentException("scheme");
            }
            if (parsed.getUserInfo() != null || parsed.getQuery() != null || parsed.getFragment() != null) {
                throw new IllegalArgumentException("credentials-or-suffix");
            }

            String host = parsed.getHost();
            if (host == null || host.isBlank()) {
                throw new IllegalArgumentException("host");
            }
            if (scheme.equals("http") && !isPrivateHost(host)) {
                throw new IllegalArgumentException("public-cleartext");
            }
            int port = parsed.getPort();
            if (port == 0 || port > 65535) {
                throw new IllegalArgumentException("port");
            }

            String path = parsed.getPath();
            if (path != null && !path.isEmpty() && !path.equals("/")) {
                throw new IllegalArgumentException("path");
            }

            String asciiHost = host.contains(":") ? host : IDN.toASCII(host).toLowerCase(Locale.ROOT);
            return new URI(scheme, null, asciiHost, port, "/", null, null);
        } catch (URISyntaxException exception) {
            throw new IllegalArgumentException("syntax", exception);
        }
    }

    static boolean isSameOrigin(URI first, URI second) {
        return first.getScheme().equalsIgnoreCase(second.getScheme())
            && first.getHost().equalsIgnoreCase(second.getHost())
            && effectivePort(first) == effectivePort(second);
    }

    private static int effectivePort(URI uri) {
        if (uri.getPort() != -1) {
            return uri.getPort();
        }
        return uri.getScheme().equalsIgnoreCase("https") ? 443 : 80;
    }

    private static boolean isPrivateHost(String host) {
        String value = host.toLowerCase(Locale.ROOT);
        if (value.equals("localhost") || value.endsWith(".local") ||
            (!value.contains(".") && !value.contains(":"))) {
            return true;
        }
        if (value.contains(":")) {
            return value.equals("::1") || value.startsWith("fe8") || value.startsWith("fe9") ||
                value.startsWith("fea") || value.startsWith("feb") ||
                value.startsWith("fc") || value.startsWith("fd");
        }

        String[] parts = value.split("\\.");
        if (parts.length != 4) {
            return false;
        }
        try {
            int first = Integer.parseInt(parts[0]);
            int second = Integer.parseInt(parts[1]);
            for (String part : parts) {
                int octet = Integer.parseInt(part);
                if (octet < 0 || octet > 255) {
                    return false;
                }
            }
            return first == 10 || first == 127 ||
                (first == 169 && second == 254) ||
                (first == 172 && second >= 16 && second <= 31) ||
                (first == 192 && second == 168);
        } catch (NumberFormatException exception) {
            return false;
        }
    }
}

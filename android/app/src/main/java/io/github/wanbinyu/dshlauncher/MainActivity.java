package io.github.wanbinyu.dshlauncher;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.DownloadManager;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.net.ConnectivityManager;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowInsets;
import android.view.inputmethod.InputMethodManager;
import android.webkit.CookieManager;
import android.webkit.DownloadListener;
import android.webkit.SafeBrowsingResponse;
import android.webkit.SslErrorHandler;
import android.webkit.URLUtil;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import java.io.IOException;
import java.net.HttpURLConnection;
import java.net.URI;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;

public final class MainActivity extends Activity {
    private static final String PREFERENCES = "dsh-launcher";
    private static final String ENDPOINT_KEY = "endpoint";
    private static final String HOST_NOTICE_VERSION_KEY = "host-notice-version";
    private static final int HOST_NOTICE_VERSION = 1;
    private static final String CLIENT_USER_AGENT = "dsh-launcher-android/0.1.1";
    private static final int FILE_CHOOSER_REQUEST = 4107;

    private final ExecutorService networkExecutor = Executors.newSingleThreadExecutor();
    private final AtomicInteger connectionGeneration = new AtomicInteger();

    private SharedPreferences preferences;
    private View setupScreen;
    private View webScreen;
    private View loadingOverlay;
    private EditText endpointInput;
    private TextView connectionStatus;
    private TextView toolbarHost;
    private TextView loadingText;
    private ProgressBar webProgress;
    private WebView webView;
    private URI endpoint;
    private ValueCallback<Uri[]> fileChooserCallback;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
        applySystemBars();
        bindViews();
        configureActions();
        preferences = getSharedPreferences(PREFERENCES, MODE_PRIVATE);

        if (preferences.getInt(HOST_NOTICE_VERSION_KEY, 0) < HOST_NOTICE_VERSION) {
            showHostRequirementDialog(savedInstanceState);
        } else {
            continueStartup(savedInstanceState);
        }
    }

    private void continueStartup(Bundle savedInstanceState) {
        String savedEndpoint = preferences.getString(ENDPOINT_KEY, "");
        endpointInput.setText(savedEndpoint);
        if (savedEndpoint == null || savedEndpoint.isBlank()) {
            showSetup(null);
        } else if (savedInstanceState != null) {
            try {
                endpoint = ConnectionUrl.normalize(savedEndpoint);
                configureWebView();
                showWebScreen();
                if (webView.restoreState(savedInstanceState) == null) {
                    connect(endpoint, false);
                }
            } catch (IllegalArgumentException exception) {
                showSetup(getString(R.string.invalid_address));
            }
        } else {
            tryConnect(savedEndpoint);
        }
    }

    private void showHostRequirementDialog(Bundle savedInstanceState) {
        AlertDialog dialog = new AlertDialog.Builder(this)
            .setTitle(R.string.host_requirement_title)
            .setMessage(R.string.host_requirement_message)
            .setPositiveButton(R.string.host_requirement_confirm, (ignored, which) -> {
                preferences.edit()
                    .putInt(HOST_NOTICE_VERSION_KEY, HOST_NOTICE_VERSION)
                    .apply();
                continueStartup(savedInstanceState);
            })
            .setNegativeButton(R.string.exit, (ignored, which) -> finish())
            .create();
        dialog.setCanceledOnTouchOutside(false);
        dialog.setCancelable(false);
        dialog.show();
    }

    private void bindViews() {
        setupScreen = findViewById(R.id.connection_screen);
        webScreen = findViewById(R.id.web_screen);
        loadingOverlay = findViewById(R.id.loading_overlay);
        endpointInput = findViewById(R.id.endpoint_input);
        connectionStatus = findViewById(R.id.connection_status);
        toolbarHost = findViewById(R.id.toolbar_host);
        loadingText = findViewById(R.id.loading_text);
        webProgress = findViewById(R.id.web_progress);
        webView = findViewById(R.id.web_view);
    }

    private void configureActions() {
        Button connectButton = findViewById(R.id.connect_button);
        connectButton.setOnClickListener(view -> tryConnect(endpointInput.getText().toString()));
        endpointInput.setOnEditorActionListener((view, actionId, event) -> {
            tryConnect(endpointInput.getText().toString());
            return true;
        });

        ImageButton refresh = findViewById(R.id.action_refresh);
        refresh.setOnClickListener(view -> {
            if (webView.getUrl() != null) {
                webView.reload();
            }
        });

        ImageButton browser = findViewById(R.id.action_browser);
        browser.setOnClickListener(view -> {
            if (endpoint != null) {
                openExternal(Uri.parse(endpoint.toASCIIString()));
            }
        });

        ImageButton settingsButton = findViewById(R.id.action_settings);
        settingsButton.setOnClickListener(view -> showSetup(null));
    }

    private void tryConnect(String value) {
        endpointInput.setError(null);
        hideKeyboard();
        final URI normalized;
        try {
            normalized = ConnectionUrl.normalize(value);
        } catch (IllegalArgumentException exception) {
            endpointInput.setError(getString(R.string.invalid_address));
            showSetup(getString(R.string.invalid_address));
            return;
        }
        connect(normalized, true);
    }

    private void connect(URI normalized, boolean persistOnSuccess) {
        int generation = connectionGeneration.incrementAndGet();
        showLoading(getString(R.string.checking_connection));
        networkExecutor.execute(() -> {
            ProbeResult result = probe(normalized);
            runOnUiThread(() -> {
                if (isFinishing() || isDestroyed() || generation != connectionGeneration.get()) {
                    return;
                }
                if (!result.ready) {
                    showSetup(result.message);
                    return;
                }

                endpoint = normalized;
                endpointInput.setText(normalized.toASCIIString());
                if (persistOnSuccess) {
                    preferences.edit().putString(ENDPOINT_KEY, normalized.toASCIIString()).apply();
                }
                configureWebView();
                showWebScreen();
                showLoading(getString(R.string.opening_harness));
                webView.loadUrl(normalized.toASCIIString());
            });
        });
    }

    private ProbeResult probe(URI uri) {
        if (!hasNetwork()) {
            return new ProbeResult(false, getString(R.string.network_unavailable));
        }

        HttpURLConnection connection = null;
        try {
            connection = (HttpURLConnection) uri.toURL().openConnection();
            connection.setConnectTimeout(4000);
            connection.setReadTimeout(4000);
            connection.setInstanceFollowRedirects(true);
            connection.setRequestMethod("GET");
            connection.setRequestProperty("Accept", "text/html,application/xhtml+xml");
            connection.setRequestProperty("User-Agent", CLIENT_USER_AGENT);
            int status = connection.getResponseCode();
            if (status >= 200 && status < 400) {
                return new ProbeResult(true, "");
            }
            return new ProbeResult(false, getString(R.string.server_http_error, status));
        } catch (IOException exception) {
            return new ProbeResult(false, getString(R.string.cannot_reach_server));
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    private boolean hasNetwork() {
        ConnectivityManager manager = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
        return manager != null && manager.getActiveNetwork() != null;
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void configureWebView() {
        if (webView.getWebViewClient() instanceof HarnessWebViewClient) {
            return;
        }

        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setSupportMultipleWindows(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setMediaPlaybackRequiresUserGesture(true);
        settings.setUserAgentString(settings.getUserAgentString() + " " + CLIENT_USER_AGENT);

        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(webView, false);
        webView.setWebViewClient(new HarnessWebViewClient());
        webView.setWebChromeClient(new HarnessChromeClient());
        webView.setDownloadListener(createDownloadListener());
    }

    private DownloadListener createDownloadListener() {
        return (url, userAgent, contentDisposition, mimeType, contentLength) -> {
            try {
                String fileName = URLUtil.guessFileName(url, contentDisposition, mimeType);
                DownloadManager.Request request = new DownloadManager.Request(Uri.parse(url));
                request.setTitle(fileName);
                request.setDescription(getString(R.string.download_description));
                request.setMimeType(mimeType);
                request.setNotificationVisibility(
                    DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
                request.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, fileName);
                if (userAgent != null) {
                    request.addRequestHeader("User-Agent", userAgent);
                }
                String cookies = CookieManager.getInstance().getCookie(url);
                if (cookies != null) {
                    request.addRequestHeader("Cookie", cookies);
                }
                DownloadManager manager = (DownloadManager) getSystemService(DOWNLOAD_SERVICE);
                manager.enqueue(request);
                Toast.makeText(this, R.string.download_started, Toast.LENGTH_SHORT).show();
            } catch (RuntimeException exception) {
                openExternal(Uri.parse(url));
            }
        };
    }

    private void showSetup(String message) {
        connectionGeneration.incrementAndGet();
        loadingOverlay.setVisibility(View.GONE);
        webScreen.setVisibility(View.GONE);
        setupScreen.setVisibility(View.VISIBLE);
        connectionStatus.setText(message == null ? "" : message);
        connectionStatus.setVisibility(message == null || message.isBlank() ? View.GONE : View.VISIBLE);
    }

    private void showWebScreen() {
        setupScreen.setVisibility(View.GONE);
        webScreen.setVisibility(View.VISIBLE);
        toolbarHost.setText(endpoint == null ? "" : endpoint.getAuthority());
    }

    private void showLoading(String message) {
        setupScreen.setVisibility(View.GONE);
        loadingText.setText(message);
        loadingOverlay.setVisibility(View.VISIBLE);
    }

    private void hideLoading() {
        loadingOverlay.setVisibility(View.GONE);
    }

    private void openExternal(Uri uri) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, uri));
        } catch (ActivityNotFoundException exception) {
            Toast.makeText(this, R.string.no_browser, Toast.LENGTH_SHORT).show();
        }
    }

    private void hideKeyboard() {
        InputMethodManager input = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
        View focused = getCurrentFocus();
        if (input != null && focused != null) {
            input.hideSoftInputFromWindow(focused.getWindowToken(), 0);
        }
    }

    @SuppressWarnings("deprecation")
    private void applySystemBars() {
        getWindow().setStatusBarColor(Color.TRANSPARENT);
        getWindow().setNavigationBarColor(getColor(R.color.surface));
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR);
        if (Build.VERSION.SDK_INT >= 30) {
            getWindow().setDecorFitsSystemWindows(false);
            View root = findViewById(R.id.root);
            root.setOnApplyWindowInsetsListener((view, insets) -> {
                android.graphics.Insets bars = insets.getInsets(
                    WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
                return insets;
            });
        }
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        super.onSaveInstanceState(outState);
        webView.saveState(outState);
    }

    @SuppressWarnings("deprecation")
    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != FILE_CHOOSER_REQUEST || fileChooserCallback == null) {
            return;
        }
        fileChooserCallback.onReceiveValue(
            WebChromeClient.FileChooserParams.parseResult(resultCode, data));
        fileChooserCallback = null;
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK && setupScreen.getVisibility() == View.VISIBLE &&
            webView.getUrl() != null && endpoint != null) {
            showWebScreen();
            return true;
        }
        if (keyCode == KeyEvent.KEYCODE_BACK && webScreen.getVisibility() == View.VISIBLE && webView.canGoBack()) {
            webView.goBack();
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }

    @Override
    protected void onDestroy() {
        connectionGeneration.incrementAndGet();
        networkExecutor.shutdownNow();
        if (fileChooserCallback != null) {
            fileChooserCallback.onReceiveValue(null);
            fileChooserCallback = null;
        }
        webView.stopLoading();
        webView.setWebChromeClient(null);
        webView.setWebViewClient(null);
        webView.destroy();
        super.onDestroy();
    }

    private final class HarnessWebViewClient extends WebViewClient {
        @Override
        public void onPageStarted(WebView view, String url, android.graphics.Bitmap favicon) {
            showLoading(getString(R.string.opening_harness));
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            hideLoading();
            webProgress.setVisibility(View.GONE);
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            Uri requested = request.getUrl();
            try {
                URI target = new URI(requested.toString());
                if (endpoint != null && ConnectionUrl.isSameOrigin(endpoint, target)) {
                    return false;
                }
            } catch (Exception ignored) {
            }
            openExternal(requested);
            return true;
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            if (request.isForMainFrame()) {
                showSetup(getString(R.string.cannot_reach_server));
            }
        }

        @Override
        public void onReceivedHttpError(
            WebView view,
            WebResourceRequest request,
            WebResourceResponse errorResponse) {
            if (request.isForMainFrame() && errorResponse.getStatusCode() >= 400) {
                showSetup(getString(R.string.server_http_error, errorResponse.getStatusCode()));
            }
        }

        @Override
        public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
            handler.cancel();
            showSetup(getString(R.string.ssl_error));
        }

        @Override
        public void onSafeBrowsingHit(
            WebView view,
            WebResourceRequest request,
            int threatType,
            SafeBrowsingResponse callback) {
            callback.backToSafety(true);
            showSetup(getString(R.string.unsafe_page));
        }
    }

    private final class HarnessChromeClient extends WebChromeClient {
        @Override
        public void onProgressChanged(WebView view, int newProgress) {
            webProgress.setProgress(newProgress);
            webProgress.setVisibility(newProgress >= 100 ? View.GONE : View.VISIBLE);
        }

        @SuppressWarnings("deprecation")
        @Override
        public boolean onShowFileChooser(
            WebView webView,
            ValueCallback<Uri[]> callback,
            FileChooserParams fileChooserParams) {
            if (fileChooserCallback != null) {
                fileChooserCallback.onReceiveValue(null);
            }
            fileChooserCallback = callback;
            try {
                startActivityForResult(fileChooserParams.createIntent(), FILE_CHOOSER_REQUEST);
                return true;
            } catch (ActivityNotFoundException exception) {
                fileChooserCallback = null;
                Toast.makeText(MainActivity.this, R.string.no_file_picker, Toast.LENGTH_SHORT).show();
                return false;
            }
        }
    }

    private static final class ProbeResult {
        private final boolean ready;
        private final String message;

        private ProbeResult(boolean ready, String message) {
            this.ready = ready;
            this.message = message;
        }
    }
}

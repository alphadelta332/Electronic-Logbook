package com.alphadelta.electroniclogbook;

import android.os.Bundle;
import android.webkit.WebView;
import androidx.activity.OnBackPressedCallback;
import androidx.core.graphics.Insets;
import androidx.core.view.ViewCompat;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsCompat;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        registerPlugin(ElectronicLogbookNativeFilesPlugin.class);
        registerPlugin(ElectronicLogbookCredentialsPlugin.class);
        super.onCreate(savedInstanceState);
        WindowCompat.setDecorFitsSystemWindows(getWindow(), true);
        configureKeyboardInsets();
        configureBackNavigation();
    }

    private void configureKeyboardInsets() {
        if (getBridge() == null || getBridge().getWebView() == null) {
            return;
        }

        WebView webView = getBridge().getWebView();
        ViewCompat.setOnApplyWindowInsetsListener(webView, (view, windowInsets) -> {
            Insets imeInsets = windowInsets.getInsets(WindowInsetsCompat.Type.ime());
            Insets systemBarInsets = windowInsets.getInsets(WindowInsetsCompat.Type.systemBars());
            int keyboardHeight = windowInsets.isVisible(WindowInsetsCompat.Type.ime())
                ? Math.max(0, imeInsets.bottom - systemBarInsets.bottom)
                : 0;

            view.post(() -> webView.evaluateJavascript(
                "window.electronicLogbookKeyboard?.setInset?.(" + keyboardHeight + ")",
                null));
            return windowInsets;
        });
        ViewCompat.requestApplyInsets(webView);
    }

    private void configureBackNavigation() {
        OnBackPressedCallback callback = new OnBackPressedCallback(true) {
            @Override
            public void handleOnBackPressed() {
                dispatchBackToWebView(this);
            }
        };
        getOnBackPressedDispatcher().addCallback(this, callback);
    }

    private void dispatchBackToWebView(OnBackPressedCallback callback) {
        if (getBridge() == null || getBridge().getWebView() == null) {
            dispatchDefaultBack(callback);
            return;
        }

        getBridge().getWebView().evaluateJavascript(
            "window.electronicLogbookNavigation?.handleAndroidBack?.() === true",
            handled -> {
                if (!"true".equals(handled)) {
                    dispatchDefaultBack(callback);
                }
            });
    }

    private void dispatchDefaultBack(OnBackPressedCallback callback) {
        callback.setEnabled(false);
        getOnBackPressedDispatcher().onBackPressed();
        callback.setEnabled(true);
    }
}

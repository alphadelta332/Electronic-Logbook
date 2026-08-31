package com.alphadelta.electroniclogbook;

import android.os.Bundle;
import androidx.activity.OnBackPressedCallback;
import androidx.core.view.WindowCompat;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        registerPlugin(ElectronicLogbookNativeFilesPlugin.class);
        registerPlugin(ElectronicLogbookCredentialsPlugin.class);
        registerPlugin(ElectronicLogbookPilotUpdatesPlugin.class);
        super.onCreate(savedInstanceState);
        WindowCompat.setDecorFitsSystemWindows(getWindow(), true);
        configureBackNavigation();
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

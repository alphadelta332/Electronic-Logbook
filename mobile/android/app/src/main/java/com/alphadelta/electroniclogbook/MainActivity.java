package com.alphadelta.electroniclogbook;

import android.os.Bundle;
import androidx.core.view.WindowCompat;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        registerPlugin(ElectronicLogbookNativeFilesPlugin.class);
        registerPlugin(ElectronicLogbookCredentialsPlugin.class);
        super.onCreate(savedInstanceState);
        WindowCompat.setDecorFitsSystemWindows(getWindow(), true);
    }

    @Override
    public void onBackPressed() {
        if (getBridge() == null || getBridge().getWebView() == null) {
            super.onBackPressed();
            return;
        }

        getBridge().getWebView().evaluateJavascript(
            "window.electronicLogbookNavigation?.handleAndroidBack?.() === true",
            handled -> {
                if (!"true".equals(handled)) {
                    MainActivity.super.onBackPressed();
                }
            });
    }
}

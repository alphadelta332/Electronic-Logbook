package com.alphadelta.electroniclogbook;

import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.os.Bundle;
import android.util.Log;
import androidx.activity.OnBackPressedCallback;
import androidx.core.view.WindowCompat;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    private static final String WEB_ASSET_STATE = "flightlogx_web_asset_state";
    private static final String LAST_UPDATE_TIME = "last_update_time";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        registerPlugin(ElectronicLogbookNativeFilesPlugin.class);
        registerPlugin(ElectronicLogbookCredentialsPlugin.class);
        registerPlugin(ElectronicLogbookPreviewUpdatesPlugin.class);
        super.onCreate(savedInstanceState);
        WindowCompat.setDecorFitsSystemWindows(getWindow(), true);
        refreshBundledWebAssetsAfterUpdate();
        configureBackNavigation();
    }

    @Override
    public void onResume() {
        super.onResume();
        ElectronicLogbookPreviewUpdatesPlugin.checkAndPromptOnResume();
    }

    private void refreshBundledWebAssetsAfterUpdate() {
        try {
            long installedUpdateTime = getPackageManager()
                .getPackageInfo(getPackageName(), 0)
                .lastUpdateTime;
            SharedPreferences state = getSharedPreferences(WEB_ASSET_STATE, MODE_PRIVATE);
            if (state.getLong(LAST_UPDATE_TIME, -1L) == installedUpdateTime ||
                getBridge() == null ||
                getBridge().getWebView() == null) {
                return;
            }

            getBridge().getWebView().clearCache(true);
            state.edit().putLong(LAST_UPDATE_TIME, installedUpdateTime).apply();
            getBridge().getWebView().reload();
        }
        catch (PackageManager.NameNotFoundException exception) {
            Log.w("FlightLogX", "Could not compare the installed app update time.", exception);
        }
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

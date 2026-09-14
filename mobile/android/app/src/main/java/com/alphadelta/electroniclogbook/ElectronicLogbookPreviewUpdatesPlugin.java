package com.alphadelta.electroniclogbook;

import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.google.firebase.appdistribution.FirebaseAppDistribution;
import java.util.concurrent.atomic.AtomicBoolean;

@CapacitorPlugin(name = "ElectronicLogbookPreviewUpdates")
public class ElectronicLogbookPreviewUpdatesPlugin extends Plugin {
    private static final AtomicBoolean AUTOMATIC_CHECK_IN_FLIGHT = new AtomicBoolean(false);
    private static final String CHANGELOG_STATE = "flightlogx_preview_changelog_state";
    private static final String ACKNOWLEDGED_VERSION_CODE = "acknowledged_version_code";

    static void checkAndPromptOnResume() {
        if (!BuildConfig.PREVIEW_UPDATES_ENABLED ||
            !AUTOMATIC_CHECK_IN_FLIGHT.compareAndSet(false, true)) {
            return;
        }

        FirebaseAppDistribution.getInstance()
            .updateIfNewReleaseAvailable()
            .addOnCompleteListener(task -> AUTOMATIC_CHECK_IN_FLIGHT.set(false));
    }

    @PluginMethod
    public void isAvailable(PluginCall call) {
        JSObject result = new JSObject();
        result.put("enabled", BuildConfig.PREVIEW_UPDATES_ENABLED);
        call.resolve(result);
    }

    @PluginMethod
    public void getInstallationInfo(PluginCall call) {
        try {
            PackageInfo packageInfo = getContext()
                .getPackageManager()
                .getPackageInfo(getContext().getPackageName(), 0);
            SharedPreferences state = getContext().getSharedPreferences(
                CHANGELOG_STATE,
                android.content.Context.MODE_PRIVATE);

            JSObject result = new JSObject();
            result.put("enabled", BuildConfig.PREVIEW_UPDATES_ENABLED);
            result.put("versionName", BuildConfig.VERSION_NAME);
            result.put("versionCode", (long) BuildConfig.VERSION_CODE);
            result.put("wasUpdated", packageInfo.lastUpdateTime > packageInfo.firstInstallTime);
            result.put(
                "acknowledgedVersionCode",
                state.getLong(ACKNOWLEDGED_VERSION_CODE, 0L));
            call.resolve(result);
        }
        catch (PackageManager.NameNotFoundException exception) {
            call.reject(
                "FlightLogX could not read the installed Preview version.",
                "PREVIEW_VERSION_UNAVAILABLE",
                exception);
        }
    }

    @PluginMethod
    public void acknowledgeInstalledVersion(PluginCall call) {
        if (!BuildConfig.PREVIEW_UPDATES_ENABLED) {
            call.resolve();
            return;
        }

        getContext()
            .getSharedPreferences(CHANGELOG_STATE, android.content.Context.MODE_PRIVATE)
            .edit()
            .putLong(ACKNOWLEDGED_VERSION_CODE, (long) BuildConfig.VERSION_CODE)
            .apply();
        call.resolve();
    }

    @PluginMethod
    public void checkAndInstall(PluginCall call) {
        if (!BuildConfig.PREVIEW_UPDATES_ENABLED) {
            call.reject(
                "Preview updates are not included in this FlightLogX build.",
                "PREVIEW_UPDATES_UNAVAILABLE");
            return;
        }

        getActivity().runOnUiThread(() -> FirebaseAppDistribution.getInstance()
            .updateIfNewReleaseAvailable()
            .addOnSuccessListener(release -> {
                JSObject result = new JSObject();
                result.put("outcome", release == null ? "current" : "updateStarted");
                call.resolve(result);
            })
            .addOnFailureListener(exception -> call.reject(
                "FlightLogX could not check Firebase App Distribution for a Preview update.",
                "PREVIEW_UPDATE_FAILED",
                exception)));
    }
}

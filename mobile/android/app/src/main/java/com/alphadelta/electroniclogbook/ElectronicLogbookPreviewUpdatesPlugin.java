package com.alphadelta.electroniclogbook;

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

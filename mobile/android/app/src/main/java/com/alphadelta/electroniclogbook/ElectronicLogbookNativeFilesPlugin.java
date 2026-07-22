package com.alphadelta.electroniclogbook;

import android.content.Intent;
import android.net.Uri;
import androidx.core.content.FileProvider;
import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import org.json.JSONException;

@CapacitorPlugin(name = "ElectronicLogbookNativeFiles")
public class ElectronicLogbookNativeFilesPlugin extends Plugin {
    private static final int MaxElogbookBytes = 64 * 1024 * 1024;

    @PluginMethod
    public void saveAndShare(PluginCall call) {
        String fileName = call.getString("fileName");
        String contentType = call.getString("contentType", "application/octet-stream");
        JSArray bytes = call.getArray("bytes");

        if (fileName == null || !fileName.endsWith(".elogbook")) {
            call.reject("Exported package file names must use the .elogbook extension.");
            return;
        }

        if (bytes == null || bytes.length() == 0) {
            call.reject("Exported package is empty.");
            return;
        }

        if (bytes.length() > MaxElogbookBytes) {
            call.reject("Exported package is larger than the 67108864 byte package limit.");
            return;
        }

        try {
            byte[] fileBytes = toByteArray(bytes);
            File exportDirectory = new File(getContext().getExternalFilesDir(null), "exports");
            if (!exportDirectory.exists() && !exportDirectory.mkdirs()) {
                call.reject("Could not create export directory.");
                return;
            }

            File exportedFile = new File(exportDirectory, fileName);
            try (FileOutputStream stream = new FileOutputStream(exportedFile, false)) {
                stream.write(fileBytes);
            }

            Uri uri = FileProvider.getUriForFile(
                getContext(),
                getContext().getPackageName() + ".fileprovider",
                exportedFile);
            Intent shareIntent = new Intent(Intent.ACTION_SEND);
            shareIntent.setType(contentType);
            shareIntent.putExtra(Intent.EXTRA_STREAM, uri);
            shareIntent.putExtra(Intent.EXTRA_TITLE, fileName);
            shareIntent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            getActivity().startActivity(Intent.createChooser(shareIntent, "Export package"));

            JSObject result = new JSObject();
            result.put("fileName", fileName);
            result.put("devicePath", exportedFile.getAbsolutePath());
            result.put("adbPath", "/sdcard/Android/data/" + getContext().getPackageName() + "/files/exports/" + fileName);
            result.put("shared", true);
            call.resolve(result);
        } catch (JSONException | IOException ex) {
            call.reject(ex.getMessage(), ex);
        }
    }

    private static byte[] toByteArray(JSArray values) throws JSONException {
        byte[] bytes = new byte[values.length()];
        for (int index = 0; index < values.length(); index++) {
            bytes[index] = (byte) (values.getInt(index) & 0xff);
        }

        return bytes;
    }
}

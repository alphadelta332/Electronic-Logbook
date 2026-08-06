package com.alphadelta.electroniclogbook;

import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
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
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.KeyStore;
import java.security.SecureRandom;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;
import org.json.JSONException;

@CapacitorPlugin(name = "ElectronicLogbookNativeFiles")
public class ElectronicLogbookNativeFilesPlugin extends Plugin {
    private static final int MaxElogbookBytes = 64 * 1024 * 1024;
    private static final int PackageKeySizeBytes = 32;
    private static final int AesGcmNonceSizeBytes = 12;
    private static final int AesGcmTagSizeBytes = 16;
    private static final String AndroidKeyStore = "AndroidKeyStore";
    private static final String WrapperKeyAlias = "electronic-logbook.package-key-wrapper";
    private static final String NativeKeyPreferences = "electronic_logbook_native_keys";

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

    @PluginMethod
    public void hasPackageKey(PluginCall call) {
        String keyName = call.getString("keyName");
        if (!isValidKeyName(keyName, call)) {
            return;
        }

        JSObject result = new JSObject();
        result.put("exists", preferences().contains(keyName + ".ciphertext"));
        call.resolve(result);
    }

    @PluginMethod
    public void ensurePackageKey(PluginCall call) {
        String keyName = call.getString("keyName");
        if (!isValidKeyName(keyName, call)) {
            return;
        }

        try {
            boolean created = false;
            if (!preferences().contains(keyName + ".ciphertext")) {
                byte[] keyBytes = new byte[PackageKeySizeBytes];
                new SecureRandom().nextBytes(keyBytes);
                storePackageKey(keyName, keyBytes);
                Arrays.fill(keyBytes, (byte) 0);
                created = true;
            }

            JSObject result = new JSObject();
            result.put("created", created);
            call.resolve(result);
        } catch (GeneralSecurityException ex) {
            call.reject("Could not create Android Keystore-backed package key.", ex);
        }
    }

    @PluginMethod
    public void importPackageKey(PluginCall call) {
        String keyName = call.getString("keyName");
        JSArray keyBytes = call.getArray("keyBytes");
        if (!isValidKeyName(keyName, call) || !isValidByteArray(keyBytes, PackageKeySizeBytes, "Package key", call)) {
            return;
        }

        try {
            byte[] rawKey = toByteArray(keyBytes);
            storePackageKey(keyName, rawKey);
            Arrays.fill(rawKey, (byte) 0);
            JSObject result = new JSObject();
            result.put("imported", true);
            call.resolve(result);
        } catch (GeneralSecurityException | JSONException ex) {
            call.reject("Could not import Android Keystore-backed package key.", ex);
        }
    }

    @PluginMethod
    public void deletePackageKey(PluginCall call) {
        String keyName = call.getString("keyName");
        if (!isValidKeyName(keyName, call)) {
            return;
        }

        preferences()
            .edit()
            .remove(keyName + ".nonce")
            .remove(keyName + ".ciphertext")
            .apply();
        call.resolve();
    }

    @PluginMethod
    public void encryptPackagePayload(PluginCall call) {
        cryptPackagePayload(call, true);
    }

    @PluginMethod
    public void decryptPackagePayload(PluginCall call) {
        cryptPackagePayload(call, false);
    }

    private static byte[] toByteArray(JSArray values) throws JSONException {
        byte[] bytes = new byte[values.length()];
        for (int index = 0; index < values.length(); index++) {
            bytes[index] = (byte) (values.getInt(index) & 0xff);
        }

        return bytes;
    }

    private void cryptPackagePayload(PluginCall call, boolean encrypt) {
        String keyName = call.getString("keyName");
        JSArray nonce = call.getArray("nonce");
        JSArray additionalData = call.getArray("additionalData");
        JSArray payload = encrypt ? call.getArray("plaintext") : call.getArray("ciphertext");
        JSArray tag = encrypt ? null : call.getArray("tag");
        if (!isValidKeyName(keyName, call)
            || !isValidByteArray(nonce, AesGcmNonceSizeBytes, "AES-GCM nonce", call)
            || !isPresentByteArray(additionalData, "AES-GCM additional data", call)
            || !isPresentByteArray(payload, encrypt ? "Plaintext" : "Ciphertext", call)
            || (!encrypt && !isValidByteArray(tag, AesGcmTagSizeBytes, "AES-GCM tag", call))) {
            return;
        }

        byte[] packageKey = null;
        try {
            packageKey = loadPackageKey(keyName);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            SecretKeySpec keySpec = new SecretKeySpec(packageKey, "AES");
            byte[] nonceBytes = toByteArray(nonce);
            byte[] aadBytes = toByteArray(additionalData);
            cipher.init(encrypt ? Cipher.ENCRYPT_MODE : Cipher.DECRYPT_MODE, keySpec, new GCMParameterSpec(128, nonceBytes));
            cipher.updateAAD(aadBytes);

            if (encrypt) {
                byte[] encrypted = cipher.doFinal(toByteArray(payload));
                JSObject result = new JSObject();
                result.put("ciphertext", toJsArray(Arrays.copyOf(encrypted, encrypted.length - AesGcmTagSizeBytes)));
                result.put("tag", toJsArray(Arrays.copyOfRange(encrypted, encrypted.length - AesGcmTagSizeBytes, encrypted.length)));
                call.resolve(result);
            } else {
                byte[] ciphertext = toByteArray(payload);
                byte[] tagBytes = toByteArray(tag);
                byte[] encrypted = new byte[ciphertext.length + tagBytes.length];
                System.arraycopy(ciphertext, 0, encrypted, 0, ciphertext.length);
                System.arraycopy(tagBytes, 0, encrypted, ciphertext.length, tagBytes.length);
                JSObject result = new JSObject();
                result.put("plaintext", toJsArray(cipher.doFinal(encrypted)));
                call.resolve(result);
            }
        } catch (GeneralSecurityException | JSONException ex) {
            call.reject(encrypt ? "Could not encrypt with Android Keystore-backed package key." : "Could not decrypt with Android Keystore-backed package key.", ex);
        } finally {
            if (packageKey != null) {
                Arrays.fill(packageKey, (byte) 0);
            }
        }
    }

    private SharedPreferences preferences() {
        return getContext().getSharedPreferences(NativeKeyPreferences, 0);
    }

    private void storePackageKey(String keyName, byte[] keyBytes) throws GeneralSecurityException {
        byte[] nonce = new byte[AesGcmNonceSizeBytes];
        new SecureRandom().nextBytes(nonce);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateWrapperKey(), new GCMParameterSpec(128, nonce));
        cipher.updateAAD(keyName.getBytes(StandardCharsets.UTF_8));
        byte[] ciphertext = cipher.doFinal(keyBytes);
        preferences()
            .edit()
            .putString(keyName + ".nonce", android.util.Base64.encodeToString(nonce, android.util.Base64.NO_WRAP))
            .putString(keyName + ".ciphertext", android.util.Base64.encodeToString(ciphertext, android.util.Base64.NO_WRAP))
            .apply();
    }

    private byte[] loadPackageKey(String keyName) throws GeneralSecurityException {
        String nonce = preferences().getString(keyName + ".nonce", null);
        String ciphertext = preferences().getString(keyName + ".ciphertext", null);
        if (nonce == null || ciphertext == null) {
            throw new GeneralSecurityException("Package key is not available.");
        }

        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(
            Cipher.DECRYPT_MODE,
            getOrCreateWrapperKey(),
            new GCMParameterSpec(128, android.util.Base64.decode(nonce, android.util.Base64.NO_WRAP)));
        cipher.updateAAD(keyName.getBytes(StandardCharsets.UTF_8));
        return cipher.doFinal(android.util.Base64.decode(ciphertext, android.util.Base64.NO_WRAP));
    }

    private SecretKey getOrCreateWrapperKey() throws GeneralSecurityException {
        KeyStore keyStore = KeyStore.getInstance(AndroidKeyStore);
        try {
            keyStore.load(null);
        } catch (IOException | java.security.cert.CertificateException ex) {
            throw new GeneralSecurityException("Could not load Android Keystore.", ex);
        }

        SecretKey existing = (SecretKey) keyStore.getKey(WrapperKeyAlias, null);
        if (existing != null) {
            return existing;
        }

        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, AndroidKeyStore);
        generator.init(new KeyGenParameterSpec.Builder(
            WrapperKeyAlias,
            KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .setRandomizedEncryptionRequired(true)
            .build());
        return generator.generateKey();
    }

    private static boolean isValidKeyName(String keyName, PluginCall call) {
        if (keyName == null || keyName.trim().isEmpty()) {
            call.reject("Package key name is required.");
            return false;
        }

        return true;
    }

    private static boolean isValidByteArray(JSArray values, int expectedLength, String label, PluginCall call) {
        if (!isPresentByteArray(values, label, call)) {
            return false;
        }

        if (values.length() != expectedLength) {
            call.reject(label + " must be " + expectedLength + " bytes.");
            return false;
        }

        return true;
    }

    private static boolean isPresentByteArray(JSArray values, String label, PluginCall call) {
        if (values == null) {
            call.reject(label + " is required.");
            return false;
        }

        return true;
    }

    private static JSArray toJsArray(byte[] bytes) {
        JSArray result = new JSArray();
        for (byte value : bytes) {
            result.put(value & 0xff);
        }

        return result;
    }
}

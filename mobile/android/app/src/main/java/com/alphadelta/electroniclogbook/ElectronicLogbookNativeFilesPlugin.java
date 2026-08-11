package com.alphadelta.electroniclogbook;

import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Build;
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
import java.security.KeyFactory;
import java.security.KeyPairGenerator;
import java.security.MessageDigest;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.SecureRandom;
import java.security.spec.MGF1ParameterSpec;
import java.security.spec.X509EncodedKeySpec;
import java.security.spec.KeySpec;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.OAEPParameterSpec;
import javax.crypto.spec.PSource;
import javax.crypto.spec.PBEKeySpec;
import javax.crypto.spec.SecretKeySpec;
import org.json.JSONException;

@CapacitorPlugin(name = "ElectronicLogbookNativeFiles")
public class ElectronicLogbookNativeFilesPlugin extends Plugin {
    private static final int MaxElogbookBytes = 64 * 1024 * 1024;
    private static final int PackageKeySizeBytes = 32;
    private static final int AesGcmNonceSizeBytes = 12;
    private static final int AesGcmTagSizeBytes = 16;
    private static final String AndroidKeyStore = "AndroidKeyStore";
    private static final String WrapperKeyAlias = "electronic-logbook.package-key-wrapper.v2";
    private static final String RecoveryKeyAlias = "electronic-logbook.recovery-key.rsa-oaep-sha256-mgf1-sha256.v2";
    private static final String NativeKeyPreferences = "electronic_logbook_native_keys";
    private static final String RecoveryCodeAlgorithm = "PBKDF2-SHA256-600000+A256GCM";
    private static final String RecoveryCodeKeyVersion = "recovery-code-v1";
    private static final int RecoveryCodeIterations = 600000;
    private static final OAEPParameterSpec RecoveryOaepParameters = new OAEPParameterSpec(
        "SHA-256",
        "MGF1",
        MGF1ParameterSpec.SHA256,
        PSource.PSpecified.DEFAULT);

    @PluginMethod
    public void saveAndShare(PluginCall call) {
        String fileName = call.getString("fileName");
        String contentType = call.getString("contentType", "application/octet-stream");
        JSArray bytes = call.getArray("bytes");

        if (!isSupportedExportFileName(fileName)) {
            call.reject("Exported file names must be a plain .elogbook or .json file name.");
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
            getActivity().startActivity(Intent.createChooser(shareIntent, "Export file"));

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

    private static boolean isSupportedExportFileName(String fileName) {
        if (fileName == null || fileName.isBlank() || fileName.contains("/") || fileName.contains("\\")) {
            return false;
        }

        String lowerName = fileName.toLowerCase(java.util.Locale.ROOT);
        return lowerName.endsWith(".elogbook") || lowerName.endsWith(".json");
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
    public void getRecoveryPublicKey(PluginCall call) {
        try {
            PublicKey publicKey = getOrCreateRecoveryPublicKey();
            byte[] encoded = publicKey.getEncoded();
            JSObject result = new JSObject();
            result.put("publicKey", android.util.Base64.encodeToString(encoded, android.util.Base64.NO_WRAP));
            result.put("fingerprint", toLowerHex(MessageDigest.getInstance("SHA-256").digest(encoded)));
            result.put("algorithm", "RSA-OAEP-256");
            call.resolve(result);
        } catch (GeneralSecurityException ex) {
            call.reject("Could not create the Android Keystore recovery key.", "RECOVERY_KEY_CREATE_FAILED", ex);
        }
    }

    @PluginMethod
    public void wrapPackageKeyForRecoveryService(PluginCall call) {
        String keyName = call.getString("keyName");
        String servicePublicKey = call.getString("servicePublicKey");
        if (!isValidKeyName(keyName, call) || servicePublicKey == null || servicePublicKey.isBlank()) {
            if (servicePublicKey == null || servicePublicKey.isBlank()) {
                call.reject("Recovery service public key is required.", "RECOVERY_SERVICE_KEY_MISSING");
            }
            return;
        }

        byte[] packageKey = null;
        try {
            packageKey = loadPackageKey(keyName);
            PublicKey publicKey = KeyFactory.getInstance("RSA").generatePublic(
                new X509EncodedKeySpec(android.util.Base64.decode(servicePublicKey, android.util.Base64.NO_WRAP)));
            Cipher cipher = Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding");
            cipher.init(Cipher.ENCRYPT_MODE, publicKey, RecoveryOaepParameters);
            JSObject result = new JSObject();
            result.put(
                "wrappedKey",
                android.util.Base64.encodeToString(cipher.doFinal(packageKey), android.util.Base64.NO_WRAP));
            result.put("algorithm", "RSA-OAEP-256");
            call.resolve(result);
        } catch (GeneralSecurityException | IllegalArgumentException ex) {
            call.reject("Could not wrap the package key for account recovery.", "RECOVERY_KEY_WRAP_FAILED", ex);
        } finally {
            if (packageKey != null) {
                Arrays.fill(packageKey, (byte) 0);
            }
        }
    }

    @PluginMethod
    public void importRecoveryEnvelope(PluginCall call) {
        String keyName = call.getString("keyName");
        String wrappedKey = call.getString("wrappedKey");
        if (!isValidKeyName(keyName, call) || wrappedKey == null || wrappedKey.isBlank()) {
            if (wrappedKey == null || wrappedKey.isBlank()) {
                call.reject("Device-wrapped recovery envelope is required.", "RECOVERY_ENVELOPE_MISSING");
            }
            return;
        }

        byte[] packageKey = null;
        try {
            Cipher cipher = Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding");
            cipher.init(Cipher.DECRYPT_MODE, getRecoveryPrivateKey(), RecoveryOaepParameters);
            packageKey = cipher.doFinal(android.util.Base64.decode(wrappedKey, android.util.Base64.NO_WRAP));
            if (packageKey.length != PackageKeySizeBytes) {
                throw new GeneralSecurityException("Recovered package key has the wrong length.");
            }

            storePackageKey(keyName, packageKey);
            JSObject result = new JSObject();
            result.put("imported", true);
            call.resolve(result);
        } catch (GeneralSecurityException | IllegalArgumentException ex) {
            call.reject("Could not import the device-wrapped recovery envelope.", "RECOVERY_ENVELOPE_IMPORT_FAILED", ex);
        } finally {
            if (packageKey != null) {
                Arrays.fill(packageKey, (byte) 0);
            }
        }
    }

    @PluginMethod
    public void wrapPackageKeyForRecoveryCode(PluginCall call) {
        String keyName = call.getString("keyName");
        String recoveryCode = call.getString("recoveryCode");
        if (!isValidKeyName(keyName, call) || !isValidRecoveryCode(recoveryCode, call)) {
            return;
        }

        byte[] packageKey = null;
        byte[] derivedKey = null;
        try {
            packageKey = loadPackageKey(keyName);
            byte[] salt = new byte[16];
            byte[] nonce = new byte[AesGcmNonceSizeBytes];
            SecureRandom random = new SecureRandom();
            random.nextBytes(salt);
            random.nextBytes(nonce);
            derivedKey = deriveRecoveryCodeKey(recoveryCode, salt);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, new SecretKeySpec(derivedKey, "AES"), new GCMParameterSpec(128, nonce));
            cipher.updateAAD(keyName.getBytes(StandardCharsets.UTF_8));

            JSObject result = new JSObject();
            result.put("ciphertext", android.util.Base64.encodeToString(cipher.doFinal(packageKey), android.util.Base64.NO_WRAP));
            result.put("nonce", android.util.Base64.encodeToString(nonce, android.util.Base64.NO_WRAP));
            result.put("salt", android.util.Base64.encodeToString(salt, android.util.Base64.NO_WRAP));
            result.put("algorithm", RecoveryCodeAlgorithm);
            result.put("keyVersionId", RecoveryCodeKeyVersion);
            call.resolve(result);
        } catch (GeneralSecurityException | IllegalArgumentException ex) {
            call.reject("Could not create the recovery-code envelope.", "RECOVERY_CODE_WRAP_FAILED", ex);
        } finally {
            if (packageKey != null) Arrays.fill(packageKey, (byte) 0);
            if (derivedKey != null) Arrays.fill(derivedKey, (byte) 0);
        }
    }

    @PluginMethod
    public void testRecoveryCodeEnvelope(PluginCall call) {
        String keyName = call.getString("keyName");
        String recoveryCode = call.getString("recoveryCode");
        JSObject envelope = call.getObject("envelope");
        if (!isValidKeyName(keyName, call) || !isValidRecoveryCode(recoveryCode, call) || envelope == null) {
            if (envelope == null) call.reject("Recovery-code envelope is required.", "RECOVERY_CODE_ENVELOPE_MISSING");
            return;
        }

        byte[] expected = null;
        byte[] recovered = null;
        try {
            expected = loadPackageKey(keyName);
            recovered = unwrapRecoveryCodeEnvelope(keyName, recoveryCode, envelope);
            JSObject result = new JSObject();
            result.put("confirmed", MessageDigest.isEqual(expected, recovered));
            call.resolve(result);
        } catch (GeneralSecurityException | IllegalArgumentException ex) {
            JSObject result = new JSObject();
            result.put("confirmed", false);
            call.resolve(result);
        } finally {
            if (expected != null) Arrays.fill(expected, (byte) 0);
            if (recovered != null) Arrays.fill(recovered, (byte) 0);
        }
    }

    @PluginMethod
    public void importRecoveryCodeEnvelope(PluginCall call) {
        String keyName = call.getString("keyName");
        String recoveryCode = call.getString("recoveryCode");
        JSObject envelope = call.getObject("envelope");
        if (!isValidKeyName(keyName, call) || !isValidRecoveryCode(recoveryCode, call) || envelope == null) {
            if (envelope == null) call.reject("Recovery-code envelope is required.", "RECOVERY_CODE_ENVELOPE_MISSING");
            return;
        }

        byte[] packageKey = null;
        try {
            packageKey = unwrapRecoveryCodeEnvelope(keyName, recoveryCode, envelope);
            storePackageKey(keyName, packageKey);
            JSObject result = new JSObject();
            result.put("imported", true);
            call.resolve(result);
        } catch (GeneralSecurityException | IllegalArgumentException ex) {
            call.reject("Recovery code is incorrect or unavailable.", "RECOVERY_CODE_INVALID", ex);
        } finally {
            if (packageKey != null) Arrays.fill(packageKey, (byte) 0);
        }
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

    private static byte[] unwrapRecoveryCodeEnvelope(String keyName, String recoveryCode, JSObject envelope)
        throws GeneralSecurityException {
        if (!RecoveryCodeAlgorithm.equals(envelope.getString("algorithm"))
            || !RecoveryCodeKeyVersion.equals(envelope.getString("keyVersionId"))) {
            throw new GeneralSecurityException("Recovery-code envelope format is unsupported.");
        }
        byte[] salt = android.util.Base64.decode(envelope.getString("salt"), android.util.Base64.NO_WRAP);
        byte[] nonce = android.util.Base64.decode(envelope.getString("nonce"), android.util.Base64.NO_WRAP);
        byte[] ciphertext = android.util.Base64.decode(envelope.getString("ciphertext"), android.util.Base64.NO_WRAP);
        if (salt.length != 16 || nonce.length != AesGcmNonceSizeBytes || ciphertext.length != PackageKeySizeBytes + AesGcmTagSizeBytes) {
            throw new GeneralSecurityException("Recovery-code envelope has an invalid length.");
        }
        byte[] derivedKey = deriveRecoveryCodeKey(recoveryCode, salt);
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, new SecretKeySpec(derivedKey, "AES"), new GCMParameterSpec(128, nonce));
            cipher.updateAAD(keyName.getBytes(StandardCharsets.UTF_8));
            byte[] packageKey = cipher.doFinal(ciphertext);
            if (packageKey.length != PackageKeySizeBytes) {
                Arrays.fill(packageKey, (byte) 0);
                throw new GeneralSecurityException("Recovered package key has the wrong length.");
            }
            return packageKey;
        } finally {
            Arrays.fill(derivedKey, (byte) 0);
        }
    }

    private static byte[] deriveRecoveryCodeKey(String recoveryCode, byte[] salt) throws GeneralSecurityException {
        char[] normalized = recoveryCode.trim().replace(" ", "").toCharArray();
        KeySpec spec = new PBEKeySpec(normalized, salt, RecoveryCodeIterations, 256);
        try {
            return SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).getEncoded();
        } finally {
            Arrays.fill(normalized, '\0');
            ((PBEKeySpec) spec).clearPassword();
        }
    }

    private static boolean isValidRecoveryCode(String recoveryCode, PluginCall call) {
        if (recoveryCode == null || recoveryCode.trim().replace(" ", "").length() < 32) {
            call.reject("Recovery code is invalid.", "RECOVERY_CODE_INVALID");
            return false;
        }
        return true;
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
            // Package keys are wrapped with a fresh SecureRandom nonce that is stored
            // beside the ciphertext. Android Keystore must therefore permit that
            // caller-supplied nonce for this dedicated wrapper key.
            .setRandomizedEncryptionRequired(false)
            .build());
        return generator.generateKey();
    }

    private PublicKey getOrCreateRecoveryPublicKey() throws GeneralSecurityException {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.VANILLA_ICE_CREAM) {
            throw new GeneralSecurityException(
                "Managed account recovery requires Android 15 or newer for RSA-OAEP SHA-256 Keystore support.");
        }

        KeyStore keyStore = loadAndroidKeyStore();
        java.security.cert.Certificate existing = keyStore.getCertificate(RecoveryKeyAlias);
        if (existing != null) {
            return existing.getPublicKey();
        }

        KeyPairGenerator generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_RSA, AndroidKeyStore);
        generator.initialize(new KeyGenParameterSpec.Builder(
            RecoveryKeyAlias,
            KeyProperties.PURPOSE_DECRYPT)
            .setDigests(KeyProperties.DIGEST_SHA256)
            .setMgf1Digests(KeyProperties.DIGEST_SHA256)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_RSA_OAEP)
            .setKeySize(2048)
            .build());
        return generator.generateKeyPair().getPublic();
    }

    private PrivateKey getRecoveryPrivateKey() throws GeneralSecurityException {
        KeyStore keyStore = loadAndroidKeyStore();
        if (keyStore.getCertificate(RecoveryKeyAlias) == null) {
            getOrCreateRecoveryPublicKey();
            keyStore = loadAndroidKeyStore();
        }

        PrivateKey privateKey = (PrivateKey) keyStore.getKey(RecoveryKeyAlias, null);
        if (privateKey == null) {
            throw new GeneralSecurityException("Android Keystore recovery key is unavailable.");
        }

        return privateKey;
    }

    private static KeyStore loadAndroidKeyStore() throws GeneralSecurityException {
        KeyStore keyStore = KeyStore.getInstance(AndroidKeyStore);
        try {
            keyStore.load(null);
        } catch (IOException | java.security.cert.CertificateException ex) {
            throw new GeneralSecurityException("Could not load Android Keystore.", ex);
        }
        return keyStore;
    }

    private static String toLowerHex(byte[] bytes) {
        StringBuilder result = new StringBuilder(bytes.length * 2);
        for (byte value : bytes) {
            result.append(String.format(java.util.Locale.ROOT, "%02x", value & 0xff));
        }
        return result.toString();
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

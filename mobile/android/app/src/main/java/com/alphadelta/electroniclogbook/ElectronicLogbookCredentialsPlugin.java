package com.alphadelta.electroniclogbook;

import android.os.CancellationSignal;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

@CapacitorPlugin(name = "ElectronicLogbookCredentials")
public class ElectronicLogbookCredentialsPlugin extends Plugin {
    @PluginMethod
    public void getGoogleIdToken(PluginCall call) {
        String webClientId = call.getString("webClientId");
        String nonce = call.getString("nonce");
        if (webClientId == null || webClientId.isBlank()) {
            call.reject("Google sign-in is missing the Web client ID.", "GOOGLE_CLIENT_ID_MISSING");
            return;
        }

        if (nonce == null || nonce.isBlank()) {
            call.reject("Google sign-in is missing its replay-protection nonce.", "GOOGLE_NONCE_MISSING");
            return;
        }

        GetSignInWithGoogleOption googleOption = new GetSignInWithGoogleOption.Builder(webClientId)
            .setNonce(nonce)
            .build();
        GetCredentialRequest request = new GetCredentialRequest.Builder()
            .addCredentialOption(googleOption)
            .build();
        CredentialManager manager = CredentialManager.create(getContext());
        manager.getCredentialAsync(
            getActivity(),
            request,
            new CancellationSignal(),
            getContext().getMainExecutor(),
            new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                @Override
                public void onResult(GetCredentialResponse response) {
                    resolveGoogleCredential(call, response.getCredential());
                }

                @Override
                public void onError(GetCredentialException exception) {
                    if (exception instanceof GetCredentialCancellationException) {
                        call.reject("Google sign-in was cancelled.", "GOOGLE_SIGN_IN_CANCELLED", exception);
                        return;
                    }

                    call.reject("Google sign-in could not obtain a credential.", "GOOGLE_CREDENTIAL_UNAVAILABLE", exception);
                }
            });
    }

    private static void resolveGoogleCredential(PluginCall call, Credential credential) {
        if (!(credential instanceof CustomCredential)
            || !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
            call.reject("Google sign-in returned an unsupported credential.", "GOOGLE_CREDENTIAL_INVALID");
            return;
        }

        try {
            GoogleIdTokenCredential googleCredential = GoogleIdTokenCredential.createFrom(
                ((CustomCredential) credential).getData());
            JSObject result = new JSObject();
            result.put("idToken", googleCredential.getIdToken());
            result.put("email", googleCredential.getId());
            call.resolve(result);
        } catch (RuntimeException exception) {
            call.reject("Google sign-in returned an invalid ID token.", "GOOGLE_TOKEN_INVALID", exception);
        }
    }
}

package game.auth;

import android.app.Activity;
import android.os.CancellationSignal;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import java.util.concurrent.Executors;

/**
 * Asks Android's Credential Manager for a Google ID token and hands it to the game (C#: GoogleSignIn.cs).
 * The token is sent to the account server, which checks it with Google. The callback runs on a background thread.
 */
public final class GoogleSignInBridge {
    public interface Callback {
        /** Exactly one of the two is not null. Errors: "cancelled", "failed". */
        void onResult(String idToken, String error);
    }

    private GoogleSignInBridge() {}

    public static void request(Activity activity, String webClientId, final Callback callback) {
        try {
            CredentialManager manager = CredentialManager.create(activity);
            GetGoogleIdOption option = new GetGoogleIdOption.Builder()
                    .setFilterByAuthorizedAccounts(false)
                    .setServerClientId(webClientId)
                    .build();
            GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
            manager.getCredentialAsync(activity, request, new CancellationSignal(), Executors.newSingleThreadExecutor(),
                    new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                        @Override
                        public void onResult(GetCredentialResponse response) {
                            Credential credential = response.getCredential();
                            if (credential instanceof CustomCredential
                                    && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                                try {
                                    callback.onResult(GoogleIdTokenCredential.createFrom(((CustomCredential) credential).getData()).getIdToken(), null);
                                } catch (RuntimeException e) {
                                    callback.onResult(null, "failed");
                                }
                            } else {
                                callback.onResult(null, "failed");
                            }
                        }

                        @Override
                        public void onError(GetCredentialException e) {
                            callback.onResult(null, e instanceof GetCredentialCancellationException ? "cancelled" : "failed");
                        }
                    });
        } catch (Throwable t) {
            callback.onResult(null, "failed");
        }
    }
}

using System;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Asks the phone for a Google ID token (the account the player chose in the system dialog). The game sends the token to the
/// account server, which checks it with Google. Needs the Web client ID of the Google Cloud project in
/// Resources/googleclient.txt (see server/ACCOUNTS.md); without it, and outside Android, the request fails with "unavailable".
/// The Android side is Plugins/Android/GoogleSignInBridge.java.
/// </summary>
public static class GoogleSignIn
{
    private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();

    public static string ClientId
    {
        get
        {
            TextAsset file = Resources.Load<TextAsset>("googleclient");
            return file != null ? file.text.Trim() : "";
        }
    }

    /// <summary>Calls back (on the main thread) with the ID token, or with null and "unavailable", "cancelled" or "failed".</summary>
    public static void Request(Action<string, string> done)
    {
        string clientId = ClientId;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (clientId.Length == 0) { done(null, "unavailable"); return; }
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass("game.auth.GoogleSignInBridge"))
            {
                bridge.CallStatic("request", activity, clientId, new BridgeCallback((token, error) => Pending.Enqueue(() => done(token, error))));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GoogleSignIn] " + e.Message);
            done(null, "unavailable");
        }
#else
        done(null, "unavailable");
#endif
    }

    /// <summary>Runs results that arrived on the Android callback thread. Called every frame by <see cref="AccountService"/>.</summary>
    public static void Pump()
    {
        while (Pending.TryDequeue(out Action action)) action();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private sealed class BridgeCallback : AndroidJavaProxy
    {
        private readonly Action<string, string> _done;

        public BridgeCallback(Action<string, string> done) : base("game.auth.GoogleSignInBridge$Callback") => _done = done;

        // Called by Java, on a background thread.
        public void onResult(string idToken, string error) => _done(idToken, error);
    }
#endif
}

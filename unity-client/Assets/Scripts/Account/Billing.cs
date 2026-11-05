using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Google Play Billing, seen from C#. Everything is asynchronous and the callbacks run on the main thread (<see cref="Pump"/>).
/// The Android side is Plugins/Android/BillingBridge.java. In development builds without Play (the editor, a phone without the store set up)
/// a fake "dev-..." purchase is used instead, which only a development account server (Account__Store__AllowDevPurchases) accepts.
/// </summary>
public static class Billing
{
    public static readonly string[] ProductIds = { "credits_500", "credits_1500", "credits_4000", "booster_credits_x2_7d" };

    private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();
    private static readonly Dictionary<string, string> Prices = new Dictionary<string, string>();

    public static bool Ready { get; private set; }
    /// <summary>The last problem Play reported (empty when fine), for the store page.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>A real Play purchase, or the development stand-in.</summary>
    public static bool Available => DevMode || (Ready && Prices.Count > 0);

    public static bool DevMode =>
#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.isDebugBuild && Prices.Count == 0; // Play answered with no products (the app is not in the Play Console yet)
#else
        Debug.isDebugBuild;
#endif

    /// <summary>The localized price Google gave ("€1.99"), or "" when not known.</summary>
    public static string PriceOf(string productId) => Prices.TryGetValue(productId, out string price) ? price : DevMode ? "dev" : "";

    /// <summary>A paid purchase arrived (bought now, or found unfinished at start): productId, token.</summary>
    public static event Action<string, string> Purchased;
    public static event Action Cancelled;
    public static event Action Changed;

    private static void DevBuy(string productId)
    {
        if (!DevMode) { LastError = "unavailable"; Pending.Enqueue(() => Cancelled?.Invoke()); return; }
        string token = "dev-" + Guid.NewGuid().ToString("N");
        Pending.Enqueue(() => Purchased?.Invoke(productId, token));
    }

    public static void Pump()
    {
        while (Pending.TryDequeue(out Action action)) action();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaClass _bridge;
    private static bool _started;

    public static void Init()
    {
        if (_started) return;
        _started = true;
        try
        {
            _bridge = new AndroidJavaClass("game.store.BillingBridge");
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                _bridge.CallStatic("init", activity, new Listener());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Billing] " + e.Message);
            _started = false;
            LastError = "unavailable";
        }
    }

    private static void Call(string method, params object[] args)
    {
        try { _bridge?.CallStatic(method, args); }
        catch (Exception e) { Debug.LogWarning("[Billing] " + method + ": " + e.Message); }
    }

    public static void Buy(string productId)
    {
        if (DevMode) { DevBuy(productId); return; }
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            Call("buy", activity, productId);
    }

    public static void QueryOwned() => Call("queryOwned");
    public static void Consume(string token) => Call("consume", token);

    private sealed class Listener : AndroidJavaProxy
    {
        public Listener() : base("game.store.BillingBridge$Listener") { }

        // Called by Java, on a background thread.
        public void onEvent(string type, string a, string b) => Pending.Enqueue(() => Handle(type, a, b));
    }

    private static void Handle(string type, string a, string b)
    {
        switch (type)
        {
            case "ready":
                Ready = true;
                LastError = "";
                Call("queryProducts", new[] { ProductIds[0], ProductIds[1], ProductIds[2], ProductIds[3] });
                QueryOwned();
                break;
            case "products":
                Prices.Clear();
                foreach (string entry in a.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = entry.Split('|');
                    if (parts.Length == 2) Prices[parts[0]] = parts[1];
                }
                break;
            case "purchase": Purchased?.Invoke(a, b); break;
            case "owned":
                foreach (string entry in a.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = entry.Split('|');
                    if (parts.Length == 2) Purchased?.Invoke(parts[0], parts[1]);
                }
                break;
            case "cancelled": Cancelled?.Invoke(); break;
            case "pending": LastError = "pending"; break;
            case "error": LastError = a; Debug.LogWarning("[Billing] " + a); Cancelled?.Invoke(); break;
        }
        Changed?.Invoke();
    }
#else
    public static void Init() { }

    public static void Buy(string productId) => DevBuy(productId);

    public static void QueryOwned() { }
    public static void Consume(string token) { }
#endif
}

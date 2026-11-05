using UnityEngine;

/// <summary>The store screen: credit packs and the double-credits booster, bought through Google Play (see DemoGame.Store.cs).</summary>
public sealed partial class DemoHud
{
    private void OpenStorePage()
    {
        _page = MenuPage.Store;
        _game.RefreshStore();
    }

    private void DrawStore()
    {
        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.08f) };
        GUI.Label(new Rect(0f, Screen.height * 0.03f, Screen.width, Screen.height * 0.12f), Strings.Get("store.title"), title);
        var sub = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0f, Screen.height * 0.14f, Screen.width, Screen.height * 0.06f), Strings.Get("armory.credits", _game.Progress.Credits), sub);

        float w = Screen.width * 0.7f, h = Screen.height * 0.13f, gap = Screen.height * 0.018f;
        float x = (Screen.width - w) / 2f, y = Screen.height * 0.21f;
        var name = new GUIStyle(_stats) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(Screen.height * 0.05f) };
        var line = new GUIStyle(_label) { alignment = TextAnchor.MiddleLeft };
        float pad = w * 0.04f, bw = w * 0.22f;

        for (int i = 0; i < Billing.ProductIds.Length; i++)
        {
            string id = Billing.ProductIds[i];
            float ty = y + i * (h + gap);
            GUI.Box(new Rect(x, ty, w, h), "");
            GUI.Label(new Rect(x + pad, ty + h * 0.06f, w - bw - pad * 3f, h * 0.5f), Strings.Get("store.product." + id), name);
            string detail = Strings.Get("store.detail." + id);
            if (id == "booster_credits_x2_7d" && _game.DoubleCreditsActive)
                detail = Strings.Get("store.boosterActive", (_game.DoubleCreditsUntil.ToLocalTime()).ToString("yyyy-MM-dd HH:mm"));
            GUI.Label(new Rect(x + pad, ty + h * 0.56f, w - bw - pad * 3f, h * 0.36f), detail, line);
            string price = Billing.PriceOf(id);
            GUI.enabled = !_game.StoreBusy && price.Length > 0;
            if (GUI.Button(new Rect(x + w - bw - pad, ty + h * 0.2f, bw, h * 0.6f), price.Length > 0 ? price : "-", _button)) _game.BuyProduct(id);
            GUI.enabled = true;
        }

        var message = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.034f) };
        string text = _game.StoreMessageKey.Length > 0 ? Strings.Get(_game.StoreMessageKey) : Strings.Get(Billing.Available ? "store.hint" : "store.unavailable");
        GUI.Label(new Rect(Screen.width * 0.15f, Screen.height * 0.88f, Screen.width * 0.7f, Screen.height * 0.1f), text, message);

        var back = new Rect(Screen.width * 0.04f, Screen.height - Screen.height * 0.14f, Screen.width * 0.1f, Screen.height * 0.1f);
        if (GUI.Button(back, Strings.Get("menu.back"), _button)) _page = MenuPage.Main;
    }
}

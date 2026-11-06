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
        DrawPageTitle(Strings.Get("store.title"));
        DrawPageChips(null, _game.Progress.Credits.ToString());

        float w = Screen.width * 0.76f, h = Screen.height * 0.15f, gap = Screen.height * 0.022f;
        float x = (Screen.width - w) / 2f, y = Screen.height * 0.17f;
        float pad = h * 0.3f, bw = w * 0.22f;

        for (int i = 0; i < Billing.ProductIds.Length; i++)
        {
            string id = Billing.ProductIds[i];
            bool booster = id == "booster_credits_x2_7d";
            bool active = booster && _game.DoubleCreditsActive;
            var row = new Rect(x, y + i * (h + gap), w, h);
            StripePlate(row, active ? UiTheme.Good : booster ? UiTheme.Warn : UiTheme.Accent);

            float icon = h * 0.52f;
            GUI.DrawTexture(new Rect(row.x + pad, row.y + (h - icon) / 2f, icon, icon), UiTheme.IconTexture(booster ? UiTheme.Icon.Star : UiTheme.Icon.Resources));
            float tx = row.x + pad * 1.4f + icon, tw = row.width - bw - icon - pad * 3f;
            string detail = Strings.Get("store.detail." + id);
            if (active) detail = Strings.Get("store.boosterActive", _game.DoubleCreditsUntil.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            PageText(new Rect(tx, row.y + h * 0.1f, tw, h * 0.42f), Strings.Get("store.product." + id), 0.048f, UiTheme.Bone);
            PageText(new Rect(tx, row.y + h * 0.54f, tw, h * 0.36f), detail, 0.032f, active ? UiTheme.Good : UiTheme.Dim);

            string price = Billing.PriceOf(id);
            GUI.enabled = !_game.StoreBusy && price.Length > 0;
            if (IconButton(new Rect(row.xMax - bw - pad * 0.6f, row.y + h * 0.2f, bw, h * 0.6f), null, price.Length > 0 ? price : "-", GUI.enabled)) _game.BuyProduct(id);
            GUI.enabled = true;
        }

        string text = _game.StoreMessageKey.Length > 0 ? Strings.Get(_game.StoreMessageKey) : Strings.Get(Billing.Available ? "store.hint" : "store.unavailable");
        PageHint(text);
        DrawBackButton(0.1f);
    }
}

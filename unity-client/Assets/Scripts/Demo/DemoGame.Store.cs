using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The store side of the game: buying credits and boosters through Google Play, the account server checking each purchase,
/// and this phone collecting what the server lists as bought. Nothing is ever lost on the way: a purchase stays unfinished in Play until
/// the server has recorded it, and the server keeps the credits as "pending" until this phone confirms it added them.
/// </summary>
public sealed partial class DemoGame
{
    private const string BoosterKey = "store.doubleCreditsUntil";

    /// <summary>Text key of what the store page should tell the player ("store.thanks", "store.offline"...), empty for nothing.</summary>
    public string StoreMessageKey { get; private set; } = "";
    public bool StoreBusy { get; private set; }

    private DateTime _doubleCreditsUntil = DateTime.MinValue;
    private bool _storeStarted;

    /// <summary>While this is on, campaign missions pay double.</summary>
    public bool DoubleCreditsActive => _doubleCreditsUntil > GameClock.UtcNow;
    public DateTime DoubleCreditsUntil => _doubleCreditsUntil;

    private void InitStore()
    {
        if (_storeStarted) return;
        _storeStarted = true;
        try
        {
            long ticks = long.Parse(PlayerPrefs.GetString(BoosterKey, "0"));
            _doubleCreditsUntil = new DateTime(ticks, DateTimeKind.Utc);
        }
        catch (Exception) { }
        Billing.Purchased += OnPurchased;
        Billing.Cancelled += () => { StoreBusy = false; if (StoreMessageKey == "store.working") StoreMessageKey = ""; };
        Account.Changed += () => { if (Account.SignedIn) RefreshStore(); };
        Billing.Init();
        if (Account.SignedIn) RefreshStore();
    }

    /// <summary>Asks the server what was bought and not collected yet (and which boosters run), and finishes any purchase Play still holds.</summary>
    public void RefreshStore()
    {
        if (Account == null || !Account.SignedIn) return;
        Account.StoreState(r => { if (r.Ok) ApplyStoreState(r.Read<StoreStateBody>()); });
        Billing.QueryOwned();
    }

    /// <summary>Starts a purchase. The result arrives through <see cref="OnPurchased"/>.</summary>
    public void BuyProduct(string productId)
    {
        if (StoreBusy) return;
        if (Account == null || !Account.SignedIn) { StoreMessageKey = "store.signInFirst"; return; }
        if (!Billing.Available) { StoreMessageKey = "store.unavailable"; return; }
        StoreBusy = true;
        StoreMessageKey = "store.working";
        Billing.Buy(productId);
    }

    private void OnPurchased(string productId, string token)
    {
        if (Account == null || !Account.SignedIn)
        {
            StoreBusy = false;
            StoreMessageKey = "store.signInFirst"; // Play keeps the purchase; it is handed to the server after signing in
            return;
        }
        StoreBusy = true;
        StoreMessageKey = "store.working";
        Account.StorePurchase(productId, token, r =>
        {
            StoreBusy = false;
            if (r.Ok)
            {
                Billing.Consume(token);
                StoreStateBody state = r.Read<StoreStateBody>();
                ApplyStoreState(state);
                StoreMessageKey = "store.thanks";
            }
            else if (r.NetworkError || r.Status >= 500) StoreMessageKey = "store.later";     // paid, not recorded yet: it is tried again at the next start
            else if (r.Error == "purchase_pending" || r.Status == 202) StoreMessageKey = "store.pending";
            else StoreMessageKey = "store.failed";
        });
    }

    private string CollectedKey() => "store.collected." + (Account.User != null ? Account.User.id : 0);

    private void ApplyStoreState(StoreStateBody state)
    {
        if (state == null) return;
        if (state.boosters != null)
        {
            foreach (BoosterInfo b in state.boosters)
            {
                if (b.kind == "credits_x2" && DateTimeOffset.TryParse(b.expiresAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset until)
                    && until.UtcDateTime > _doubleCreditsUntil)
                    _doubleCreditsUntil = until.UtcDateTime;
            }
            try { PlayerPrefs.SetString(BoosterKey, _doubleCreditsUntil.Ticks.ToString()); PlayerPrefs.Save(); } catch (Exception) { }
        }
        if (state.pending == null || state.pending.Length == 0) return;

        // Add each purchase once. The ids already added are written down before the server is told (a crash in between only repeats the ack).
        var done = new HashSet<string>();
        try { foreach (string id in PlayerPrefs.GetString(CollectedKey(), "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) done.Add(id); } catch (Exception) { }
        var ack = new List<long>();
        bool added = false;
        foreach (PendingGrant grant in state.pending)
        {
            if (done.Add(grant.id.ToString()))
            {
                Progress.AddPurchased(grant.credits);
                added = true;
            }
            ack.Add(grant.id);
        }
        if (added)
        {
            SaveProgress();
            try { PlayerPrefs.SetString(CollectedKey(), string.Join(",", done)); PlayerPrefs.Save(); } catch (Exception) { }
            Sfx.Play("place", 0.8f, 0f);
        }
        Account.StoreAck(ack.ToArray(), _ => { });
    }
}

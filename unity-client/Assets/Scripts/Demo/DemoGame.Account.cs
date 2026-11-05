using System;
using SimCore;
using UnityEngine;

/// <summary>The account side of the game: keeping the progress on this phone and the progress on the server in step.</summary>
public sealed partial class DemoGame
{
    public AccountService Account { get; private set; }

    /// <summary>Progress changed since the last successful upload.</summary>
    public bool ProfileDirty { get; private set; }
    public bool Syncing { get; private set; }
    /// <summary>Text key of the result of the last sync ("account.synced", "account.syncFailed"), empty before the first one.</summary>
    public string SyncStatusKey { get; private set; } = "";

    private float _lastSyncTime = -100f;
    private const int MaxSyncAttempts = 3;
    private const float SyncIntervalSeconds = 8f;

    private ProfileData CurrentProfile() => new ProfileData
    {
        Campaign = Progress.Serialize(),
        Armory = Armory.Serialize(),
        Daily = Daily.Serialize(),
        Tutorial = _tutorialDone ? "1" : "",
        Stats = Stats.Serialize(),
    };

    private static ProfileData FromDto(ProfileDto dto) => dto == null ? new ProfileData() : new ProfileData
    {
        Campaign = dto.campaign ?? "", Armory = dto.armory ?? "", Daily = dto.daily ?? "", Tutorial = dto.tutorial ?? "", Stats = dto.stats ?? "",
    };

    private static ProfileDto ToDto(ProfileData data) => new ProfileDto { campaign = data.Campaign, armory = data.Armory, daily = data.Daily, tutorial = data.Tutorial, stats = data.Stats };

    private void ApplyProfile(ProfileData data)
    {
        Progress = CampaignProgress.Parse(data.Campaign);
        Armory = Armory.Parse(data.Armory);
        Daily = DailyProgress.Parse(data.Daily);
        Stats = PlayerStats.Parse(data.Stats);
        _tutorialDone = data.Tutorial == "1";
        SaveProgress();
        try { PlayerPrefs.SetInt(TutorialKey, _tutorialDone ? 1 : 0); PlayerPrefs.Save(); } catch (Exception) { }
    }

    // Pushes changes to the account in the background, at the main menu, never in the middle of a match.
    private void TickProfileSync()
    {
        if (Account == null || !Account.SignedIn || Syncing || !ProfileDirty || Mode != GameMode.Menu) return;
        if (Time.unscaledTime - _lastSyncTime < SyncIntervalSeconds) return;
        SyncProfile();
    }

    /// <summary>Joins this phone's progress with the account's (nothing is lost on either side) and uploads the result.</summary>
    public void SyncProfile(Action<bool> done = null)
    {
        if (Account == null || !Account.SignedIn || Syncing) { done?.Invoke(false); return; }
        Syncing = true;
        SyncAttempt(1, done);
    }

    private void SyncAttempt(int attempt, Action<bool> done)
    {
        Account.GetProfile(get =>
        {
            ProfileBody server = get.Ok ? get.Read<ProfileBody>() : null;
            if (server == null) { EndSync(false, done); return; }

            ProfileData onServer = ProfileMerge.Merge(FromDto(server.data), new ProfileData()); // the server's text in the same normal form
            ProfileData merged = ProfileMerge.Merge(CurrentProfile(), onServer);
            if (!merged.SameAs(CurrentProfile())) ApplyProfile(merged);
            if (merged.SameAs(onServer)) { EndSync(true, done); return; }

            Account.PutProfile(server.revision, ToDto(merged), put =>
            {
                if (put.Ok) EndSync(true, done);
                else if (put.Status == 409 && attempt < MaxSyncAttempts) SyncAttempt(attempt + 1, done); // another device wrote in between
                else EndSync(false, done);
            });
        });
    }

    private void EndSync(bool ok, Action<bool> done)
    {
        Syncing = false;
        _lastSyncTime = Time.unscaledTime;
        ProfileDirty = !ok;
        SyncStatusKey = ok ? "account.synced" : "account.syncFailed";
        done?.Invoke(ok);
    }

    /// <summary>Saves the latest progress to the account, ends the session on the server and clears this phone, so the next person starts fresh.</summary>
    public void SignOut(Action done)
    {
        void Finish() => Account.Logout(_ => { ResetLocalProgress(); done?.Invoke(); });
        if (ProfileDirty && !Syncing) SyncProfile(_ => Finish());
        else Finish();
    }

    /// <summary>The account was deleted: this phone starts fresh too.</summary>
    public void AfterAccountDeleted() => ResetLocalProgress();

    private void ResetLocalProgress()
    {
        ApplyProfile(new ProfileData());
        ProfileDirty = false;
        SyncStatusKey = "";
    }
}

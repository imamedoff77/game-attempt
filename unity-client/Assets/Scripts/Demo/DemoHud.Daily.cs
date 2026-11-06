using SimCore;
using UnityEngine;

/// <summary>The daily screen: the login reward with its streak, and three daily tasks.</summary>
public sealed partial class DemoHud
{
    private void DrawDaily()
    {
        DailyProgress daily = _game.Daily;
        int day = DemoGame.Today;

        DrawPageTitle(Strings.Get("daily.title"));
        DrawPageChips(null, _game.Progress.Credits.ToString());

        float w = Screen.width * 0.76f, h = Screen.height * 0.15f, gap = Screen.height * 0.022f;
        float x = (Screen.width - w) / 2f, y = Screen.height * 0.17f;
        float pad = h * 0.3f, bw = w * 0.2f;

        // login reward
        bool canClaimLogin = daily.CanClaimReward(day);
        int streak = canClaimLogin ? daily.StreakIfClaimed(day) : daily.Streak;
        var card = new Rect(x, y, w, h);
        StripePlate(card, canClaimLogin ? UiTheme.Accent : UiTheme.Good);
        DailyRowText(card, pad, bw, Strings.Get("daily.login", daily.RewardFor(streak)), Strings.Get("daily.streak", streak), -1f);
        DrawClaimButton(new Rect(card.xMax - bw - pad * 0.6f, card.y + h * 0.2f, bw, h * 0.6f), canClaimLogin, _game.ClaimDailyReward, !canClaimLogin);

        // tasks
        DailyTask[] tasks = DailyProgress.TasksFor(day);
        for (int i = 0; i < tasks.Length; i++)
        {
            var row = new Rect(x, y + (i + 1) * (h + gap), w, h);
            int done = daily.Progress(day, i);
            bool claimed = daily.IsClaimed(day, i), ready = daily.CanClaimTask(day, i);
            StripePlate(row, claimed ? UiTheme.Good : ready ? UiTheme.Accent : UiTheme.Edge);
            DailyRowText(row, pad, bw, Strings.DailyTaskText(tasks[i]), Strings.Get("daily.progress", done, tasks[i].Target), (float)done / tasks[i].Target);
            UiTheme.IconLabel(new Rect(row.xMax - bw * 1.95f - pad * 0.6f, row.y + h * 0.34f, bw * 0.85f, h * 0.32f), UiTheme.Icon.Resources, "+" + tasks[i].Reward, UiTheme.Bone);
            int index = i;
            DrawClaimButton(new Rect(row.xMax - bw - pad * 0.6f, row.y + h * 0.2f, bw, h * 0.6f), ready, () => _game.ClaimDailyTask(index), claimed);
        }

        PageHint(Strings.Get("daily.hint"));
        DrawBackButton(0.1f);
    }

    // Title and a second line (with an optional progress bar) on the left of a daily row.
    private void DailyRowText(Rect row, float pad, float bw, string title, string second, float barFraction)
    {
        float tw = row.width - bw * 2.3f - pad * 1.5f;
        PageText(new Rect(row.x + pad, row.y + row.height * 0.1f, tw, row.height * 0.4f), title, 0.045f, UiTheme.Bone);
        if (barFraction >= 0f)
        {
            PageBar(new Rect(row.x + pad, row.y + row.height * 0.62f, tw * 0.62f, row.height * 0.16f), barFraction, UiTheme.Accent);
            PageText(new Rect(row.x + pad + tw * 0.66f, row.y + row.height * 0.52f, tw * 0.34f, row.height * 0.36f), second, 0.032f, UiTheme.Dim);
        }
        else PageText(new Rect(row.x + pad, row.y + row.height * 0.52f, tw, row.height * 0.36f), second, 0.034f, UiTheme.Dim);
    }

    private void DrawClaimButton(Rect r, bool canClaim, System.Action claim, bool alreadyClaimed = false)
    {
        GUI.enabled = canClaim;
        if (IconButton(r, alreadyClaimed ? UiTheme.Icon.Flag : (UiTheme.Icon?)null, Strings.Get(alreadyClaimed ? "daily.claimed" : "daily.claim"), canClaim)) claim();
        GUI.enabled = true;
    }
}

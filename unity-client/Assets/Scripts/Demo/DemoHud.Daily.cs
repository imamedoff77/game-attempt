using SimCore;
using UnityEngine;

/// <summary>The daily screen: the login reward with its streak, and three daily tasks.</summary>
public sealed partial class DemoHud
{
    private void DrawDaily()
    {
        DailyProgress daily = _game.Daily;
        int day = DemoGame.Today;

        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.08f) };
        GUI.Label(new Rect(0f, Screen.height * 0.03f, Screen.width, Screen.height * 0.12f), Strings.Get("daily.title"), title);
        var sub = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0f, Screen.height * 0.14f, Screen.width, Screen.height * 0.06f), Strings.Get("armory.credits", _game.Progress.Credits), sub);

        float w = Screen.width * 0.7f, h = Screen.height * 0.14f, gap = Screen.height * 0.02f;
        float x = (Screen.width - w) / 2f, y = Screen.height * 0.22f;
        var name = new GUIStyle(_stats) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(Screen.height * 0.05f) };
        var line = new GUIStyle(_label) { alignment = TextAnchor.MiddleLeft };
        float pad = w * 0.04f, bw = w * 0.22f;

        // login reward
        var card = new Rect(x, y, w, h);
        GUI.Box(card, "");
        bool canClaimLogin = daily.CanClaimReward(day);
        int streak = canClaimLogin ? daily.StreakIfClaimed(day) : daily.Streak;
        GUI.Label(new Rect(x + pad, y + h * 0.06f, w - bw - pad * 3f, h * 0.5f), Strings.Get("daily.login", daily.RewardFor(streak)), name);
        GUI.Label(new Rect(x + pad, y + h * 0.56f, w - bw - pad * 3f, h * 0.36f), Strings.Get("daily.streak", streak), line);
        DrawClaimButton(new Rect(x + w - bw - pad, y + h * 0.2f, bw, h * 0.6f), canClaimLogin, _game.ClaimDailyReward, !canClaimLogin);

        // tasks
        DailyTask[] tasks = DailyProgress.TasksFor(day);
        for (int i = 0; i < tasks.Length; i++)
        {
            float ty = y + (i + 1) * (h + gap);
            GUI.Box(new Rect(x, ty, w, h), "");
            GUI.Label(new Rect(x + pad, ty + h * 0.06f, w - bw - pad * 3f, h * 0.5f), Strings.DailyTaskText(tasks[i]), name);
            string progress = Strings.Get("daily.progress", daily.Progress(day, i), tasks[i].Target) + "   +" + tasks[i].Reward;
            GUI.Label(new Rect(x + pad, ty + h * 0.56f, w - bw - pad * 3f, h * 0.36f), progress, line);
            int index = i;
            DrawClaimButton(new Rect(x + w - bw - pad, ty + h * 0.2f, bw, h * 0.6f), daily.CanClaimTask(day, i), () => _game.ClaimDailyTask(index), daily.IsClaimed(day, i));
        }

        var hint = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.028f) };
        GUI.Label(new Rect(Screen.width * 0.22f, Screen.height * 0.9f, Screen.width * 0.56f, Screen.height * 0.07f), Strings.Get("daily.hint"), hint);

        var back = new Rect(Screen.width * 0.04f, Screen.height - Screen.height * 0.14f, Screen.width * 0.1f, Screen.height * 0.1f);
        if (GUI.Button(back, Strings.Get("menu.back"), _button)) _page = MenuPage.Main;
    }

    private void DrawClaimButton(Rect r, bool canClaim, System.Action claim, bool alreadyClaimed = false)
    {
        GUI.enabled = canClaim;
        if (GUI.Button(r, Strings.Get(alreadyClaimed ? "daily.claimed" : "daily.claim"), _button)) claim();
        GUI.enabled = true;
    }
}

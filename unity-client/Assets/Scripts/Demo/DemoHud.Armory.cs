using SimCore;
using UnityEngine;

/// <summary>The armory screen: spend campaign credits on permanent unit upgrades.</summary>
public sealed partial class DemoHud
{
    private static readonly UnitKind[] ArmoryOrder =
    {
        UnitKind.Rifleman, UnitKind.RocketTrooper, UnitKind.Sniper,
        UnitKind.Jeep, UnitKind.ArmoredCar, UnitKind.Tank,
    };

    private void DrawArmory()
    {
        Armory armory = _game.Armory;
        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.08f) };
        GUI.Label(new Rect(0f, Screen.height * 0.03f, Screen.width, Screen.height * 0.12f), Strings.Get("armory.title"), title);
        var sub = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0f, Screen.height * 0.14f, Screen.width, Screen.height * 0.06f), Strings.Get("armory.credits", _game.Progress.Credits), sub);

        const int columns = 3;
        float gap = Screen.height * 0.03f;
        float w = (Screen.width * 0.86f - gap * (columns - 1)) / columns, h = Screen.height * 0.29f;
        float x0 = (Screen.width - (w * columns + gap * (columns - 1))) / 2f, y0 = Screen.height * 0.21f;

        var name = new GUIStyle(_stats) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.045f) };
        var line = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        for (int i = 0; i < ArmoryOrder.Length; i++)
        {
            UnitKind kind = ArmoryOrder[i];
            var r = new Rect(x0 + (i % columns) * (w + gap), y0 + (i / columns) * (h + gap), w, h);
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x, r.y + h * 0.04f, w, h * 0.2f), Strings.UnitName(kind), name);
            GUI.Label(new Rect(r.x, r.y + h * 0.26f, w, h * 0.16f), Strings.Get("armory.level", armory.Level(kind), Armory.MaxLevel), line);
            GUI.Label(new Rect(r.x, r.y + h * 0.42f, w, h * 0.16f), Strings.Get("armory.bonus", armory.BonusMilli(kind) / 10), line);

            var buy = new Rect(r.x + w * 0.1f, r.y + h * 0.64f, w * 0.8f, h * 0.28f);
            int cost = armory.NextCost(kind);
            if (cost == 0)
            {
                GUI.enabled = false;
                GUI.Button(buy, Strings.Get("armory.max"), _button);
                GUI.enabled = true;
            }
            else
            {
                GUI.enabled = armory.CanBuy(kind, _game.Progress);
                if (GUI.Button(buy, Strings.Get("armory.buy", cost), _button)) _game.BuyArmoryLevel(kind);
                GUI.enabled = true;
            }
        }

        var hint = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.028f) };
        GUI.Label(new Rect(Screen.width * 0.22f, Screen.height * 0.86f, Screen.width * 0.56f, Screen.height * 0.12f), Strings.Get("armory.hint"), hint);

        var back = new Rect(Screen.width * 0.04f, Screen.height - Screen.height * 0.14f, Screen.width * 0.16f, Screen.height * 0.1f);
        if (GUI.Button(back, Strings.Get("menu.back"), _button)) _page = MenuPage.Main;
    }
}

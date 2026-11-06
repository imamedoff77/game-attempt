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
        DrawPageTitle(Strings.Get("armory.title"));
        DrawPageChips(null, _game.Progress.Credits.ToString());

        const int columns = 3;
        float gap = Screen.height * 0.03f;
        float w = (Screen.width * 0.86f - gap * (columns - 1)) / columns, h = Screen.height * 0.325f;
        float x0 = (Screen.width - (w * columns + gap * (columns - 1))) / 2f, y0 = Screen.height * 0.17f;

        for (int i = 0; i < ArmoryOrder.Length; i++)
        {
            UnitKind kind = ArmoryOrder[i];
            var r = new Rect(x0 + (i % columns) * (w + gap), y0 + (i / columns) * (h + gap), w, h);
            int level = armory.Level(kind);
            GUI.Box(r, "");
            Color old = GUI.color;
            GUI.color = level >= Armory.MaxLevel ? UiTheme.Good : level > 0 ? UiTheme.Accent : UiTheme.Edge;
            GUI.DrawTexture(new Rect(r.x + h * 0.08f, r.y + 3f, r.width - h * 0.16f, Mathf.Max(4f, h * 0.018f)), Texture2D.whiteTexture);
            GUI.color = old;

            PageText(new Rect(r.x + w * 0.06f, r.y + h * 0.06f, w * 0.88f, h * 0.2f), Strings.UnitName(kind), 0.05f, UiTheme.Bone);

            // level pips
            float pip = h * 0.07f, pg = pip * 0.45f;
            for (int p = 0; p < Armory.MaxLevel; p++)
            {
                GUI.color = p < level ? UiTheme.Accent : new Color(0.04f, 0.05f, 0.03f, 0.9f);
                GUI.DrawTexture(new Rect(r.x + w * 0.06f + p * (pip * 1.9f + pg), r.y + h * 0.31f, pip * 1.9f, pip), Texture2D.whiteTexture);
            }
            GUI.color = old;
            PageText(new Rect(r.x + w * 0.06f, r.y + h * 0.4f, w * 0.88f, h * 0.15f), Strings.Get("armory.level", level, Armory.MaxLevel), 0.032f, UiTheme.Dim);
            PageText(new Rect(r.x + w * 0.06f, r.y + h * 0.53f, w * 0.88f, h * 0.15f), Strings.Get("armory.bonus", armory.BonusMilli(kind) / 10), 0.032f, UiTheme.Good);

            var buy = new Rect(r.x + w * 0.08f, r.y + h * 0.7f, w * 0.84f, h * 0.22f);
            int cost = armory.NextCost(kind);
            if (cost == 0)
            {
                GUI.enabled = false;
                IconButton(buy, null, Strings.Get("armory.max"));
                GUI.enabled = true;
            }
            else
            {
                bool can = armory.CanBuy(kind, _game.Progress);
                GUI.enabled = can;
                if (IconButton(buy, UiTheme.Icon.Resources, cost.ToString(), can)) _game.BuyArmoryLevel(kind);
                GUI.enabled = true;
            }
        }

        PageHint(Strings.Get("armory.hint"));
        DrawBackButton(0.12f);
    }
}

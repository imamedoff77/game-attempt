using UnityEngine;

/// <summary>The hint banner of the first mission: tells a new player what to do next.</summary>
public sealed partial class DemoHud
{
    private void DrawTutorial()
    {
        float pad = Screen.height * 0.02f;
        Rect mini = MinimapRect(); // the banner sits to the right of the minimap
        var rect = new Rect(mini.xMax + pad * 1.5f, Screen.height * 0.66f, Screen.width * 0.27f, Screen.height * 0.22f);
        GUI.Box(rect, "");
        var style = new GUIStyle(_label) { alignment = TextAnchor.MiddleLeft, wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.034f) };
        GUI.Label(new Rect(rect.x + pad, rect.y, rect.width - pad * 2f, rect.height), Strings.TutorialText(_game.TutorialStep), style);

        var skip = new Rect(rect.x, rect.yMax + pad * 0.4f, Screen.width * 0.16f, Screen.height * 0.075f);
        if (GUI.Button(skip, Strings.Get("tutorial.skip"), _button)) _game.FinishTutorial();
    }
}

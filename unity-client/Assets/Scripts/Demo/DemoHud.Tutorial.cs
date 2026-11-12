using UnityEngine;

/// <summary>The hint banner of the first mission: tells a new player what to do next.</summary>
public sealed partial class DemoHud
{
    private void DrawTutorial()
    {
        float pad = Screen.height * 0.02f;
        Rect mini = MinimapRect(); // the banner sits to the right of the minimap
        var rect = new Rect(mini.xMax + pad * 1.5f, Screen.height * 0.6f, Screen.width * 0.27f, Screen.height * 0.28f);
        GUI.Box(rect, "");
        var style = new GUIStyle(_label) { alignment = TextAnchor.MiddleLeft, wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.034f) };
        GUI.Label(new Rect(rect.x + pad, rect.y, rect.width - pad * 2f, rect.height), Strings.TutorialText(_game.TutorialStep), style);

        var skip = new Rect(rect.xMax - Screen.width * 0.14f - pad * 0.5f, rect.yMax - Screen.height * 0.07f - pad * 0.5f, Screen.width * 0.14f, Screen.height * 0.07f);
        if (GUI.Button(skip, Strings.Get("tutorial.skip"), _button)) _game.FinishTutorial();
    }
}

using UnityEngine;
using UnityEngine.UIElements;

// One UI Toolkit document for every scene, created at startup and kept across scene loads (exhibition matches
// reload the scene), so no scene needs a UIDocument of its own.
public static class UIRoot
{
    public static VisualElement Root { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/Theme");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        settings.match = 0.5f;

        // Inactive while it is set up: the document attaches to its panel when it is enabled.
        var host = new GameObject("UI");
        host.SetActive(false);
        Object.DontDestroyOnLoad(host);
        var document = host.AddComponent<UIDocument>();
        document.panelSettings = settings;
        host.SetActive(true);

        Root = document.rootVisualElement;
        Root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Game"));
        Root.AddToClassList("game-root");
        // Clicks that miss every panel reach the game.
        Root.pickingMode = PickingMode.Ignore;

        host.AddComponent<BrainPanel>();
    }

    // Whether the pointer is over a panel, so a click there does not also select a car.
    public static bool PointerOverUI()
    {
        if (Root?.panel is null)
            return false;

        Vector2 mouse = Input.mousePosition;
        var position = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(mouse.x, Screen.height - mouse.y));
        var picked = Root.panel.Pick(position);
        return picked != null && picked != Root;
    }
}

using UnityEngine;
using UnityEngine.UIElements;

// One UI Toolkit document for every scene, created at startup and kept across scene loads (exhibition matches
// reload the scene), so no scene needs a UIDocument of its own.
public static class UIRoot
{
    private static VisualElement root;

    // Created on first use too: a scene's Start may run before the AfterSceneLoad callback.
    public static VisualElement Root
    {
        get
        {
            if (root == null)
                Create();
            return root;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (root != null)
            return;

        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/Theme");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1280, 720);
        settings.match = 0.5f;

        // Inactive while it is set up: the document attaches to its panel when it is enabled.
        var host = new GameObject("UI");
        host.SetActive(false);
        Object.DontDestroyOnLoad(host);
        var document = host.AddComponent<UIDocument>();
        document.panelSettings = settings;
        host.SetActive(true);

        root = document.rootVisualElement;
        root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Game"));
        root.AddToClassList("game-root");
        // Clicks that miss every panel reach the game.
        root.pickingMode = PickingMode.Ignore;

        host.AddComponent<HudPanel>();
        host.AddComponent<BrainPanel>();
        host.AddComponent<PauseMenu>();
    }

    // Whether the pointer is over a panel, so a click there does not also select a car.
    public static bool PointerOverUI()
    {
        if (root?.panel is null)
            return false;

        Vector2 mouse = Input.mousePosition;
        var position = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mouse.x, Screen.height - mouse.y));
        var picked = root.panel.Pick(position);
        return picked != null && picked != root;
    }
}

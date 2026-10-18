using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Status of the scene in the top-left corner, from whatever IHudSource is in it.
public class HudPanel : MonoBehaviour
{
    private const float SEARCH_EVERY_SECONDS = 0.5f;

    private VisualElement panel;
    private Label title;
    private VisualElement lines;
    private Slider speed;
    private readonly List<Label> lineLabels = new();

    private IHudSource source;
    private float searchIn;

    void Start()
    {
        panel = new VisualElement();
        panel.AddToClassList("panel");
        panel.AddToClassList("hud");
        title = new Label();
        title.AddToClassList("title");
        panel.Add(title);
        lines = new VisualElement();
        panel.Add(lines);
        speed = new Slider("Velocidad", 1, 10) { showInputField = true };
        speed.RegisterValueChangedCallback(change =>
        {
            if (source is { HasSpeed: true })
                source.Speed = change.newValue;
        });
        panel.Add(speed);
        UIRoot.Root.Add(panel);
    }

    void Update()
    {
        if ((searchIn -= Time.unscaledDeltaTime) <= 0 || source is Object destroyed && destroyed == null)
        {
            searchIn = SEARCH_EVERY_SECONDS;
            source = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IHudSource>().FirstOrDefault();
        }

        bool show = source is Object alive && alive != null && ((MonoBehaviour)source).isActiveAndEnabled && source.ShowHud;
        panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show)
            return;

        title.text = source.HudTitle;
        var texts = source.HudLines.ToList();
        while (lineLabels.Count < texts.Count)
        {
            var label = new Label();
            label.style.whiteSpace = WhiteSpace.Normal;
            lineLabels.Add(label);
            lines.Add(label);
        }
        for (int i = 0; i < lineLabels.Count; i++)
        {
            lineLabels[i].text = i < texts.Count ? texts[i] : "";
            lineLabels[i].style.display = i < texts.Count ? DisplayStyle.Flex : DisplayStyle.None;
        }

        speed.style.display = source.HasSpeed ? DisplayStyle.Flex : DisplayStyle.None;
        if (source.HasSpeed)
            speed.SetValueWithoutNotify(source.Speed);
    }
}

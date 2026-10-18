using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Best and average fitness per generation, like the loss chart of the suma-manual GUI.
public class FitnessChart : VisualElement
{
    private const float MARGIN = 22;
    private static readonly Color BEST = new(0.65f, 0.55f, 0.98f);
    private static readonly Color AVERAGE = new(0.13f, 0.77f, 0.37f);
    private static readonly Color GRID = new(0.23f, 0.23f, 0.37f);

    private IReadOnlyList<Vector2> history = new Vector2[0];
    private int drawnCount = -1;
    private readonly Label top = new(), bottom = new(), generations = new();

    public FitnessChart()
    {
        AddToClassList("chart");
        generateVisualContent += Draw;

        top.style.left = 4;
        top.style.top = 2;
        bottom.style.left = 4;
        bottom.style.bottom = 2;
        generations.style.right = 6;
        generations.style.bottom = 2;
        foreach (var label in new[] { top, bottom, generations })
        {
            label.AddToClassList("chart-axis");
            label.pickingMode = PickingMode.Ignore;
            Add(label);
        }
    }

    // Only redraws when a generation was added: the history only grows.
    public void Show(IReadOnlyList<Vector2> shown)
    {
        history = shown ?? new Vector2[0];
        if (history.Count == drawnCount)
            return;

        drawnCount = history.Count;
        Range(out float min, out float max);
        top.text = history.Count > 0 ? $"{max:F1}" : "";
        bottom.text = history.Count > 0 ? $"{min:F1}" : "Todavía no termina ninguna generación";
        generations.text = history.Count > 0 ? $"generación {history.Count}" : "";
        MarkDirtyRepaint();
    }

    private void Range(out float min, out float max)
    {
        min = 0;
        max = 1;
        foreach (var point in history)
        {
            min = Mathf.Min(min, point.x, point.y);
            max = Mathf.Max(max, point.x, point.y);
        }
    }

    private void Draw(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        var area = contentRect;
        var plot = new Rect(MARGIN + 18, MARGIN / 2, area.width - MARGIN - 26, area.height - MARGIN * 1.5f);

        painter.strokeColor = GRID;
        painter.lineWidth = 1;
        painter.BeginPath();
        painter.MoveTo(new Vector2(plot.xMin, plot.yMin));
        painter.LineTo(new Vector2(plot.xMin, plot.yMax));
        painter.LineTo(new Vector2(plot.xMax, plot.yMax));
        painter.Stroke();

        if (history.Count < 2)
            return;

        Range(out float min, out float max);
        Line(painter, plot, min, max, point => point.y, AVERAGE);
        Line(painter, plot, min, max, point => point.x, BEST);
    }

    private void Line(Painter2D painter, Rect plot, float min, float max, System.Func<Vector2, float> value, Color color)
    {
        painter.strokeColor = color;
        painter.lineWidth = 2;
        painter.BeginPath();
        for (int i = 0; i < history.Count; i++)
        {
            float x = Mathf.Lerp(plot.xMin, plot.xMax, i / (float)(history.Count - 1));
            float y = Mathf.Lerp(plot.yMax, plot.yMin, Mathf.InverseLerp(min, max, value(history[i])));
            if (i == 0)
                painter.MoveTo(new Vector2(x, y));
            else
                painter.LineTo(new Vector2(x, y));
        }
        painter.Stroke();
    }
}

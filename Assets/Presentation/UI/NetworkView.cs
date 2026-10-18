using Assets.Logic.AI;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Draws a network like the suma-manual GUI: green connections are positive weights, red negative, thicker
// is stronger; each neuron is filled with its activation in the last decision (green positive, red negative).
public class NetworkView : VisualElement
{
    private const float RADIUS = 9;
    private const float INPUT_LABELS_WIDTH = 150;
    private const float OUTPUT_LABELS_WIDTH = 130;
    private const float VERTICAL_MARGIN = 14;
    // Weights this strong or more get the full color and width.
    private const float STRONG_WEIGHT = 2.5f;

    private static readonly Color POSITIVE = new(0.13f, 0.77f, 0.37f);
    private static readonly Color NEGATIVE = new(0.94f, 0.27f, 0.27f);
    private static readonly Color NEURON = new(0.16f, 0.16f, 0.24f);
    private static readonly Color OUTLINE = new(0.49f, 0.23f, 0.93f);

    private NeuralNetwork network;
    private string[] inputNames = new string[0];
    private int[] shape = new int[0];
    // One label per neuron, layer by layer.
    private readonly List<Label> labels = new();

    public NetworkView()
    {
        AddToClassList("network");
        generateVisualContent += Draw;
        RegisterCallback<GeometryChangedEvent>(_ => PlaceLabels());
    }

    public void Show(NeuralNetwork shown, string[] names)
    {
        network = shown;
        inputNames = names ?? new string[0];

        var layers = shown is null ? new int[0] : new List<int>(shown.Layers).ToArray();
        if (!SameShape(layers))
            BuildLabels(layers);

        UpdateLabels();
        MarkDirtyRepaint();
    }

    private bool SameShape(int[] layers)
    {
        if (layers.Length != shape.Length)
            return false;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] != shape[i])
                return false;
        return true;
    }

    private void BuildLabels(int[] layers)
    {
        foreach (var label in labels)
            label.RemoveFromHierarchy();
        labels.Clear();
        shape = layers;

        for (int layer = 0; layer < shape.Length; layer++)
            for (int neuron = 0; neuron < shape[layer]; neuron++)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("neuron-label");
                label.style.unityTextAlign = layer == 0 ? TextAnchor.MiddleRight
                                           : layer == shape.Length - 1 ? TextAnchor.MiddleLeft
                                           : TextAnchor.MiddleCenter;
                labels.Add(label);
                Add(label);
            }

        PlaceLabels();
    }

    private void PlaceLabels()
    {
        int index = 0;
        for (int layer = 0; layer < shape.Length; layer++)
            for (int neuron = 0; neuron < shape[layer]; neuron++)
            {
                var label = labels[index++];
                var center = Position(layer, neuron);
                if (layer == 0)
                {
                    label.style.left = 0;
                    label.style.width = center.x - RADIUS - 6;
                    label.style.top = center.y - 8;
                }
                else if (layer == shape.Length - 1)
                {
                    label.style.left = center.x + RADIUS + 6;
                    label.style.width = OUTPUT_LABELS_WIDTH;
                    label.style.top = center.y - 8;
                }
                else
                {
                    label.style.left = center.x - 25;
                    label.style.width = 50;
                    label.style.top = center.y + RADIUS;
                }
            }
    }

    private void UpdateLabels()
    {
        if (network is null)
            return;

        var activations = network.LastActivations;
        int index = 0;
        for (int layer = 0; layer < shape.Length; layer++)
            for (int neuron = 0; neuron < shape[layer]; neuron++)
            {
                float value = layer < activations.Count ? activations[layer][neuron] : 0;
                labels[index++].text = layer == 0 ? $"{InputName(neuron)}  {value:+0.00;-0.00}"
                                     : layer == shape.Length - 1 ? OutputText(neuron, value)
                                     : $"{value:+0.00;-0.00}";
            }
    }

    private string InputName(int neuron) => neuron < inputNames.Length ? inputNames[neuron] : $"Entrada {neuron}";

    // CarPilot turns the outputs into key presses beyond its dead zone.
    private string OutputText(int neuron, float value)
    {
        bool pressed = Mathf.Abs(value) > CarPilot.OUTPUT_THRESHOLD;
        if (shape.Length == 0 || shape[shape.Length - 1] != CarPilot.OUTPUTS)
            return $"Salida {neuron}  {value:+0.00;-0.00}";

        return neuron == 0
                   ? $"Acelerador {value:+0.00;-0.00}\n{(pressed ? value > 0 ? "▲ adelante" : "▼ atrás" : "· suelto")}"
                   : $"Volante {value:+0.00;-0.00}\n{(pressed ? value > 0 ? "◀ izquierda" : "▶ derecha" : "· recto")}";
    }

    private Vector2 Position(int layer, int neuron)
    {
        var area = contentRect;
        float left = INPUT_LABELS_WIDTH, right = area.width - OUTPUT_LABELS_WIDTH;
        float x = shape.Length > 1 ? Mathf.Lerp(left, right, layer / (float)(shape.Length - 1)) : left;

        float height = area.height - 2 * VERTICAL_MARGIN;
        float y = VERTICAL_MARGIN + height * (neuron + 0.5f) / shape[layer];
        return new Vector2(x, y);
    }

    private void Draw(MeshGenerationContext context)
    {
        if (network is null || shape.Length < 2)
            return;

        var painter = context.painter2D;

        for (int layer = 0; layer < shape.Length - 1; layer++)
            for (int to = 0; to < shape[layer + 1]; to++)
                for (int from = 0; from < shape[layer]; from++)
                {
                    float weight = network.Weight(layer, from, to);
                    float strength = Mathf.Clamp01(Mathf.Abs(weight) / STRONG_WEIGHT);
                    var color = weight >= 0 ? POSITIVE : NEGATIVE;
                    color.a = 0.12f + 0.7f * strength;

                    painter.strokeColor = color;
                    painter.lineWidth = Mathf.Lerp(0.5f, 4, strength);
                    painter.BeginPath();
                    painter.MoveTo(Position(layer, from));
                    painter.LineTo(Position(layer + 1, to));
                    painter.Stroke();
                }

        var activations = network.LastActivations;
        for (int layer = 0; layer < shape.Length; layer++)
            for (int neuron = 0; neuron < shape[layer]; neuron++)
            {
                float value = layer < activations.Count ? activations[layer][neuron] : 0;

                painter.fillColor = Color.Lerp(NEURON, value >= 0 ? POSITIVE : NEGATIVE, Mathf.Clamp01(Mathf.Abs(value)));
                painter.strokeColor = OUTLINE;
                painter.lineWidth = 2;
                painter.BeginPath();
                painter.Arc(Position(layer, neuron), RADIUS, Angle.Degrees(0), Angle.Degrees(360));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            }
    }
}

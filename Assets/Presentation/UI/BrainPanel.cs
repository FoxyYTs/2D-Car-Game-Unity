using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Side panel with the brain of one AI car, live, and the fitness of past generations while training.
// Tab shows or hides it; clicking a car shows that car; "Auto" follows the leader again.
public class BrainPanel : MonoBehaviour
{
    private const KeyCode TOGGLE_KEY = KeyCode.Tab;
    private const float SEARCH_EVERY_SECONDS = 0.25f;

    private VisualElement panel;
    private Label hint;
    private Label carName;
    private Label carDetail;
    private Button autoButton;
    private NetworkView brainView;
    private VisualElement chartBox;
    private FitnessChart chart;

    private bool visible;
    private bool auto = true;
    private IPilotedCar shown;
    private IPilotedCar[] cars = new IPilotedCar[0];
    private float searchIn;
    private string[] inputNames;
    private CarPilot namesFor;

    void Start()
    {
        Build(UIRoot.Root);
        SetVisible(false);
    }

    private void Build(VisualElement root)
    {
        hint = new Label("Tab: ver la red neuronal");
        hint.AddToClassList("panel");
        hint.AddToClassList("muted");
        hint.AddToClassList("brain-hint");
        root.Add(hint);

        panel = new VisualElement();
        panel.AddToClassList("panel");
        panel.AddToClassList("brain-panel");
        root.Add(panel);

        var header = new VisualElement();
        header.AddToClassList("row");
        var title = new Label("Red neuronal");
        title.AddToClassList("title");
        title.style.flexGrow = 1;
        header.Add(title);
        autoButton = new Button(() => { auto = true; Search(); }) { text = "Auto" };
        autoButton.AddToClassList("button");
        header.Add(autoButton);
        var next = new Button(ShowNext) { text = "Siguiente »" };
        next.AddToClassList("button");
        header.Add(next);
        panel.Add(header);

        carName = new Label();
        panel.Add(carName);
        carDetail = new Label();
        carDetail.AddToClassList("muted");
        panel.Add(carDetail);

        brainView = new NetworkView();
        panel.Add(brainView);

        var legend = new Label("verde = peso positivo · rojo = negativo · grosor = magnitud · relleno = activación");
        legend.AddToClassList("muted");
        legend.style.whiteSpace = WhiteSpace.Normal;
        panel.Add(legend);

        chartBox = new VisualElement();
        var chartTitle = new VisualElement();
        chartTitle.AddToClassList("row");
        chartTitle.style.marginTop = 8;
        chartTitle.Add(new Label("Fitness por generación: "));
        var best = new Label("mejor");
        best.AddToClassList("legend-best");
        chartTitle.Add(best);
        chartTitle.Add(new Label(" / "));
        var average = new Label("promedio");
        average.AddToClassList("legend-average");
        chartTitle.Add(average);
        chartBox.Add(chartTitle);
        chart = new FitnessChart();
        chartBox.Add(chart);
        panel.Add(chartBox);

        var keys = new Label("Tab: ocultar · clic en un carro: ver su red");
        keys.AddToClassList("muted");
        keys.style.marginTop = 6;
        panel.Add(keys);
    }

    void Update()
    {
        if (Input.GetKeyDown(TOGGLE_KEY) && !GameSession.InMenu)
            SetVisible(!visible);

        if ((searchIn -= Time.unscaledDeltaTime) <= 0 || !IsShowable(shown))
            Search();

        if (!visible)
        {
            hint.text = cars.Length > 0 ? "Tab: ver la red neuronal · Esc: pausa" : "Esc: pausa";
            hint.style.display = GameSession.InMenu ? DisplayStyle.None : DisplayStyle.Flex;
            return;
        }

        if (Input.GetMouseButtonDown(0) && !UIRoot.PointerOverUI())
            SelectClickedCar();

        Refresh();
    }

    private void SetVisible(bool value)
    {
        visible = value;
        panel.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Destroyed cars (scene reloads between matches) compare equal to null only as Unity objects.
    private static bool IsShowable(IPilotedCar car) => car is Object unityObject && unityObject != null &&
                                                       car.Pilot != null && car.Active;

    private void Search()
    {
        searchIn = SEARCH_EVERY_SECONDS;
        cars = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                   .OfType<IPilotedCar>()
                   .Where(IsShowable)
                   .OrderBy(car => ((Object)car).name)
                   .ToArray();

        if (!auto && IsShowable(shown))
            return;

        auto = true;
        shown = Leader();
    }

    // While training on checkpoints, the agent with the best fitness so far; otherwise the first car.
    private IPilotedCar Leader()
    {
        var agents = cars.OfType<CarAgent>().ToArray();
        if (agents.Length > 0)
            return agents.OrderByDescending(agent => agent.Fitness).First();
        return cars.FirstOrDefault();
    }

    private void ShowNext()
    {
        Search();
        if (cars.Length == 0)
            return;

        int index = System.Array.IndexOf(cars, shown);
        shown = cars[(index + 1) % cars.Length];
        auto = false;
    }

    private void SelectClickedCar()
    {
        var camera = Camera.main;
        if (camera == null)
            return;

        Vector2 point = camera.ScreenToWorldPoint(Input.mousePosition);
        var clicked = Physics2D.OverlapPointAll(point)
                               .Select(collider => collider.GetComponentInParent<IPilotedCar>())
                               .FirstOrDefault(IsShowable);
        if (clicked == null)
            return;

        shown = clicked;
        auto = false;
    }

    private void Refresh()
    {
        autoButton.EnableInClassList("button--on", auto);

        if (!IsShowable(shown))
        {
            carName.text = "No hay ningún carro manejado por la IA";
            carDetail.text = "";
            brainView.Show(null, null);
        }
        else
        {
            var pilot = shown.Pilot;
            if (namesFor != pilot)
            {
                inputNames = pilot.InputNames();
                namesFor = pilot;
            }

            carName.text = $"{((Object)shown).name}{(auto ? " (líder)" : "")}";
            carDetail.text = Detail(shown, pilot);
            brainView.Show(pilot.Brain, inputNames);
        }

        var history = History();
        chartBox.style.display = history is null ? DisplayStyle.None : DisplayStyle.Flex;
        chart.Show(history?.History, history?.CircuitStarts);
    }

    private static string Detail(IPilotedCar car, CarPilot pilot)
    {
        string layers = string.Join(" → ", pilot.Brain.Layers);
        return car switch
        {
            CarAgent { Task: CheckpointTask task } agent => $"Red {layers} · fitness {agent.Fitness:F2} · checkpoints {task.Reached}",
            CarAgent { Task: ExplorationTask task } agent => $"Red {layers} · fitness {agent.Fitness:F2} · celdas {task.Visited}",
            _ => $"Red {layers}"
        };
    }

    // The running trainer: the curriculum disables the TrainingManager of its scene.
    private static IFitnessHistory History() =>
        FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(component => component.isActiveAndEnabled)
            .OfType<IFitnessHistory>()
            .FirstOrDefault();
}

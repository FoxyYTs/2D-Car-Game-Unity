using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Start screen: pick a mode, its brains and settings, and play. Values are kept in GameSession, so coming back
// to the menu shows the last choice.
public class MainMenu : MonoBehaviour
{
    private const string AUTO_BRAIN = "Automático (el más reciente)";

    private static readonly (GameMode mode, string title, string description)[] MODES =
    {
        (GameMode.HumanVsHuman, "Humano vs Humano", "Dos jugadores en el mismo teclado."),
        (GameMode.HumanVsAI, "Humano vs IA", "Juega contra un cerebro entrenado."),
        (GameMode.AIVsAI, "IA vs IA", "Exhibición: las partidas se reinician solas y llevan marcador."),
        (GameMode.TrainCheckpoints, "Entrenar: circuito", "La población aprende a seguir checkpoints alrededor de la montaña."),
        (GameMode.TrainRandomPoints, "Entrenar: puntos al azar", "Los checkpoints aparecen en cualquier lugar del mapa: aprende a moverse libremente."),
        (GameMode.TrainExploration, "Entrenar: exploración", "Sin objetivo: gana puntos por recorrer zonas nuevas del mapa sin chocar."),
        (GameMode.TrainCurriculum, "Entrenar: currículo", "Anillo, puntos al azar y exploración en cada generación; se guarda el que mejor valida."),
        (GameMode.TrainChase, "Entrenar: persecución", "Un carro naranja huye por puntos al azar y la población aprende a embestirlo."),
        (GameMode.TrainVersus, "Entrenar: versus", "Los cerebros se enfrentan por parejas para aprender a embestir.")
    };

    private static GameMode selected = GameMode.AIVsAI;

    private VisualElement menu;
    private VisualElement options;
    private readonly Dictionary<GameMode, Button> modeButtons = new();
    private List<BrainLibrary.Entry> brains;

    void Start()
    {
        Time.timeScale = 1;
        brains = BrainLibrary.List();
        Build(UIRoot.Root);
        Select(selected);
    }

    void OnDestroy()
    {
        menu?.RemoveFromHierarchy();
    }

    private void Build(VisualElement root)
    {
        menu = new VisualElement();
        menu.AddToClassList("menu-screen");
        root.Add(menu);

        var card = new VisualElement();
        card.AddToClassList("panel");
        card.AddToClassList("menu");
        menu.Add(card);

        var title = new Label("2D Car Game · IA");
        title.AddToClassList("menu-title");
        card.Add(title);
        var subtitle = new Label("Neuroevolución: redes neuronales entrenadas con un algoritmo genético");
        subtitle.AddToClassList("muted");
        card.Add(subtitle);

        var columns = new VisualElement();
        columns.AddToClassList("menu-columns");
        card.Add(columns);

        // More modes than fit in a small window.
        var modes = new ScrollView(ScrollViewMode.Vertical);
        modes.AddToClassList("menu-modes");
        columns.Add(modes);
        foreach (var (mode, modeTitle, description) in MODES)
        {
            var button = new Button(() => Select(mode));
            button.AddToClassList("mode-button");
            var name = new Label(modeTitle);
            name.AddToClassList("mode-title");
            button.Add(name);
            var text = new Label(description);
            text.AddToClassList("muted");
            text.style.whiteSpace = WhiteSpace.Normal;
            button.Add(text);
            modes.Add(button);
            modeButtons[mode] = button;
        }

        var right = new VisualElement();
        right.AddToClassList("menu-options");
        columns.Add(right);
        options = new VisualElement();
        options.style.flexGrow = 1;
        right.Add(options);

        var actions = new VisualElement();
        actions.AddToClassList("row");
        actions.style.justifyContent = Justify.FlexEnd;
        var quit = new Button(Quit) { text = "Salir" };
        quit.AddToClassList("button");
        actions.Add(quit);
        var play = new Button(() => GameSession.Play(selected)) { text = "Comenzar" };
        play.AddToClassList("button");
        play.AddToClassList("button--on");
        play.AddToClassList("button--big");
        actions.Add(play);
        right.Add(actions);
    }

    private void Select(GameMode mode)
    {
        selected = mode;
        foreach (var pair in modeButtons)
            pair.Value.EnableInClassList("mode-button--selected", pair.Key == mode);

        options.Clear();
        var (_, title, _) = MODES.First(entry => entry.mode == mode);
        var heading = new Label(title);
        heading.AddToClassList("title");
        options.Add(heading);

        switch (mode)
        {
            case GameMode.HumanVsHuman:
                Note("Carro verde: W A S D\nCarro azul: flechas\nGana quien embiste al otro o quien no se estrella.");
                break;

            case GameMode.HumanVsAI:
                var side = new DropdownField("Tu carro", new List<string> { "Verde (W A S D)", "Azul (flechas)" },
                                             GameSession.HumanCarId == "A" ? 0 : 1);
                side.RegisterValueChangedCallback(_ => GameSession.HumanCarId = side.index == 0 ? "A" : "B");
                options.Add(side);
                BrainField("Cerebro de la IA", () => GameSession.OpponentBrain, file => GameSession.OpponentBrain = file);
                break;

            case GameMode.AIVsAI:
                BrainField("Cerebro del carro verde", () => GameSession.BrainA, file => GameSession.BrainA = file);
                BrainField("Cerebro del carro azul", () => GameSession.BrainB, file => GameSession.BrainB = file);
                Note("Pon dos cerebros distintos a pelear, o el mismo contra sí mismo.");
                break;

            case GameMode.TrainCheckpoints:
            case GameMode.TrainRandomPoints:
                TrainingFields();
                var circuit = new IntegerField("Cambiar de recorrido cada (gens)") { value = GameSession.CircuitChangeEvery };
                circuit.RegisterValueChangedCallback(change => GameSession.CircuitChangeEvery = Math.Max(0, change.newValue));
                options.Add(circuit);
                Note("0 deja siempre el mismo circuito. Se guarda brain-seed<semilla>.json al terminar cada circuito.");
                break;

            case GameMode.TrainCurriculum:
                TrainingFields();
                var courses = new IntegerField("Cambiar de recorridos cada (gens)") { value = Math.Max(1, GameSession.CircuitChangeEvery) };
                courses.RegisterValueChangedCallback(change => GameSession.CircuitChangeEvery = Math.Max(1, change.newValue));
                options.Add(courses);
                Note("Cada generación tiene 3 etapas y el fitness combina sus puntajes (0 a 1, chocar resta). " +
                     "Cada 10 generaciones los 5 mejores corren una batería fija de validación, siempre la misma; " +
                     "solo se guarda brain-seed<semilla>.json cuando mejora la validación.");
                break;

            case GameMode.TrainChase:
                TrainingFields();
                BrainField("Cerebro del que huye", () => GameSession.RunnerBrainFile, file => GameSession.RunnerBrainFile = file,
                           entry => entry.Brain.Course is "RandomPoints" or "Curriculum");
                Note("Automático usa el cerebro más reciente de puntos al azar o del currículo; los perseguidores " +
                     "arrancan de copias mutadas de él. Embestir vale 2, otro contacto 1 y acercarse hasta 1.");
                break;

            case GameMode.TrainExploration:
                TrainingFields();
                Note("El mapa se divide en celdas: el fitness es cuántas celdas distintas recorre. " +
                     "Se guarda brain-seed<semilla>.json con cada récord.");
                break;

            case GameMode.TrainVersus:
                TrainingFields();
                BrainField("Cerebro semilla", () => GameSession.SeedBrainFile, file => GameSession.SeedBrainFile = file,
                           entry => !entry.IsVersus);
                Note("La población arranca de copias mutadas del cerebro semilla. Se guarda versus-seed<semilla>.json.");
                break;
        }
    }

    private void TrainingFields()
    {
        var population = new IntegerField("Población") { value = GameSession.PopulationSize };
        population.RegisterValueChangedCallback(change => GameSession.PopulationSize = Math.Clamp(change.newValue, 2, 500));
        options.Add(population);

        var seed = new IntegerField("Semilla (0 = al azar)") { value = GameSession.Seed };
        seed.RegisterValueChangedCallback(change => GameSession.Seed = Math.Max(0, change.newValue));
        options.Add(seed);

        var speed = new Slider("Velocidad", 1, 10) { value = GameSession.SimulationSpeed, showInputField = true };
        speed.RegisterValueChangedCallback(change => GameSession.SimulationSpeed = change.newValue);
        options.Add(speed);
    }

    private void BrainField(string label, Func<string> current, Action<string> set,
                            Func<BrainLibrary.Entry, bool> filter = null)
    {
        var shown = brains.Where(filter ?? (_ => true)).ToList();
        var choices = new List<string> { AUTO_BRAIN };
        choices.AddRange(shown.Select(entry => entry.Label));

        int index = shown.FindIndex(entry => entry.File == current()) + 1;
        var field = new DropdownField(label, choices, index);
        field.RegisterValueChangedCallback(_ => set(field.index <= 0 ? "" : shown[field.index - 1].File));
        options.Add(field);

        if (shown.Count == 0)
            Note("No hay cerebros guardados: primero entrena uno.");
    }

    private void Note(string text)
    {
        var note = new Label(text);
        note.AddToClassList("muted");
        note.AddToClassList("menu-note");
        options.Add(note);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

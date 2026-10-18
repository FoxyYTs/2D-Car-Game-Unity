using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Esc pauses any scene but the main menu: resume, restart the scene or go back to the menu.
public class PauseMenu : MonoBehaviour
{
    private const KeyCode TOGGLE_KEY = KeyCode.Escape;

    // Trainers set Time.timeScale every frame; while paused they must leave it alone.
    public static bool Paused { get; private set; }

    private VisualElement overlay;
    private Button menuButton;
    private float scaleBeforePause = 1;

    void Start()
    {
        overlay = new VisualElement();
        overlay.AddToClassList("pause-overlay");

        var box = new VisualElement();
        box.AddToClassList("panel");
        box.AddToClassList("pause-box");
        var title = new Label("Pausa");
        title.AddToClassList("menu-title");
        box.Add(title);
        box.Add(MenuButton("Reanudar", Resume));
        box.Add(MenuButton("Reiniciar", Restart));
        menuButton = MenuButton("Menú principal", ToMenu);
        box.Add(menuButton);
        overlay.Add(box);

        UIRoot.Root.Add(overlay);
        Show(false);
    }

    private static Button MenuButton(string text, System.Action action)
    {
        var button = new Button(action) { text = text };
        button.AddToClassList("button");
        button.AddToClassList("button--big");
        return button;
    }

    void Update()
    {
        if (!Input.GetKeyDown(TOGGLE_KEY) || GameSession.InMenu)
            return;

        if (Paused)
            Resume();
        else
            Pause();
    }

    private void Pause()
    {
        scaleBeforePause = Time.timeScale;
        Time.timeScale = 0;
        Paused = true;
        menuButton.style.display = GameSession.HasMenu ? DisplayStyle.Flex : DisplayStyle.None;
        Show(true);
    }

    private void Resume()
    {
        // A match that had already ended (time scale 0) stays ended.
        Time.timeScale = scaleBeforePause;
        Paused = false;
        Show(false);
    }

    private void Restart()
    {
        Unpause();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void ToMenu()
    {
        Unpause();
        GameSession.ToMenu();
    }

    private void Unpause()
    {
        Paused = false;
        Time.timeScale = 1;
        Show(false);
    }

    private void Show(bool value) => overlay.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
}

using System.Collections.Generic;

// Whatever runs the scene (a trainer, the exhibition) and has a status to show in the HUD.
public interface IHudSource
{
    bool ShowHud { get; }
    string HudTitle { get; }
    IEnumerable<string> HudLines { get; }
    // Trainers let the speed be changed from the HUD; the rest return false.
    bool HasSpeed { get; }
    float Speed { get; set; }
}

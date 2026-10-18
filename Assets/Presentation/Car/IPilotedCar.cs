// A car driven by a neural network, for the brain viewer.
public interface IPilotedCar
{
    CarPilot Pilot { get; }
    // Driving right now: not dead, benched or handed back to the keyboard.
    bool Active { get; }
}

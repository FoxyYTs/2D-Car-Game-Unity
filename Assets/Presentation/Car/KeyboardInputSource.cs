using EngineAbstractor;
using System.Collections.Generic;

public class KeyboardInputSource : IInputSource
{
    private readonly ControlType controlType;

    public KeyboardInputSource(string id)
    {
        controlType = id.ToLower() == nameof(ControlType.A).ToLower()
                              ? ControlType.A
                              : ControlType.B;
    }

    public IReadOnlyList<InputValue> Read() => CarKeyboardReader.Inputs(controlType);
}

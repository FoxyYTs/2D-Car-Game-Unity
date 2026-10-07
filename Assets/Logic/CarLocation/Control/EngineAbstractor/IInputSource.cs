using System.Collections.Generic;

namespace EngineAbstractor
{
    public interface IInputSource
    {
        IReadOnlyList<InputValue> Read();
    }
}

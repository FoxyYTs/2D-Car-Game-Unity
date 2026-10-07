using System;
using System.Collections.Generic;

namespace Assets.Logic.AI
{
    /// <summary>Elige un padre de la población según su fitness.</summary>
    public interface ISelection
    {
        /// <returns>Índice del individuo elegido dentro de <paramref name="fitness"/>.</returns>
        int Select(IReadOnlyList<float> fitness, Random random);
    }
}

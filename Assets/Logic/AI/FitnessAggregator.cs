using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Logic.AI
{
    /// <summary>
    /// Combina los puntajes de un genoma en varios entornos, cada uno normalizado a [0, 1], en un solo fitness.
    /// El promedio solo dejaría que un entorno fácil tape uno en el que el genoma falla: por eso el peor
    /// puntaje pesa aparte.
    /// </summary>
    public static class FitnessAggregator
    {
        /// <summary>
        /// (1 - worstWeight) · promedio + worstWeight · mínimo. Los puntajes se recortan a [0, 1].
        /// Sin puntajes, o con worstWeight fuera de [0, 1], lanza <see cref="ArgumentException"/>.
        /// </summary>
        public static float Combine(IReadOnlyList<float> scores, float worstWeight = 0.5f)
        {
            if (scores is null || scores.Count == 0)
                throw new ArgumentException("There must be at least one score", nameof(scores));
            if (worstWeight < 0 || worstWeight > 1)
                throw new ArgumentException("The weight of the worst score must be within [0, 1]", nameof(worstWeight));

            var clamped = scores.Select(score => Math.Clamp(score, 0f, 1f)).ToArray();
            return (1 - worstWeight) * clamped.Average() + worstWeight * clamped.Min();
        }
    }
}

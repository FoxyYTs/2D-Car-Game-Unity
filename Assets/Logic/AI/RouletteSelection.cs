using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Logic.AI
{
    /// <summary>
    /// Selección proporcional al fitness: la probabilidad de elegir a i es fitness[i] / Σ fitness.
    /// Si algún fitness es negativo, lanza <see cref="ArgumentException"/>.
    /// Si todos son 0, elige uniformemente al azar.
    /// </summary>
    public class RouletteSelection : ISelection
    {
        public int Select(IReadOnlyList<float> fitness, Random random)
        {
            if (fitness.Any(value => value < 0))
                throw new ArgumentException("Roulette selection needs non-negative fitness", nameof(fitness));

            double total = fitness.Sum(value => (double)value);
            if (total == 0)
                return random.Next(fitness.Count);

            double spin = random.NextDouble() * total;
            double accumulated = 0;
            int lastPositive = 0;

            for (int i = 0; i < fitness.Count; i++)
            {
                if (fitness[i] == 0)
                    continue;

                accumulated += fitness[i];
                lastPositive = i;
                if (spin < accumulated)
                    return i;
            }

            // Floating point rounding can leave spin a hair above the accumulated total.
            return lastPositive;
        }
    }
}

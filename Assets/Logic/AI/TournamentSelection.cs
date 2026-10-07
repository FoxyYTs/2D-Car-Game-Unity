using System;
using System.Collections.Generic;

namespace Assets.Logic.AI
{
    /// <summary>
    /// Toma <see cref="Size"/> individuos al azar (con reemplazo) y devuelve el de mayor fitness.
    /// Un torneo más grande aumenta la presión selectiva.
    /// </summary>
    public class TournamentSelection : ISelection
    {
        public int Size { get; }

        /// <summary>Si <paramref name="size"/> es menor que 1, lanza <see cref="ArgumentException"/>.</summary>
        public TournamentSelection(int size)
        {
            if (size < 1)
                throw new ArgumentException("The tournament needs at least one contestant", nameof(size));

            Size = size;
        }

        public int Select(IReadOnlyList<float> fitness, Random random)
        {
            int winner = random.Next(fitness.Count);

            for (int round = 1; round < Size; round++)
            {
                int contestant = random.Next(fitness.Count);
                if (fitness[contestant] > fitness[winner])
                    winner = contestant;
            }

            return winner;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Logic.AI
{
    /// <summary>
    /// Evoluciona genomas (float[]) sin saber qué representan: solo necesita su fitness.
    /// Toda la aleatoriedad sale del <see cref="Random"/> recibido, así una misma semilla
    /// reproduce exactamente la misma evolución.
    /// </summary>
    public class GeneticAlgorithm
    {
        public GeneticSettings Settings { get; }

        private readonly ISelection selection;
        private readonly Random random;

        /// <summary>
        /// Si EliteCount es negativo o mayor que PopulationSize, o PopulationSize es menor que 1,
        /// lanza <see cref="ArgumentException"/>.
        /// </summary>
        public GeneticAlgorithm(GeneticSettings settings, ISelection selection, Random random)
        {
            if (settings.PopulationSize < 1)
                throw new ArgumentException("The population needs at least one individual", nameof(settings));
            if (settings.EliteCount < 0 || settings.EliteCount > settings.PopulationSize)
                throw new ArgumentException("The elite must fit within the population", nameof(settings));

            Settings = settings;
            this.selection = selection;
            this.random = random;
        }

        /// <summary>
        /// PopulationSize genomas de longitud <paramref name="genomeLength"/>,
        /// con genes uniformes en [-InitialRange, InitialRange].
        /// </summary>
        public float[][] CreatePopulation(int genomeLength)
        {
            var population = new float[Settings.PopulationSize][];

            for (int individual = 0; individual < population.Length; individual++)
            {
                population[individual] = new float[genomeLength];
                for (int gene = 0; gene < genomeLength; gene++)
                    population[individual][gene] = (float)((random.NextDouble() * 2 - 1) * Settings.InitialRange);
            }

            return population;
        }

        /// <summary>
        /// 1. Copia los EliteCount genomas de mayor fitness sin mutarlos, al inicio y de mejor a peor.
        /// 2. Completa hasta PopulationSize con hijos: elige dos padres con la selección,
        ///    los cruza y muta al hijo.
        /// No modifica <paramref name="population"/>: los genomas devueltos son arreglos nuevos.
        /// Si population y fitness no tienen la misma longitud, lanza <see cref="ArgumentException"/>.
        /// </summary>
        public float[][] NextGeneration(IReadOnlyList<float[]> population, IReadOnlyList<float> fitness)
        {
            if (population.Count != fitness.Count)
                throw new ArgumentException("Every individual needs a fitness value", nameof(fitness));

            var next = new List<float[]>(Settings.PopulationSize);

            var elite = Enumerable.Range(0, population.Count)
                                  .OrderByDescending(individual => fitness[individual])
                                  .Take(Settings.EliteCount);
            foreach (int individual in elite)
                next.Add(population[individual].ToArray());

            while (next.Count < Settings.PopulationSize)
            {
                var mother = population[selection.Select(fitness, random)];
                var father = population[selection.Select(fitness, random)];

                var child = Crossover(mother, father);
                Mutate(child);
                next.Add(child);
            }

            return next.ToArray();
        }

        /// <summary>
        /// Cruce uniforme: cada gen del hijo viene, con probabilidad 0.5, del padre a o del padre b
        /// (en la misma posición). Devuelve un arreglo nuevo.
        /// </summary>
        public float[] Crossover(float[] a, float[] b)
        {
            var child = new float[a.Length];
            for (int gene = 0; gene < child.Length; gene++)
                child[gene] = random.NextDouble() < 0.5 ? a[gene] : b[gene];
            return child;
        }

        /// <summary>
        /// Muta en el lugar: cada gen, con probabilidad MutationRate, recibe + N(0, MutationSigma).
        /// </summary>
        public void Mutate(float[] genome)
        {
            for (int gene = 0; gene < genome.Length; gene++)
                if (random.NextDouble() < Settings.MutationRate)
                    genome[gene] += NextGaussian(random) * Settings.MutationSigma;
        }

        /// <summary>
        /// Muestra de una normal estándar N(0, 1). System.Random solo da uniformes:
        /// usa la transformada de Box-Muller.
        /// </summary>
        public static float NextGaussian(Random random)
        {
            // 1 - NextDouble() lies in (0, 1], so Log never receives 0.
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();

            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}

using System;
using System.Linq;
using Assets.Logic.AI;
using NUnit.Framework;

namespace Assets.Tests.EditMode
{
    public class GeneticAlgorithmTests
    {
        private const int SEED = 42;

        private static GeneticAlgorithm Create(GeneticSettings settings = null, int seed = SEED) =>
            new(settings ?? new GeneticSettings(), new TournamentSelection(3), new Random(seed));

        [Test]
        public void Constructor_RejectsInvalidSettings()
        {
            Assert.Throws<ArgumentException>(() => Create(new GeneticSettings { PopulationSize = 0 }));
            Assert.Throws<ArgumentException>(() => Create(new GeneticSettings { PopulationSize = 5, EliteCount = 6 }));
            Assert.Throws<ArgumentException>(() => Create(new GeneticSettings { EliteCount = -1 }));
        }

        [Test]
        public void CreatePopulation_HasConfiguredSizeAndGenomeLength()
        {
            var population = Create(new GeneticSettings { PopulationSize = 20 }).CreatePopulation(106);

            Assert.AreEqual(20, population.Length);
            Assert.That(population.Select(g => g.Length), Has.All.EqualTo(106));
        }

        [Test]
        public void CreatePopulation_GenesAreWithinInitialRange()
        {
            var population = Create(new GeneticSettings { InitialRange = 0.5f }).CreatePopulation(100);

            Assert.That(population.SelectMany(g => g), Has.All.InRange(-0.5f, 0.5f));
        }

        [Test]
        public void CreatePopulation_SameSeedSamePopulation()
        {
            var first = Create(seed: 7).CreatePopulation(10);
            var second = Create(seed: 7).CreatePopulation(10);

            Assert.AreEqual(first.SelectMany(g => g).ToArray(), second.SelectMany(g => g).ToArray());
        }

        [Test]
        public void Crossover_EachGeneComesFromOneParentAtSamePosition()
        {
            var algorithm = Create();
            var a = Enumerable.Range(0, 1000).Select(i => (float)i).ToArray();
            var b = Enumerable.Range(0, 1000).Select(i => (float)-i - 1).ToArray();

            var child = algorithm.Crossover(a, b);

            Assert.AreEqual(a.Length, child.Length);
            for (int i = 0; i < child.Length; i++)
                Assert.That(child[i] == a[i] || child[i] == b[i], $"Gen {i} no viene de ningún padre");
        }

        [Test]
        public void Crossover_MixesBothParentsEvenly()
        {
            var algorithm = Create();
            var a = new float[1000];
            var b = Enumerable.Repeat(1f, 1000).ToArray();

            var child = algorithm.Crossover(a, b);

            Assert.That(child.Count(g => g == 1f), Is.InRange(430, 570));
            Assert.AreNotSame(a, child);
            Assert.AreNotSame(b, child);
        }

        [Test]
        public void Mutate_RateZero_LeavesGenomeUntouched()
        {
            var algorithm = Create(new GeneticSettings { MutationRate = 0f });
            var genome = Enumerable.Range(0, 100).Select(i => (float)i).ToArray();
            var original = genome.ToArray();

            algorithm.Mutate(genome);

            CollectionAssert.AreEqual(original, genome);
        }

        [Test]
        public void Mutate_RateOne_ChangesEveryGene()
        {
            var algorithm = Create(new GeneticSettings { MutationRate = 1f });
            var genome = new float[100];

            algorithm.Mutate(genome);

            Assert.That(genome, Has.None.EqualTo(0f));
        }

        [Test]
        public void Mutate_AddsNoiseWithConfiguredSigma()
        {
            var algorithm = Create(new GeneticSettings { MutationRate = 1f, MutationSigma = 0.3f });
            var genome = new float[10000];

            algorithm.Mutate(genome);

            Assert.That(genome.Average(), Is.EqualTo(0f).Within(0.02f));
            Assert.That(StandardDeviation(genome), Is.EqualTo(0.3f).Within(0.02f));
        }

        [Test]
        public void NextGaussian_IsStandardNormal()
        {
            var random = new Random(SEED);
            var samples = Enumerable.Range(0, 20000).Select(_ => GeneticAlgorithm.NextGaussian(random)).ToArray();

            Assert.That(samples.Average(), Is.EqualTo(0f).Within(0.03f));
            Assert.That(StandardDeviation(samples), Is.EqualTo(1f).Within(0.03f));
            // En una normal estándar ≈ 68 % cae en [-1, 1]; en una uniforme sería distinto.
            Assert.That(samples.Count(s => Math.Abs(s) <= 1) / (float)samples.Length, Is.EqualTo(0.683f).Within(0.02f));
        }

        [Test]
        public void NextGeneration_KeepsPopulationSize()
        {
            var algorithm = Create(new GeneticSettings { PopulationSize = 10 });
            var population = algorithm.CreatePopulation(5);

            var next = algorithm.NextGeneration(population, Enumerable.Range(0, 10).Select(i => (float)i).ToArray());

            Assert.AreEqual(10, next.Length);
            Assert.That(next.Select(g => g.Length), Has.All.EqualTo(5));
        }

        [Test]
        public void NextGeneration_CopiesElitesUnchanged()
        {
            var algorithm = Create(new GeneticSettings { PopulationSize = 6, EliteCount = 2, MutationRate = 1f });
            var population = algorithm.CreatePopulation(4);
            var fitness = new[] { 1f, 9f, 3f, 7f, 0f, 2f };

            var next = algorithm.NextGeneration(population, fitness);

            Assert.That(next.Any(g => g.SequenceEqual(population[1])), "Falta el mejor individuo");
            Assert.That(next.Any(g => g.SequenceEqual(population[3])), "Falta el segundo mejor individuo");
            Assert.That(next.All(g => population.All(p => !ReferenceEquals(p, g))), "La élite debe copiarse, no compartirse");
        }

        [Test]
        public void NextGeneration_DoesNotModifyCurrentPopulation()
        {
            var algorithm = Create(new GeneticSettings { PopulationSize = 8, MutationRate = 1f });
            var population = algorithm.CreatePopulation(6);
            var snapshot = population.Select(g => g.ToArray()).ToArray();

            algorithm.NextGeneration(population, Enumerable.Repeat(1f, 8).ToArray());

            for (int i = 0; i < population.Length; i++)
                CollectionAssert.AreEqual(snapshot[i], population[i]);
        }

        [Test]
        public void NextGeneration_RejectsMismatchedFitness()
        {
            var algorithm = Create(new GeneticSettings { PopulationSize = 4 });
            var population = algorithm.CreatePopulation(3);

            Assert.Throws<ArgumentException>(() => algorithm.NextGeneration(population, new float[3]));
        }

        [Test]
        public void Evolution_ImprovesFitnessOnSimpleProblem()
        {
            // Problema de juguete: maximizar la suma de genes acotados a [-1, 1].
            var settings = new GeneticSettings { PopulationSize = 30, EliteCount = 2, MutationRate = 0.2f, MutationSigma = 0.2f };
            var algorithm = Create(settings);
            var population = algorithm.CreatePopulation(10);

            float Fitness(float[] g) => g.Sum(x => Math.Clamp(x, -1f, 1f)) + 10f;
            float initialBest = population.Max(Fitness);

            for (int generation = 0; generation < 40; generation++)
                population = algorithm.NextGeneration(population, population.Select(Fitness).ToArray());

            Assert.That(population.Max(Fitness), Is.GreaterThan(initialBest + 2f));
        }

        private static float StandardDeviation(float[] values)
        {
            double mean = values.Average();
            return (float)Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Length);
        }
    }
}

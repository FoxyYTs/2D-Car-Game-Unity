using System;
using System.Linq;
using Assets.Logic.AI;
using NUnit.Framework;

namespace Assets.Tests.EditMode
{
    public class SelectionTests
    {
        private const int SEED = 42;
        private const int DRAWS = 10000;

        [Test]
        public void Tournament_RejectsSizeBelowOne()
        {
            Assert.Throws<ArgumentException>(() => new TournamentSelection(0));
        }

        [Test]
        public void Tournament_ReturnsValidIndex()
        {
            var selection = new TournamentSelection(3);
            var random = new Random(SEED);
            var fitness = new[] { 1f, 5f, 2f, 0f };

            for (int i = 0; i < 100; i++)
                Assert.That(selection.Select(fitness, random), Is.InRange(0, fitness.Length - 1));
        }

        [Test]
        public void Tournament_LargeTournament_AlmostAlwaysPicksTheBest()
        {
            // Con reemplazo, un torneo de 100 sobre 5 individuos deja fuera al mejor con probabilidad 0.8^100 ≈ 2e-10.
            var selection = new TournamentSelection(100);
            var random = new Random(SEED);
            var fitness = new[] { 1f, 5f, 9f, 2f, 0f };

            for (int i = 0; i < 100; i++)
                Assert.AreEqual(2, selection.Select(fitness, random));
        }

        [Test]
        public void Tournament_SizeOne_IsUniform()
        {
            var selection = new TournamentSelection(1);
            var random = new Random(SEED);
            var fitness = new[] { 100f, 0f };

            int worst = Enumerable.Range(0, DRAWS).Count(_ => selection.Select(fitness, random) == 1);

            Assert.That(worst / (float)DRAWS, Is.EqualTo(0.5f).Within(0.03f));
        }

        [Test]
        public void Roulette_IsProportionalToFitness()
        {
            var selection = new RouletteSelection();
            var random = new Random(SEED);
            var fitness = new[] { 1f, 3f };

            int second = Enumerable.Range(0, DRAWS).Count(_ => selection.Select(fitness, random) == 1);

            Assert.That(second / (float)DRAWS, Is.EqualTo(0.75f).Within(0.03f));
        }

        [Test]
        public void Roulette_NeverPicksZeroFitnessWhenOthersArePositive()
        {
            var selection = new RouletteSelection();
            var random = new Random(SEED);
            var fitness = new[] { 0f, 0f, 5f, 0f };

            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(2, selection.Select(fitness, random));
        }

        [Test]
        public void Roulette_AllZero_PicksUniformly()
        {
            var selection = new RouletteSelection();
            var random = new Random(SEED);
            var fitness = new[] { 0f, 0f, 0f, 0f };

            var counts = new int[fitness.Length];
            for (int i = 0; i < DRAWS; i++)
                counts[selection.Select(fitness, random)]++;

            Assert.That(counts.Select(c => c / (float)DRAWS), Has.All.EqualTo(0.25f).Within(0.03f));
        }

        [Test]
        public void Roulette_RejectsNegativeFitness()
        {
            var selection = new RouletteSelection();

            Assert.Throws<ArgumentException>(() => selection.Select(new[] { 1f, -0.5f }, new Random(SEED)));
        }
    }
}

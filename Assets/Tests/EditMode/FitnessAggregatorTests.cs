using System;
using Assets.Logic.AI;
using NUnit.Framework;

namespace Assets.Tests.EditMode
{
    public class FitnessAggregatorTests
    {
        private const float TOLERANCE = 1e-5f;

        [Test]
        public void Combine_MixesAverageAndWorst()
        {
            // promedio 0.5, mínimo 0.2
            Assert.AreEqual(0.5f * 0.5f + 0.5f * 0.2f, FitnessAggregator.Combine(new[] { 0.2f, 0.5f, 0.8f }), TOLERANCE);
        }

        [Test]
        public void Combine_PrefersBalancedOverOneSidedWithTheSameAverage()
        {
            float balanced = FitnessAggregator.Combine(new[] { 0.5f, 0.5f });
            float oneSided = FitnessAggregator.Combine(new[] { 1f, 0f });

            Assert.Greater(balanced, oneSided);
        }

        [Test]
        public void Combine_WithZeroWeight_IsTheAverage()
        {
            Assert.AreEqual(0.5f, FitnessAggregator.Combine(new[] { 0f, 1f }, worstWeight: 0), TOLERANCE);
        }

        [Test]
        public void Combine_ClampsScoresToUnitRange()
        {
            Assert.AreEqual(1f, FitnessAggregator.Combine(new[] { 3f, 1f }), TOLERANCE);
            Assert.AreEqual(0f, FitnessAggregator.Combine(new[] { -1f }), TOLERANCE);
        }

        [Test]
        public void Combine_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentException>(() => FitnessAggregator.Combine(new float[0]));
            Assert.Throws<ArgumentException>(() => FitnessAggregator.Combine(new[] { 0.5f }, worstWeight: 1.5f));
        }
    }
}

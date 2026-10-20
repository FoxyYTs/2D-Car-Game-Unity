using Assets.Logic.AI;
using NUnit.Framework;

namespace Assets.Tests.EditMode
{
    public class ExamCriteriaTests
    {
        [Test]
        public void Failures_IsEmptyWhenEveryThresholdIsMet()
        {
            CollectionAssert.IsEmpty(ExamCriteria.Failures(0.5f, 0.5f, 0.4f, crashedStages: 2));
        }

        [Test]
        public void Failures_ListsEachMissedThreshold()
        {
            // The curriculum champion of the baseline: good at nothing in particular.
            var failures = ExamCriteria.Failures(0.44f, 0.41f, 0.25f, crashedStages: 0);

            Assert.AreEqual(3, failures.Count);
        }

        [Test]
        public void Failures_CountsCrashesOnTheirOwn()
        {
            var failures = ExamCriteria.Failures(1, 1, 1, crashedStages: 3);

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("crashed", failures[0]);
        }
    }
}

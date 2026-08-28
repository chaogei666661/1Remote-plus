using _1RM.Utils.Reachability;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Reachability
{
    [TestClass]
    public class ConnectionQualityTrackerTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        private static ConnectionQualityTracker WithLatencies(params int[] latencies)
        {
            var tracker = new ConnectionQualityTracker();
            foreach (var latency in latencies)
                tracker.Record(true, latency);
            return tracker;
        }

        [TestMethod]
        public void NothingRecordedIsNotAGradeOfAnyKind()
        {
            var snapshot = new ConnectionQualityTracker().Snapshot();

            Assert.AreEqual(EConnectionQuality.Unknown, snapshot.Quality);
            Assert.AreEqual(0, snapshot.SampleCount);
        }

        [TestMethod]
        public void AWindowOfNothingButFailuresHasNoLatencyToReportButFullLoss()
        {
            var tracker = new ConnectionQualityTracker();
            tracker.Record(false, 0);
            tracker.Record(false, 0);

            var snapshot = tracker.Snapshot();

            Assert.AreEqual(EConnectionQuality.Unknown, snapshot.Quality, "there is no link to grade");
            Assert.AreEqual(100, snapshot.LossPercent);
            Assert.AreEqual(0, snapshot.AverageLatencyMs);
        }

        [TestMethod]
        public void AFastSteadyLinkIsExcellent()
        {
            var snapshot = WithLatencies(8, 9, 10, 9, 8).Snapshot();

            Assert.AreEqual(EConnectionQuality.Excellent, snapshot.Quality);
            Assert.AreEqual(9, snapshot.AverageLatencyMs);
            Assert.AreEqual(0, snapshot.LossPercent);
        }

        [TestMethod]
        public void LatencyAloneIsEnoughToDowngrade()
        {
            Assert.AreEqual(EConnectionQuality.Good, WithLatencies(70, 70, 70).Snapshot().Quality);
            Assert.AreEqual(EConnectionQuality.Fair, WithLatencies(160, 160, 160).Snapshot().Quality);
            Assert.AreEqual(EConnectionQuality.Poor, WithLatencies(400, 400, 400).Snapshot().Quality);
        }

        [TestMethod]
        public void AFastLinkThatWillNotHoldStillIsNotExcellent()
        {
            // Same average as a 60 ms link, but every check lands somewhere else. That is what makes a
            // session feel unpredictable, and it is exactly what a green dot would hide.
            var snapshot = WithLatencies(5, 200, 5, 200, 5, 200).Snapshot();

            Assert.IsTrue(snapshot.JitterMs > 100, "the swing between checks is the point");
            Assert.AreEqual(EConnectionQuality.Poor, snapshot.Quality);
        }

        [TestMethod]
        public void JitterIsTheMeanChangeBetweenConsecutiveAnswers()
        {
            // 10 -> 20 -> 10 -> 20 : four differences of 10 apiece.
            var snapshot = WithLatencies(10, 20, 10, 20).Snapshot();

            Assert.AreEqual(10, snapshot.JitterMs);
        }

        [TestMethod]
        public void JitterIsNotGradedOnUntilThereAreEnoughAnswersForItToMeanSomething()
        {
            // Two samples always produce a jitter number; on its own it is an accident of when the sweep
            // happened to run, and grading on it would make a brand new server flicker.
            var snapshot = WithLatencies(5, 130).Snapshot();

            Assert.AreEqual(0, snapshot.JitterMs);
            Assert.AreEqual(EConnectionQuality.Good, snapshot.Quality, "graded on the 68 ms average alone");
        }

        [TestMethod]
        public void LossCountsAgainstALinkThatIsFastWhenItAnswers()
        {
            var tracker = new ConnectionQualityTracker();
            for (var i = 0; i < 8; i++) tracker.Record(true, 5);
            tracker.Record(false, 0);
            tracker.Record(false, 0);

            var snapshot = tracker.Snapshot();

            Assert.AreEqual(20, snapshot.LossPercent);
            Assert.AreEqual(5, snapshot.AverageLatencyMs, "failures do not drag the average of the answers");
            Assert.AreEqual(EConnectionQuality.Poor, snapshot.Quality);
        }

        [TestMethod]
        public void ASingleMissInATenCheckWindowIsOnlyFair()
        {
            var tracker = new ConnectionQualityTracker();
            for (var i = 0; i < 9; i++) tracker.Record(true, 5);
            tracker.Record(false, 0);

            Assert.AreEqual(EConnectionQuality.Fair, tracker.Snapshot().Quality);
        }

        [TestMethod]
        public void TheWindowSlidesSoARecoveredLinkStopsBeingReportedAsBroken()
        {
            var tracker = new ConnectionQualityTracker();
            for (var i = 0; i < ConnectionQualityTracker.WINDOW; i++) tracker.Record(false, 0);
            Assert.AreEqual(100, tracker.Snapshot().LossPercent);

            for (var i = 0; i < ConnectionQualityTracker.WINDOW; i++) tracker.Record(true, 5);

            var snapshot = tracker.Snapshot();
            Assert.AreEqual(0, snapshot.LossPercent, "the failures have aged out of the window");
            Assert.AreEqual(ConnectionQualityTracker.WINDOW, snapshot.SampleCount);
            Assert.AreEqual(EConnectionQuality.Excellent, snapshot.Quality);
        }

        [TestMethod]
        public void TheWindowNeverGrowsPastItsSize()
        {
            var tracker = new ConnectionQualityTracker();
            for (var i = 0; i < ConnectionQualityTracker.WINDOW * 3; i++) tracker.Record(true, 5);

            Assert.AreEqual(ConnectionQualityTracker.WINDOW, tracker.Snapshot().SampleCount);
        }

        [TestMethod]
        public void JitterIsMeasuredInTheOrderTheChecksHappenedEvenAfterTheWindowWraps()
        {
            var tracker = new ConnectionQualityTracker();
            // A link that climbs by a steady 10 ms a check, recorded past the end of the buffer so the
            // oldest surviving entry is no longer at index zero. Read in the order the checks happened,
            // every difference is 10; read from index zero, the wrap point shows up as one 90 ms jump.
            for (var i = 0; i < ConnectionQualityTracker.WINDOW + 5; i++) tracker.Record(true, i * 10);

            var snapshot = tracker.Snapshot();
            Assert.AreEqual(10, snapshot.JitterMs);
            Assert.AreEqual(95, snapshot.AverageLatencyMs, "the oldest five checks have aged out");
        }

        [TestMethod]
        public void ClearingForgetsTheWholeWindow()
        {
            var tracker = WithLatencies(5, 5, 5);
            tracker.Clear();

            var snapshot = tracker.Snapshot();
            Assert.AreEqual(0, snapshot.SampleCount);
            Assert.AreEqual(EConnectionQuality.Unknown, snapshot.Quality);
        }
    }
}

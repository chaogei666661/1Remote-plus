using System;

namespace _1RM.Utils.Reachability
{
    /// <summary>
    /// How good the link to a server has been over the last few checks, as opposed to whether the last one
    /// answered at all.
    /// </summary>
    public enum EConnectionQuality
    {
        /// <summary>Nothing has answered yet, so there is nothing to grade.</summary>
        Unknown = 0,

        Excellent = 1,
        Good = 2,
        Fair = 3,
        Poor = 4,
    }

    /// <summary>
    /// Turns the stream of single probes <see cref="ServerProbe"/> already produces into round-trip time,
    /// jitter and loss over a sliding window.
    ///
    /// It costs nothing extra on the wire. A quality reading normally means sending a burst of packets, but
    /// the reachability sweep is already opening one connection per server per interval, and a burst is the
    /// last thing to add to something that a corporate network is inclined to read as a port scan. Ten
    /// consecutive sweeps say the same thing over a longer baseline: whether the server is merely up, or
    /// actually pleasant to work on.
    ///
    /// Instances are written from the sweep's worker threads and read from the UI thread, so every member
    /// takes the lock.
    /// </summary>
    public sealed class ConnectionQualityTracker
    {
        /// <summary>
        /// Ten checks. At the default sixty-second interval that is the last ten minutes — long enough for
        /// one bad moment not to repaint the whole list, short enough that a link which recovers stops
        /// being reported as broken within a couple of sweeps.
        /// </summary>
        public const int WINDOW = 10;

        /// <summary>Jitter needs at least two round trips to differ from, and three to mean much.</summary>
        private const int MIN_SAMPLES_FOR_JITTER = 3;

        // Thresholds. The numbers are round-trip milliseconds to the port the session itself uses, so they
        // are about how a keystroke will feel rather than about how a network diagram looks: under 60 ms is
        // indistinguishable from local, 150 ms is where typing starts to lag behind the eye, and 300 ms is
        // where a shell becomes something you fight. Loss matters more than latency for a TCP session,
        // because every dropped segment costs a retransmit timeout on top of the round trip.
        private const int LATENCY_GOOD_MS = 60;
        private const int LATENCY_FAIR_MS = 150;
        private const int LATENCY_POOR_MS = 300;
        private const int JITTER_GOOD_MS = 20;
        private const int JITTER_FAIR_MS = 50;
        private const int JITTER_POOR_MS = 100;
        private const int LOSS_FAIR_PERCENT = 5;
        private const int LOSS_POOR_PERCENT = 20;

        private readonly object _lock = new object();
        private readonly bool[] _reachable = new bool[WINDOW];
        private readonly int[] _latency = new int[WINDOW];
        private int _next;
        private int _count;

        /// <summary>Adds the outcome of one probe, evicting the oldest once the window is full.</summary>
        public void Record(bool reachable, int latencyMs)
        {
            lock (_lock)
            {
                _reachable[_next] = reachable;
                _latency[_next] = reachable ? Math.Max(0, latencyMs) : 0;
                _next = (_next + 1) % WINDOW;
                if (_count < WINDOW) _count++;
            }
        }

        /// <summary>
        /// Forgets everything. Used when probing is switched off or a server stops being probed at all:
        /// history from before is about a different question.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _next = 0;
                _count = 0;
                Array.Clear(_reachable, 0, WINDOW);
                Array.Clear(_latency, 0, WINDOW);
            }
        }

        public int SampleCount
        {
            get { lock (_lock) return _count; }
        }

        /// <summary>
        /// Everything at once, read under a single lock — the four numbers have to describe the same window
        /// or a tooltip can end up saying "0% loss" next to a latency averaged over a different set.
        /// </summary>
        public ConnectionQualitySnapshot Snapshot()
        {
            lock (_lock)
            {
                if (_count == 0)
                    return new ConnectionQualitySnapshot(EConnectionQuality.Unknown, 0, 0, 0, 0);

                // Oldest first, so consecutive differences are consecutive in time.
                var ordered = new int[_count];
                var reachable = new bool[_count];
                var start = _count < WINDOW ? 0 : _next;
                for (var i = 0; i < _count; i++)
                {
                    var index = (start + i) % WINDOW;
                    ordered[i] = _latency[index];
                    reachable[i] = _reachable[index];
                }

                var successes = 0;
                long total = 0;
                for (var i = 0; i < _count; i++)
                {
                    if (!reachable[i]) continue;
                    successes++;
                    total += ordered[i];
                }

                var lossPercent = (int)Math.Round((_count - successes) * 100.0 / _count, MidpointRounding.AwayFromZero);
                if (successes == 0)
                    return new ConnectionQualitySnapshot(EConnectionQuality.Unknown, 0, 0, lossPercent, _count);

                var average = (int)Math.Round((double)total / successes, MidpointRounding.AwayFromZero);

                // Mean absolute difference between consecutive successful round trips. Not RFC 3550's
                // smoothed estimate — that assumes a packet stream, and these are minutes apart — but it
                // answers the same question: does the link hold still, or does each check land somewhere
                // else?
                var jitter = 0;
                if (successes >= MIN_SAMPLES_FOR_JITTER)
                {
                    long deltas = 0;
                    var pairs = 0;
                    var previous = -1;
                    for (var i = 0; i < _count; i++)
                    {
                        if (!reachable[i]) continue;
                        if (previous >= 0)
                        {
                            deltas += Math.Abs(ordered[i] - previous);
                            pairs++;
                        }
                        previous = ordered[i];
                    }
                    if (pairs > 0) jitter = (int)Math.Round((double)deltas / pairs, MidpointRounding.AwayFromZero);
                }

                return new ConnectionQualitySnapshot(Grade(average, jitter, lossPercent, successes), average, jitter, lossPercent, _count);
            }
        }

        private static EConnectionQuality Grade(int averageMs, int jitterMs, int lossPercent, int successes)
        {
            if (lossPercent >= LOSS_POOR_PERCENT || averageMs >= LATENCY_POOR_MS) return EConnectionQuality.Poor;
            if (lossPercent >= LOSS_FAIR_PERCENT || averageMs >= LATENCY_FAIR_MS) return EConnectionQuality.Fair;

            // Jitter is only believable once there are enough round trips to have varied; with one or two
            // samples it is either zero or an accident, and grading on it would flicker.
            if (successes >= MIN_SAMPLES_FOR_JITTER)
            {
                if (jitterMs >= JITTER_POOR_MS) return EConnectionQuality.Poor;
                if (jitterMs >= JITTER_FAIR_MS) return EConnectionQuality.Fair;
                if (jitterMs >= JITTER_GOOD_MS) return EConnectionQuality.Good;
            }

            return averageMs >= LATENCY_GOOD_MS ? EConnectionQuality.Good : EConnectionQuality.Excellent;
        }
    }

    /// <summary>One consistent reading of a <see cref="ConnectionQualityTracker"/>.</summary>
    public readonly struct ConnectionQualitySnapshot
    {
        public EConnectionQuality Quality { get; }

        /// <summary>Mean round trip over the checks that answered, in milliseconds.</summary>
        public int AverageLatencyMs { get; }

        /// <summary>Mean absolute change between consecutive answers, in milliseconds.</summary>
        public int JitterMs { get; }

        /// <summary>Share of the window that did not answer at all.</summary>
        public int LossPercent { get; }

        /// <summary>How many checks the numbers above are drawn from.</summary>
        public int SampleCount { get; }

        public ConnectionQualitySnapshot(EConnectionQuality quality, int averageLatencyMs, int jitterMs, int lossPercent, int sampleCount)
        {
            Quality = quality;
            AverageLatencyMs = averageLatencyMs;
            JitterMs = jitterMs;
            LossPercent = lossPercent;
            SampleCount = sampleCount;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeStream.Core.Services;

/// <summary>
/// Adaptive refresh strategy that predicts optimal polling times based on observed data patterns.
/// Designed for sources with regular but not perfectly predictable update intervals.
/// </summary>
public class AdaptiveRefreshStrategy
{
    private readonly object _lock = new object();
    private readonly Queue<TimeSpan> _observedIntervals = new Queue<TimeSpan>();
    private readonly int _maxObservations;

    private DateTime? _lastDataTimestamp;
    private DateTime? _lastCheckTime;
    private int _consecutiveMisses;
    private TimeSpan _currentSlack;

    /// <summary>
    /// Creates a new adaptive refresh strategy.
    /// </summary>
    /// <param name="baseInterval">Expected interval between data updates (e.g., 6 minutes for BOM radar).</param>
    /// <param name="initialSlack">Initial slack time to add after expected update time.</param>
    /// <param name="minimumInterval">Minimum time between polls (safety floor).</param>
    /// <param name="maximumInterval">Maximum time between polls (ceiling).</param>
    /// <param name="retryInterval">Short interval to use when retrying after a miss.</param>
    /// <param name="maxRetries">Maximum retry attempts before waiting for next cycle.</param>
    /// <param name="maxObservations">Number of observations to keep for averaging.</param>
    public AdaptiveRefreshStrategy(
        TimeSpan baseInterval,
        TimeSpan initialSlack,
        TimeSpan minimumInterval,
        TimeSpan maximumInterval,
        TimeSpan retryInterval,
        int maxRetries = 3,
        int maxObservations = 10)
    {
        BaseInterval = baseInterval;
        MinimumInterval = minimumInterval;
        MaximumInterval = maximumInterval;
        RetryInterval = retryInterval;
        MaxRetries = maxRetries;
        _maxObservations = maxObservations;
        _currentSlack = initialSlack;
    }

    /// <summary>
    /// Base expected interval between data updates.
    /// </summary>
    public TimeSpan BaseInterval { get; }

    /// <summary>
    /// Minimum time between polls (safety floor to avoid flooding).
    /// </summary>
    public TimeSpan MinimumInterval { get; }

    /// <summary>
    /// Maximum time between polls.
    /// </summary>
    public TimeSpan MaximumInterval { get; }

    /// <summary>
    /// Short interval to use when retrying after a miss.
    /// </summary>
    public TimeSpan RetryInterval { get; }

    /// <summary>
    /// Maximum retry attempts before waiting for next full cycle.
    /// </summary>
    public int MaxRetries { get; }

    /// <summary>
    /// Current adaptive slack being applied.
    /// </summary>
    public TimeSpan CurrentSlack
    {
        get { lock (_lock) return _currentSlack; }
    }

    /// <summary>
    /// Number of consecutive misses (data not yet available).
    /// </summary>
    public int ConsecutiveMisses
    {
        get { lock (_lock) return _consecutiveMisses; }
    }

    /// <summary>
    /// Timestamp of the last successfully retrieved data.
    /// </summary>
    public DateTime? LastDataTimestamp
    {
        get { lock (_lock) return _lastDataTimestamp; }
    }

    /// <summary>
    /// Average observed interval between data updates.
    /// </summary>
    public TimeSpan AverageObservedInterval
    {
        get
        {
            lock (_lock)
            {
                if (_observedIntervals.Count == 0)
                    return BaseInterval;

                var avgTicks = _observedIntervals.Average(ts => ts.Ticks);
                return TimeSpan.FromTicks((long)avgTicks);
            }
        }
    }

    /// <summary>
    /// Records a successful data fetch with the timestamp from the data.
    /// </summary>
    /// <param name="dataTimestamp">Timestamp of the data (from the source, e.g., filename).</param>
    public void RecordSuccess(DateTime dataTimestamp)
    {
        lock (_lock)
        {
            if (_lastDataTimestamp.HasValue && dataTimestamp > _lastDataTimestamp.Value)
            {
                var interval = dataTimestamp - _lastDataTimestamp.Value;

                // Only record reasonable intervals (not catchup scenarios)
                if (interval >= MinimumInterval && interval <= MaximumInterval * 2)
                {
                    _observedIntervals.Enqueue(interval);

                    while (_observedIntervals.Count > _maxObservations)
                        _observedIntervals.Dequeue();

                    // Adapt slack based on observations
                    AdaptSlack();
                }
            }

            _lastDataTimestamp = dataTimestamp;
            _lastCheckTime = DateTime.UtcNow;
            _consecutiveMisses = 0;
        }
    }

    /// <summary>
    /// Records a miss (data not yet available).
    /// </summary>
    public void RecordMiss()
    {
        lock (_lock)
        {
            _consecutiveMisses++;
            _lastCheckTime = DateTime.UtcNow;

            // If we're consistently missing, increase slack
            if (_consecutiveMisses >= MaxRetries)
            {
                _currentSlack = TimeSpan.FromTicks(Math.Min(
                    (long)(_currentSlack.Ticks * 1.2),
                    MaximumInterval.Ticks / 2));
            }
        }
    }

    /// <summary>
    /// Calculates the next time to check for data.
    /// </summary>
    /// <returns>When to next poll for data.</returns>
    public DateTime GetNextCheckTime()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            // If we're in retry mode, use short interval
            if (_consecutiveMisses > 0 && _consecutiveMisses < MaxRetries)
            {
                var retryTime = now.Add(RetryInterval);
                return retryTime;
            }

            // Calculate next expected data time
            DateTime nextExpected;
            if (_lastDataTimestamp.HasValue)
            {
                // Use observed average if we have enough data
                var interval = _observedIntervals.Count >= 3
                    ? AverageObservedInterval
                    : BaseInterval;

                nextExpected = _lastDataTimestamp.Value.Add(interval).Add(_currentSlack);
            }
            else
            {
                // No data yet, check soon
                nextExpected = now.Add(MinimumInterval);
            }

            // Ensure we don't poll too soon
            if (nextExpected < now.Add(MinimumInterval))
            {
                nextExpected = now.Add(MinimumInterval);
            }

            // Ensure we don't wait too long
            if (nextExpected > now.Add(MaximumInterval))
            {
                nextExpected = now.Add(MaximumInterval);
            }

            return nextExpected;
        }
    }

    /// <summary>
    /// Gets the delay until the next check.
    /// </summary>
    public TimeSpan GetDelayUntilNextCheck()
    {
        var nextCheck = GetNextCheckTime();
        var delay = nextCheck - DateTime.UtcNow;

        // Ensure positive delay with minimum floor
        if (delay < MinimumInterval)
            delay = MinimumInterval;

        return delay;
    }

    /// <summary>
    /// Whether we should retry (still within retry attempts).
    /// </summary>
    public bool ShouldRetry
    {
        get { lock (_lock) return _consecutiveMisses > 0 && _consecutiveMisses < MaxRetries; }
    }

    private void AdaptSlack()
    {
        if (_observedIntervals.Count < 3)
            return;

        // Calculate standard deviation to understand variability
        var avg = AverageObservedInterval.TotalSeconds;
        var variance = _observedIntervals.Average(ts => Math.Pow(ts.TotalSeconds - avg, 2));
        var stdDev = Math.Sqrt(variance);

        // Set slack to ~1 standard deviation, with bounds
        var newSlackSeconds = Math.Max(15, Math.Min(120, stdDev * 1.5 + 15));
        _currentSlack = TimeSpan.FromSeconds(newSlackSeconds);
    }

    /// <summary>
    /// Resets the strategy to initial state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _observedIntervals.Clear();
            _lastDataTimestamp = null;
            _lastCheckTime = null;
            _consecutiveMisses = 0;
        }
    }
}

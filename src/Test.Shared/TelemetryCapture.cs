namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;

    /// <summary>
    /// In-memory capture of metrics and spans using only the base class library listeners, the same way any host
    /// (Radiant, the OpenTelemetry SDK) subscribes.  Captures every meter and activity source whose name is in the
    /// subscription list.  Spans are recorded when they stop.  Thread safe.
    /// </summary>
    public sealed class TelemetryCapture : IDisposable
    {
        /// <summary>
        /// Name of the activity source tests use to open a parent span, so a test can pick out its own trace.
        /// </summary>
        public const string TestSourceName = "CrawlSharp.Tests";

        private static readonly ActivitySource _TestSource = new ActivitySource(TestSourceName);

        private readonly HashSet<string> _Names;
        private readonly MeterListener _MeterListener;
        private readonly ActivityListener _ActivityListener;
        private readonly object _Lock = new object();
        private readonly List<RecordedMeasurement> _Measurements = new List<RecordedMeasurement>();
        private readonly List<Activity> _Spans = new List<Activity>();
        private bool _Disposed = false;

        /// <summary>
        /// Start capturing the named meters and activity sources.  The test source is always included.
        /// </summary>
        public TelemetryCapture(params string[] names)
        {
            _Names = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.Ordinal) { TestSourceName };

            _MeterListener = new MeterListener();
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (_Names.Contains(instrument.Meter.Name)) listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Record(instrument, value, tags));
            _MeterListener.SetMeasurementEventCallback<int>((instrument, value, tags, state) => Record(instrument, value, tags));
            _MeterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => Record(instrument, value, tags));
            _MeterListener.Start();

            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = source => _Names.Contains(source.Name),
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_Lock)
                    {
                        _Spans.Add(activity);
                    }
                }
            };

            ActivitySource.AddActivityListener(_ActivityListener);
        }

        /// <summary>
        /// Start a parent span on the test source; spans started under it share its trace id.
        /// </summary>
        public static Activity StartTestSpan(string name)
        {
            return _TestSource.StartActivity(name, ActivityKind.Internal);
        }

        /// <summary>
        /// Read every observable instrument (gauges) now.
        /// </summary>
        public void CollectObservables()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// Every measurement captured for an instrument, optionally filtered by labels.
        /// </summary>
        public List<RecordedMeasurement> Measurements(string instrument, IDictionary<string, string> tags = null)
        {
            lock (_Lock)
            {
                return _Measurements.Where(m => m.Instrument == instrument && m.Matches(tags)).ToList();
            }
        }

        /// <summary>
        /// Sum of every measurement captured for an instrument, optionally filtered by labels.
        /// For counters this is the total; for up-down counters it is the net change.
        /// </summary>
        public double Sum(string instrument, IDictionary<string, string> tags = null)
        {
            return Measurements(instrument, tags).Sum(m => m.Value);
        }

        /// <summary>
        /// Number of measurements captured for an instrument, optionally filtered by labels.
        /// For histograms this is the number of recordings.
        /// </summary>
        public int Count(string instrument, IDictionary<string, string> tags = null)
        {
            return Measurements(instrument, tags).Count;
        }

        /// <summary>
        /// Every stopped span, optionally restricted to one trace.
        /// </summary>
        public List<Activity> Spans(ActivityTraceId? traceId = null)
        {
            lock (_Lock)
            {
                return _Spans.Where(a => traceId == null || a.TraceId == traceId.Value).ToList();
            }
        }

        /// <summary>
        /// Stopped spans with the given name, optionally restricted to one trace.
        /// </summary>
        public List<Activity> Spans(string name, ActivityTraceId? traceId = null)
        {
            return Spans(traceId).Where(a => a.OperationName == name || a.DisplayName == name).ToList();
        }

        /// <summary>
        /// Stop capturing.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _MeterListener.Dispose();
            _ActivityListener.Dispose();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            RecordedMeasurement measurement = new RecordedMeasurement
            {
                Instrument = instrument.Name,
                Value = value
            };

            foreach (KeyValuePair<string, object> tag in tags)
            {
                measurement.Tags[tag.Key] = tag.Value != null ? Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
            }

            lock (_Lock)
            {
                _Measurements.Add(measurement);
            }
        }
    }
}

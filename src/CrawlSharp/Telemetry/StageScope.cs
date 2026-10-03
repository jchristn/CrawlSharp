namespace CrawlSharp.Telemetry
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Times one pipeline stage: opens a "stage:&lt;name&gt;" span and, on dispose, records the stage duration histogram
    /// and event counter with the stage's outcome and sets the span status.  The outcome defaults to success.
    /// Best-effort; never throws.  Not thread safe: use one scope per stage execution on one logical flow.
    /// </summary>
    internal sealed class StageScope : IDisposable
    {
        #region Internal-Members

        /// <summary>
        /// The stage span, or null when nothing listens.
        /// </summary>
        internal Activity Activity
        {
            get
            {
                return _Activity;
            }
        }

        /// <summary>
        /// The outcome that will be recorded.
        /// </summary>
        internal string Outcome
        {
            get
            {
                return _Outcome;
            }
        }

        #endregion

        #region Private-Members

        private readonly string _Stage;
        private readonly long _StartTimestamp;
        private readonly Activity _Activity;
        private readonly Activity _Previous;
        private string _Outcome = CrawlSharpTelemetry.OutcomeSuccess;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start a stage.
        /// </summary>
        /// <param name="stage">Stage name, one of the CrawlSharpTelemetry stage constants.</param>
        /// <param name="parent">Explicit parent context, or default to nest under <see cref="Activity.Current"/>.</param>
        internal StageScope(string stage, ActivityContext parent = default)
        {
            _Stage = stage;
            _StartTimestamp = Stopwatch.GetTimestamp();
            _Previous = Activity.Current;
            _Activity = CrawlInstruments.StartActivity(CrawlSharpTelemetry.SpanStagePrefix + stage, ActivityKind.Internal, parent);
            CrawlInstruments.SetTag(_Activity, CrawlSharpTelemetry.AttributeStage, stage);
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Set the outcome recorded when the stage ends.
        /// </summary>
        internal void SetOutcome(string outcome)
        {
            if (!String.IsNullOrEmpty(outcome)) _Outcome = outcome;
        }

        /// <summary>
        /// Set a span tag.
        /// </summary>
        internal void SetTag(string key, object value)
        {
            CrawlInstruments.SetTag(_Activity, key, value);
        }

        /// <summary>
        /// Mark the stage failed: records the error counter, marks the span as an error and attaches the exception.
        /// A cancellation is recorded as cancelled rather than failed.
        /// </summary>
        internal void Fail(Exception e)
        {
            if (e is OperationCanceledException)
            {
                _Outcome = CrawlSharpTelemetry.OutcomeCancelled;
                return;
            }

            _Outcome = CrawlSharpTelemetry.OutcomeFailure;
            CrawlInstruments.RecordError(_Stage, e);
            CrawlInstruments.SetError(_Activity, e);
        }

        /// <summary>
        /// End the stage and record it.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            CrawlInstruments.RecordStage(_Stage, _Outcome, CrawlInstruments.ElapsedSeconds(_StartTimestamp));
            CrawlInstruments.SetOutcomeStatus(_Activity, _Outcome);
            CrawlInstruments.StopActivity(_Activity, _Previous);
        }

        #endregion
    }
}

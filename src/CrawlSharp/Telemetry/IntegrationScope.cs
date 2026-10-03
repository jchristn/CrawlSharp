namespace CrawlSharp.Telemetry
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    /// <summary>
    /// Times one outbound call: opens a client span named "&lt;service&gt; &lt;operation&gt;" and, on dispose, records the
    /// integration request counter and latency histogram with the call's outcome.  The outcome defaults to failure, so a
    /// call that exits without reporting a result is never counted as a success.
    /// Best-effort; never throws.  Not thread safe: use one scope per call.
    /// </summary>
    internal sealed class IntegrationScope : IDisposable
    {
        #region Internal-Members

        /// <summary>
        /// The client span, or null when nothing listens.
        /// </summary>
        internal Activity Activity
        {
            get
            {
                return _Activity;
            }
        }

        #endregion

        #region Private-Members

        private readonly string _Service;
        private readonly string _Operation;
        private readonly long _StartTimestamp;
        private readonly Activity _Activity;
        private readonly Activity _Previous;
        private string _Outcome = CrawlSharpTelemetry.OutcomeFailure;
        private Exception _Exception = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start an outbound call.
        /// </summary>
        /// <param name="service">Integration service, one of the CrawlSharpTelemetry service constants.</param>
        /// <param name="operation">Operation, for example an HTTP method or a Playwright operation constant.</param>
        internal IntegrationScope(string service, string operation)
        {
            _Service = service;
            _Operation = operation;
            _StartTimestamp = Stopwatch.GetTimestamp();
            _Previous = Activity.Current;
            _Activity = CrawlInstruments.StartActivity(service + " " + operation, ActivityKind.Client);
            CrawlInstruments.SetTag(_Activity, CrawlSharpTelemetry.AttributeIntegrationService, service);
            CrawlInstruments.SetTag(_Activity, CrawlSharpTelemetry.AttributeIntegrationOperation, operation);
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Set the outcome recorded when the call ends.
        /// </summary>
        internal void SetOutcome(string outcome)
        {
            if (!String.IsNullOrEmpty(outcome)) _Outcome = outcome;
        }

        /// <summary>
        /// Record the HTTP status of the response and derive the outcome from it.
        /// </summary>
        internal void SetStatus(int status)
        {
            CrawlInstruments.SetTag(_Activity, CrawlSharpTelemetry.AttributeHttpStatusCode, status);
            _Outcome = CrawlInstruments.OutcomeForStatus(status);
        }

        /// <summary>
        /// Set a span tag.
        /// </summary>
        internal void SetTag(string key, object value)
        {
            CrawlInstruments.SetTag(_Activity, key, value);
        }

        /// <summary>
        /// Mark the call failed with an exception; timeouts and cancellations get their own outcomes.
        /// </summary>
        internal void Fail(Exception e, CancellationToken token = default)
        {
            _Exception = e;
            _Outcome = CrawlInstruments.OutcomeForException(e, token);
            if (_Outcome != CrawlSharpTelemetry.OutcomeCancelled) CrawlInstruments.SetError(_Activity, e);
        }

        /// <summary>
        /// End the call and record it.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            CrawlInstruments.RecordIntegration(_Service, _Operation, _Outcome, CrawlInstruments.ElapsedSeconds(_StartTimestamp), _Exception);

            // OpenTelemetry marks client spans with a 4xx response as errors (server spans leave 4xx unset).
            if (_Activity != null && (_Outcome == CrawlSharpTelemetry.OutcomeClientError || _Outcome == CrawlSharpTelemetry.OutcomeThrottled))
            {
                try { _Activity.SetStatus(ActivityStatusCode.Error, _Outcome); } catch (Exception) { }
            }

            CrawlInstruments.SetOutcomeStatus(_Activity, _Outcome);
            CrawlInstruments.StopActivity(_Activity, _Previous);
        }

        #endregion
    }
}

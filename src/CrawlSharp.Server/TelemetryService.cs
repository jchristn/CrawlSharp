namespace CrawlSharp.Server
{
    using System;
    using CrawlSharp.Telemetry;
    using Microsoft.Extensions.Logging;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// Owns the process's single Radiant host: subscribes to the Watson, CrawlSharp and CrawlSharp.Server meters and activity
    /// sources, exports metrics and traces over OTLP, serves the Prometheus scrape endpoint, and ships logs to Loki.
    /// All operations are best-effort: if the host cannot start, the server runs without telemetry rather than failing.
    /// Thread safe after construction.
    /// </summary>
    public class TelemetryService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Whether a live Radiant host is running.
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                return _Host != null && _Host.IsEnabled;
            }
        }

        /// <summary>
        /// Whether debug-level log records are exported; when false, callers skip building debug messages for export.
        /// </summary>
        public bool IsDebugEnabled
        {
            get
            {
                return _Logger != null && _Logger.IsEnabled(LogLevel.Debug);
            }
        }

        /// <summary>
        /// The Radiant settings the host was started with, or null when telemetry is disabled or failed to start.
        /// </summary>
        public RadiantSettings RadiantSettings
        {
            get
            {
                return _RadiantSettings;
            }
        }

        #endregion

        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly RadiantHost _Host = null;
        private readonly RadiantSettings _RadiantSettings = null;
        private readonly ILogger _Logger = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start telemetry from settings.  Never throws on a telemetry failure; the failure is logged as a warning.
        /// </summary>
        /// <param name="settings">Telemetry settings; null or disabled starts nothing.</param>
        /// <param name="logging">Logging module, used to report the telemetry state; may be null.</param>
        public TelemetryService(TelemetrySettings settings, LoggingModule logging)
        {
            _Logging = logging;
            ServerInstruments.SetConfig(settings);

            if (settings == null || !settings.Enabled)
            {
                _Logging?.Info("[TelemetryService] telemetry disabled by settings");
                return;
            }

            try
            {
                _RadiantSettings = BuildRadiantSettings(settings);
                _Host = RadiantHost.Start(_RadiantSettings);
                _Logger = _Host.CreateLogger("CrawlSharp.Server");

                _Logging?.Info("[TelemetryService] telemetry enabled for " + settings.ServiceName
                    + (settings.OtlpEnabled ? ", OTLP " + settings.OtlpProtocol + " to " + settings.OtlpEndpoint : ", OTLP off")
                    + (settings.PrometheusEnabled ? ", Prometheus at " + _RadiantSettings.Prometheus.ToScrapeUrl() : ", Prometheus off")
                    + (settings.LokiEnabled ? ", Loki at " + settings.LokiEndpoint : ", Loki off"));
            }
            catch (Exception e)
            {
                try { _Host?.Dispose(); } catch (Exception) { }
                _Host = null;
                _Logger = null;
                Exception root = e.GetBaseException();
                _Logging?.Warn("[TelemetryService] telemetry disabled (init failed): " + e.Message
                    + (ReferenceEquals(root, e) ? "" : " Cause: " + root.GetType().FullName + ": " + root.Message));
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the Radiant settings for the given telemetry settings, subscribed to every meter and activity source in the process.
        /// </summary>
        /// <param name="settings">Telemetry settings.</param>
        /// <returns>Radiant settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static RadiantSettings BuildRadiantSettings(TelemetrySettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            RadiantSettings radiant = new RadiantSettings(settings.ServiceName);
            radiant.Enable = settings.Enabled;

            radiant.Sources.AddMeter("Watson");
            radiant.Sources.AddActivitySource("Watson");
            radiant.Sources.AddMeter(CrawlSharpTelemetry.MeterName);
            radiant.Sources.AddActivitySource(CrawlSharpTelemetry.ActivitySourceName);
            radiant.Sources.AddMeter(CrawlSharpTelemetry.ServerMeterName);
            radiant.Sources.AddActivitySource(CrawlSharpTelemetry.ServerActivitySourceName);

            radiant.Otlp.Enable = settings.OtlpEnabled;
            radiant.Otlp.Endpoint = settings.OtlpEndpoint;
            radiant.Otlp.Protocol = settings.OtlpProtocol == "httpprotobuf" ? OtlpProtocolEnum.HttpProtobuf : OtlpProtocolEnum.Grpc;

            radiant.Prometheus.Enable = settings.PrometheusEnabled;
            radiant.Prometheus.Hostname = settings.PrometheusHostname;
            radiant.Prometheus.Port = settings.PrometheusPort;
            radiant.Prometheus.Path = "/metrics";

            radiant.Loki.Enable = settings.LokiEnabled;
            radiant.Loki.Endpoint = settings.LokiEndpoint;
            radiant.Loki.MinimumSeverity = settings.LogMinimumSeverity;

            radiant.Traces.SamplingRatio = settings.TracesSamplingRatio;
            radiant.Traces.PropagateContext = true;
            radiant.Logs.MinimumSeverity = settings.LogMinimumSeverity;
            radiant.Metrics.IncludeRuntime = true;

            return radiant;
        }

        /// <summary>
        /// Export a debug log record.  Best-effort.
        /// </summary>
        /// <param name="message">Message.</param>
        public void Debug(string message)
        {
            Write(LogLevel.Debug, message);
        }

        /// <summary>
        /// Export an information log record.  Best-effort.
        /// </summary>
        /// <param name="message">Message.</param>
        public void Info(string message)
        {
            Write(LogLevel.Information, message);
        }

        /// <summary>
        /// Export a warning log record.  Best-effort.
        /// </summary>
        /// <param name="message">Message.</param>
        public void Warn(string message)
        {
            Write(LogLevel.Warning, message);
        }

        /// <summary>
        /// Flush and stop the Radiant host.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Flush and stop the Radiant host.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;

            if (disposing)
            {
                try { _Host?.Dispose(); } catch (Exception) { }
            }
        }

        private void Write(LogLevel level, string message)
        {
            if (_Logger == null || String.IsNullOrEmpty(message)) return;

            try
            {
                if (_Logger.IsEnabled(level)) _Logger.Log(level, "{Message}", message);
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}

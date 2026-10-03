namespace CrawlSharp.Server
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Telemetry settings for the CrawlSharp server: where Radiant exports metrics, traces and logs.
    /// Modeled on Pneuma's TelemetrySettings.  Loopback defaults use 127.0.0.1 rather than localhost, because on Windows
    /// localhost resolves to IPv6 ::1 first and stalls before falling back.
    /// <para>
    /// Every property can be set from an environment variable (see <see cref="FromEnvironment"/>); the variable names are
    /// the constants on this class.  Not thread safe; configure once at startup.
    /// </para>
    /// </summary>
    public class TelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Environment variable for <see cref="Enabled"/>.
        /// </summary>
        public const string EnabledVariable = "CRAWLSHARP_TELEMETRY_ENABLED";

        /// <summary>
        /// Environment variable for <see cref="ServiceName"/>.
        /// </summary>
        public const string ServiceNameVariable = "CRAWLSHARP_TELEMETRY_SERVICE_NAME";

        /// <summary>
        /// Environment variable for <see cref="OtlpEnabled"/>.
        /// </summary>
        public const string OtlpEnabledVariable = "CRAWLSHARP_OTLP_ENABLED";

        /// <summary>
        /// Environment variable for <see cref="OtlpEndpoint"/>.
        /// </summary>
        public const string OtlpEndpointVariable = "CRAWLSHARP_OTLP_ENDPOINT";

        /// <summary>
        /// Environment variable for <see cref="OtlpProtocol"/>.
        /// </summary>
        public const string OtlpProtocolVariable = "CRAWLSHARP_OTLP_PROTOCOL";

        /// <summary>
        /// Environment variable for <see cref="PrometheusEnabled"/>.
        /// </summary>
        public const string PrometheusEnabledVariable = "CRAWLSHARP_PROMETHEUS_ENABLED";

        /// <summary>
        /// Environment variable for <see cref="PrometheusHostname"/>.
        /// </summary>
        public const string PrometheusHostnameVariable = "CRAWLSHARP_PROMETHEUS_HOSTNAME";

        /// <summary>
        /// Environment variable for <see cref="PrometheusPort"/>.
        /// </summary>
        public const string PrometheusPortVariable = "CRAWLSHARP_PROMETHEUS_PORT";

        /// <summary>
        /// Environment variable for <see cref="LokiEnabled"/>.
        /// </summary>
        public const string LokiEnabledVariable = "CRAWLSHARP_LOKI_ENABLED";

        /// <summary>
        /// Environment variable for <see cref="LokiEndpoint"/>.
        /// </summary>
        public const string LokiEndpointVariable = "CRAWLSHARP_LOKI_ENDPOINT";

        /// <summary>
        /// Environment variable for <see cref="TracesSamplingRatio"/>.
        /// </summary>
        public const string TracesSamplingRatioVariable = "CRAWLSHARP_TRACES_SAMPLING_RATIO";

        /// <summary>
        /// Environment variable for <see cref="LogMinimumSeverity"/>.
        /// </summary>
        public const string LogMinimumSeverityVariable = "CRAWLSHARP_LOG_MINIMUM_SEVERITY";

        /// <summary>
        /// Whether telemetry is enabled.  When false, no Radiant host starts and nothing is exported.  Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Service name stamped as service.name on every signal.  Default "crawlsharp-server".  Cannot be null or empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string ServiceName
        {
            get
            {
                return _ServiceName;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ServiceName));
                _ServiceName = value.Trim();
            }
        }

        /// <summary>
        /// Whether metrics and traces are pushed over OTLP (to Tempo or an OpenTelemetry Collector).  Default true.
        /// </summary>
        public bool OtlpEnabled { get; set; } = true;

        /// <summary>
        /// OTLP endpoint.  Use the gRPC port (4317) with the "grpc" protocol, or the HTTP port (4318) with "httpprotobuf".
        /// Default "http://127.0.0.1:4317".  Must be an absolute URI.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the value is not an absolute URI.</exception>
        public string OtlpEndpoint
        {
            get
            {
                return _OtlpEndpoint;
            }
            set
            {
                _OtlpEndpoint = ValidateUri(value, nameof(OtlpEndpoint));
            }
        }

        /// <summary>
        /// OTLP protocol, "grpc" or "httpprotobuf".  Default "grpc".
        /// </summary>
        /// <exception cref="ArgumentException">Thrown for any other value.</exception>
        public string OtlpProtocol
        {
            get
            {
                return _OtlpProtocol;
            }
            set
            {
                string protocol = value != null ? value.Trim().ToLowerInvariant() : null;
                if (protocol != "grpc" && protocol != "httpprotobuf")
                    throw new ArgumentException("OtlpProtocol must be 'grpc' or 'httpprotobuf', not '" + value + "'.", nameof(OtlpProtocol));
                _OtlpProtocol = protocol;
            }
        }

        /// <summary>
        /// Whether the in-process Prometheus scrape endpoint is served.  It exposes every subscribed meter (Watson, CrawlSharp,
        /// CrawlSharp.Server and .NET runtime metrics).  Default true.
        /// </summary>
        public bool PrometheusEnabled { get; set; } = true;

        /// <summary>
        /// Hostname the Prometheus endpoint binds and answers to.  Default "127.0.0.1".  In a container, set it to the container's
        /// hostname (for example "crawlsharp-server") so other containers can scrape it; Radiant 0.1.2 rejects "*" and "+",
        /// and the HTTP listener rejects "0.0.0.0".  Cannot be null or empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string PrometheusHostname
        {
            get
            {
                return _PrometheusHostname;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(PrometheusHostname));
                _PrometheusHostname = value.Trim();
            }
        }

        /// <summary>
        /// Port the Prometheus endpoint binds; metrics are served at /metrics.  Default 9464.  Minimum 1, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside 1..65535.</exception>
        public int PrometheusPort
        {
            get
            {
                return _PrometheusPort;
            }
            set
            {
                if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(PrometheusPort), "PrometheusPort must be between 1 and 65535.");
                _PrometheusPort = value;
            }
        }

        /// <summary>
        /// Whether logs are pushed directly to Loki.  Default false.
        /// </summary>
        public bool LokiEnabled { get; set; } = false;

        /// <summary>
        /// Loki OTLP base endpoint; "/v1/logs" is appended.  Default "http://127.0.0.1:3100/otlp".  Must be an absolute URI.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the value is not an absolute URI.</exception>
        public string LokiEndpoint
        {
            get
            {
                return _LokiEndpoint;
            }
            set
            {
                _LokiEndpoint = ValidateUri(value, nameof(LokiEndpoint));
            }
        }

        /// <summary>
        /// Head-based trace sampling ratio.  Default 1.0 (every trace).  Minimum 0.0, maximum 1.0.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside 0..1.</exception>
        public double TracesSamplingRatio
        {
            get
            {
                return _TracesSamplingRatio;
            }
            set
            {
                if (Double.IsNaN(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(TracesSamplingRatio), "TracesSamplingRatio must be between 0 and 1.");
                _TracesSamplingRatio = value;
            }
        }

        /// <summary>
        /// Minimum severity of exported logs, 0 (trace) to 7 (none).  1 is debug and includes the crawler's per-URL log lines;
        /// 2 is information.  Default 2.  Minimum 0, maximum 7.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside 0..7.</exception>
        public int LogMinimumSeverity
        {
            get
            {
                return _LogMinimumSeverity;
            }
            set
            {
                if (value < 0 || value > 7) throw new ArgumentOutOfRangeException(nameof(LogMinimumSeverity), "LogMinimumSeverity must be between 0 and 7.");
                _LogMinimumSeverity = value;
            }
        }

        #endregion

        #region Private-Members

        private string _ServiceName = "crawlsharp-server";
        private string _OtlpEndpoint = "http://127.0.0.1:4317";
        private string _OtlpProtocol = "grpc";
        private string _PrometheusHostname = "127.0.0.1";
        private int _PrometheusPort = 9464;
        private string _LokiEndpoint = "http://127.0.0.1:3100/otlp";
        private double _TracesSamplingRatio = 1.0;
        private int _LogMinimumSeverity = 2;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public TelemetrySettings()
        {
        }

        /// <summary>
        /// Build settings from the process environment variables.  Unset variables keep their defaults.
        /// </summary>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentException">Thrown when a variable holds an invalid value; the message names the variable.</exception>
        public static TelemetrySettings FromEnvironment()
        {
            return FromValues(Environment.GetEnvironmentVariable);
        }

        /// <summary>
        /// Build settings from a lookup of variable name to value.  A null or empty value keeps the default.
        /// </summary>
        /// <param name="lookup">Lookup, for example <see cref="Environment.GetEnvironmentVariable(string)"/>.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when lookup is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a variable holds an invalid value; the message names the variable.</exception>
        public static TelemetrySettings FromValues(Func<string, string> lookup)
        {
            ArgumentNullException.ThrowIfNull(lookup);

            TelemetrySettings settings = new TelemetrySettings();
            string value;

            if (TryGet(lookup, EnabledVariable, out value)) settings.Enabled = ParseBool(EnabledVariable, value);
            if (TryGet(lookup, ServiceNameVariable, out value)) Apply(ServiceNameVariable, () => settings.ServiceName = value);
            if (TryGet(lookup, OtlpEnabledVariable, out value)) settings.OtlpEnabled = ParseBool(OtlpEnabledVariable, value);
            if (TryGet(lookup, OtlpEndpointVariable, out value)) Apply(OtlpEndpointVariable, () => settings.OtlpEndpoint = value);
            if (TryGet(lookup, OtlpProtocolVariable, out value)) Apply(OtlpProtocolVariable, () => settings.OtlpProtocol = value);
            if (TryGet(lookup, PrometheusEnabledVariable, out value)) settings.PrometheusEnabled = ParseBool(PrometheusEnabledVariable, value);
            if (TryGet(lookup, PrometheusHostnameVariable, out value)) Apply(PrometheusHostnameVariable, () => settings.PrometheusHostname = value);
            if (TryGet(lookup, PrometheusPortVariable, out value)) Apply(PrometheusPortVariable, () => settings.PrometheusPort = ParseInt(PrometheusPortVariable, value));
            if (TryGet(lookup, LokiEnabledVariable, out value)) settings.LokiEnabled = ParseBool(LokiEnabledVariable, value);
            if (TryGet(lookup, LokiEndpointVariable, out value)) Apply(LokiEndpointVariable, () => settings.LokiEndpoint = value);
            if (TryGet(lookup, TracesSamplingRatioVariable, out value)) Apply(TracesSamplingRatioVariable, () => settings.TracesSamplingRatio = ParseDouble(TracesSamplingRatioVariable, value));
            if (TryGet(lookup, LogMinimumSeverityVariable, out value)) Apply(LogMinimumSeverityVariable, () => settings.LogMinimumSeverity = ParseInt(LogMinimumSeverityVariable, value));

            return settings;
        }

        #endregion

        #region Private-Methods

        private static bool TryGet(Func<string, string> lookup, string name, out string value)
        {
            value = lookup(name);
            if (String.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            return true;
        }

        private static void Apply(string name, Action apply)
        {
            try
            {
                apply();
            }
            catch (ArgumentException e) when (e.ParamName != name)
            {
                throw new ArgumentException("Invalid value for environment variable " + name + ": " + e.Message, name, e);
            }
        }

        private static bool ParseBool(string name, string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                case "on":
                    return true;
                case "false":
                case "0":
                case "no":
                case "off":
                    return false;
                default:
                    throw new ArgumentException("Invalid value for environment variable " + name + ": '" + value + "' is not a boolean.", name);
            }
        }

        private static int ParseInt(string name, string value)
        {
            int result;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw new ArgumentException("Invalid value for environment variable " + name + ": '" + value + "' is not an integer.", name);
            return result;
        }

        private static double ParseDouble(string name, string value)
        {
            double result;
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                throw new ArgumentException("Invalid value for environment variable " + name + ": '" + value + "' is not a number.", name);
            return result;
        }

        private static string ValidateUri(string value, string name)
        {
            Uri uri;
            if (String.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri))
                throw new ArgumentException(name + " must be an absolute URI, not '" + value + "'.", name);
            return value.Trim();
        }

        #endregion
    }
}

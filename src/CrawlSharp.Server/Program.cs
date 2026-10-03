namespace CrawlSharp.Server
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.Loader;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using CrawlSharp.Telemetry;
    using CrawlSharp.Web;
    using SerializationHelper;
    using SyslogLogging;
    using Timestamps;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    public static class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static string _Header = "[CrawlSharpServer] ";
        private static LoggingModule _Logging;
        private static Webserver _Webserver;
        private static Serializer _Serializer = new Serializer();
        private static TelemetryService _Telemetry;

        private static string _Hostname = "localhost";
        private static int _Port = 8000;

        public static void Main(string[] args)
        {
            Welcome();
            ParseArguments(args);

            _Logging = new LoggingModule(
                new List<SyslogLogging.SyslogServer>
                {
                    new SyslogLogging.SyslogServer("127.0.0.1", 514)
                });
            _Logging.Settings.MinimumSeverity = (Severity)0;
            _Logging.Settings.EnableConsole = true;
            _Logging.Settings.EnableColors = false;

            if (!Directory.Exists("logs/"))
                Directory.CreateDirectory("logs/");

            _Logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
            _Logging.Settings.LogFilename = "logs/" + "crawlsharp.log";

            TelemetrySettings telemetrySettings;
            try
            {
                telemetrySettings = TelemetrySettings.FromEnvironment();
            }
            catch (ArgumentException e)
            {
                _Logging.Warn(_Header + "invalid telemetry configuration, using defaults: " + e.Message);
                telemetrySettings = new TelemetrySettings();
            }

            // The single telemetry host for the process; it subscribes to Watson's built-in HTTP telemetry and to CrawlSharp's.
            _Telemetry = new TelemetryService(telemetrySettings, _Logging);

            WebserverSettings webserverSettings = new WebserverSettings
            {
                Hostname = _Hostname,
                Port = _Port,
                Ssl = new WatsonWebserver.Core.Settings.SslSettings
                {
                    Enable = false
                }
            };

            // Watson emits the HTTP metrics and the per-request server span; these are the defaults, set explicitly so the
            // contract is visible here.  Radiant serves /metrics, so Watson's own in-process scrape endpoint stays off.
            webserverSettings.Telemetry.Enable = true;
            webserverSettings.Telemetry.EnableMetrics = true;
            webserverSettings.Telemetry.EnableTraces = true;
            webserverSettings.Telemetry.PropagateContext = true;

            _Webserver = new Webserver(webserverSettings, DefaultRoute);

            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.HEAD, "/", RootRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", RootRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.HEAD, "/favicon.ico", FaviconIconRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/favicon.ico", FaviconIconRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.HEAD, "/favicon.png", FaviconPngRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/favicon.png", FaviconPngRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/crawl", CrawlRoute, ExceptionRoute);
            _Webserver.Routes.Preflight = PreflightRoute;
            _Webserver.Routes.PreRouting = PreRoutingRoute;
            _Webserver.Routes.PostRouting = PostRoutingRoute;

            if (_Hostname.Equals("*")
                || _Hostname.Equals("+")
                || _Hostname.Equals("0.0.0.0"))
            {
                Console.WriteLine("Listening on hostname " + _Hostname + " requires administrative privileges.");
                Console.WriteLine("If you encounter an exception, restart with administrative privileges.");
                Console.WriteLine("");
            }

            _Webserver.Start();

            Console.WriteLine("Webserver started on " + _Webserver.Settings.Prefix);
            Console.WriteLine("");

            LogInfo("server started");

            EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
            AssemblyLoadContext.Default.Unloading += (ctx) => waitHandle.Set();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                waitHandle.Set();
                eventArgs.Cancel = true;
            };

            bool waitHandleSignal = false;
            do
            {
                waitHandleSignal = waitHandle.WaitOne(1000);
            }
            while (!waitHandleSignal);

            LogInfo("server stopped");

            try { _Webserver.Stop(); } catch (Exception) { }
            _Telemetry?.Dispose();
        }

        private static void Welcome()
        {
            Console.WriteLine(Environment.NewLine + Constants.Logo + Constants.Copyright + Environment.NewLine);
        }

        private static void ParseArguments(string[] args)
        {
            if (args == null || args.Length != 2)
            {
                Console.WriteLine("");
                Console.WriteLine("Usage:");
                Console.WriteLine("  crawlsharp [hostname] [port]");
                Console.WriteLine("");
                Console.WriteLine("Where:");
                Console.WriteLine("  [hostname] is the hostname or IP address on which to listen");
                Console.WriteLine("  [port] is the port number, greater than or equal to zero, and less than 65536");
                Console.WriteLine("");
            }

            if (args != null && args.Length == 2)
            {
                _Hostname = args[0];

                if (Int32.TryParse(args[1], out int val))
                {
                    if (val < 0 || val > 65535)
                    {
                        Console.WriteLine("");
                        Console.WriteLine("Invalid port specified.  Must be zero or greater, and less than 65536.");
                        Console.WriteLine("");
                        Environment.Exit(1);
                    }

                    _Port = val;
                }
            }

            if (_Hostname == "localhost"
                || _Hostname == "127.0.0.1")
            {
                Console.WriteLine("");
                Console.WriteLine("NOTICE");
                Console.WriteLine("------");
                Console.WriteLine("Configured to listen on local address '" + _Hostname + "'");
                Console.WriteLine("Service will not receive requests from outside of localhost");
                Console.WriteLine("");
            }
        }

        private static async Task RootRoute(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength = Constants.HtmlHomepage.Length;
            ctx.Response.ContentType = Constants.HtmlContentType;

            if (ctx.Request.Method == HttpMethod.HEAD) await ctx.Response.Send();
            else
            {
                await ctx.Response.Send(Constants.HtmlHomepage);
            }
        }

        private static void LogInfo(string msg)
        {
            _Logging.Info(_Header + msg);
            _Telemetry?.Info(_Header + msg);
        }

        private static void LogWarn(string msg)
        {
            _Logging.Warn(_Header + msg);
            _Telemetry?.Warn(_Header + msg);
        }

        private static void LogDebug(string msg)
        {
            _Logging.Debug(_Header + msg);
            _Telemetry?.Debug(_Header + msg);
        }

        private static async Task ExceptionRoute(HttpContextBase ctx, Exception e)
        {
            LogWarn("exception encountered:" + Environment.NewLine + e.ToString());

            ctx.Response.ContentType = Constants.JsonContentType;

            if (e is JsonException)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.DeserializationError), true));
                return;
            }

            if (e is ArgumentException)
            {
                // Invalid settings, such as incomplete authentication or an out-of-range value.  WebCrawler validates in its
                // constructor, before the response switches to server-sent events, so a clean 400 can still be returned.
                ctx.Response.StatusCode = 400;
                await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.BadRequest, null, e.Message), true));
                return;
            }

            ctx.Response.StatusCode = 500;
            await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.InternalError), true));
            return;
        }

        private static async Task FaviconIconRoute(HttpContextBase ctx)
        {
            FileInfo fi = new FileInfo(Constants.FaviconIconFilename);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength = fi.Length;
            ctx.Response.ContentType = Constants.FaviconIconContentType;

            if (ctx.Request.Method == HttpMethod.HEAD) await ctx.Response.Send();
            else
            {
                await ctx.Response.Send(File.ReadAllBytes(Constants.FaviconIconFilename));
            }
        }

        private static async Task FaviconPngRoute(HttpContextBase ctx)
        {
            FileInfo fi = new FileInfo(Constants.FaviconPngFilename);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength = fi.Length;
            ctx.Response.ContentType = Constants.FaviconPngContentType;

            if (ctx.Request.Method == HttpMethod.HEAD) await ctx.Response.Send();
            else
            {
                await ctx.Response.Send(File.ReadAllBytes(Constants.FaviconPngFilename));
            }
        }

        private static async Task DefaultRoute(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.BadRequest), true));
        }

        private static async Task CrawlRoute(HttpContextBase ctx)
        {
            ctx.Response.ContentType = Constants.JsonContentType;

            // One span for the crawl request under Watson's HTTP server span; the library's crawl span nests under it.
            long startTimestamp = Stopwatch.GetTimestamp();
            Activity previous = Activity.Current;
            Activity span = ServerInstruments.StartActivity(CrawlSharpTelemetry.SpanServerCrawlRequest);
            string outcome = ServerInstruments.OutcomeFailed;
            bool streamOpen = false;
            long events = 0;

            try
            {
                if (ctx.Request.DataAsString == null || ctx.Request.DataAsString.Length < 1)
                {
                    LogWarn("no request body from " + ctx.Request.Source.IpAddress);
                    outcome = ServerInstruments.OutcomeBadRequest;
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.BadRequest), true));
                    return;
                }

                Settings settings = RunStage(CrawlSharpTelemetry.ServerStageDeserialize, ServerInstruments.OutcomeDeserializationError, ref outcome,
                    () => _Serializer.DeserializeJson<Settings>(ctx.Request.DataAsString));

                WebCrawler crawler = RunStage(CrawlSharpTelemetry.ServerStageCrawlerInit, ServerInstruments.OutcomeInvalidSettings, ref outcome,
                    () => new WebCrawler(settings));

                using (crawler)
                using (Timestamp ts = new Timestamp())
                {
                    if (_Telemetry != null && _Telemetry.IsDebugEnabled) crawler.Logger = msg => _Telemetry.Debug(msg);
                    crawler.Exception = (url, e) => _Telemetry?.Warn("[WebCrawler] exception processing " + url + ": " + e.GetType().FullName + ": " + e.Message);

                    ctx.Response.ServerSentEvents = true;
                    ts.Start = DateTime.UtcNow;
                    ServerInstruments.AddStream(1);
                    streamOpen = true;

                    LogDebug("crawl request received from " + ctx.Request.Source.IpAddress + " for " + settings.Crawl.StartUrl);

                    bool clientGone = false;

                    await foreach (WebResource resource in crawler.CrawlAsync())
                    {
                        string data = _Serializer.SerializeJson(resource, false);

                        if (!await SendEventAsync(ctx, data))
                        {
                            // The client stopped reading; stop crawling for it rather than fetching pages nobody will receive.
                            LogWarn("client " + ctx.Request.Source.IpAddress + " stopped reading the crawl of " + settings.Crawl.StartUrl + ", stopping");
                            clientGone = true;
                            break;
                        }

                        events++;
                    }

                    if (clientGone)
                    {
                        outcome = ServerInstruments.OutcomeClientDisconnected;
                    }
                    else
                    {
                        await ctx.Response.SendEvent(new ServerSentEvent
                        {
                            Data = "[DONE]"
                        }, true);

                        outcome = ServerInstruments.OutcomeCompleted;
                    }

                    ts.End = DateTime.UtcNow;

                    LogDebug("completed crawl request from " + ctx.Request.Source.IpAddress + " for " + settings.Crawl.StartUrl + " (" + ts.TotalMs + "ms)");
                }
            }
            catch (Exception e)
            {
                if (outcome == ServerInstruments.OutcomeFailed) ServerInstruments.SetError(span, e);
                throw;
            }
            finally
            {
                if (streamOpen) ServerInstruments.AddStream(-1);
                ServerInstruments.RecordRequest(outcome, ServerInstruments.ElapsedSeconds(startTimestamp));

                if (span != null)
                {
                    try
                    {
                        span.SetTag(CrawlSharpTelemetry.AttributeOutcome, outcome);
                        span.SetTag(CrawlSharpTelemetry.AttributeSseEvents, events);
                        if (span.Status != ActivityStatusCode.Error)
                        {
                            if (outcome == ServerInstruments.OutcomeFailed) span.SetStatus(ActivityStatusCode.Error, outcome);
                            else span.SetStatus(ActivityStatusCode.Ok);
                        }

                        span.Dispose();
                        if (!ReferenceEquals(Activity.Current, previous)) Activity.Current = previous;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static T RunStage<T>(string stage, string failureOutcome, ref string outcome, Func<T> work)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            Activity previous = Activity.Current;
            Activity activity = ServerInstruments.StartActivity(CrawlSharpTelemetry.SpanStagePrefix + stage);
            string stageOutcome = CrawlSharpTelemetry.OutcomeSuccess;

            try
            {
                return work();
            }
            catch (Exception e)
            {
                // JSON and argument errors are the caller's mistake and become a 400 in ExceptionRoute; anything else is a server failure.
                stageOutcome = CrawlSharpTelemetry.OutcomeFailure;
                outcome = (e is JsonException || e is ArgumentException) ? failureOutcome : ServerInstruments.OutcomeFailed;
                ServerInstruments.SetError(activity, e);
                throw;
            }
            finally
            {
                ServerInstruments.RecordStage(stage, stageOutcome, ServerInstruments.ElapsedSeconds(startTimestamp));

                if (activity != null)
                {
                    try
                    {
                        activity.SetTag(CrawlSharpTelemetry.AttributeOutcome, stageOutcome);
                        if (activity.Status != ActivityStatusCode.Error) activity.SetStatus(ActivityStatusCode.Ok);
                        activity.Dispose();
                        if (!ReferenceEquals(Activity.Current, previous)) Activity.Current = previous;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static async Task<bool> SendEventAsync(HttpContextBase ctx, string data)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            bool sent = false;

            try
            {
                sent = await ctx.Response.SendEvent(new ServerSentEvent
                {
                    Data = data
                },
                false);

                return sent;
            }
            finally
            {
                ServerInstruments.RecordStage(
                    CrawlSharpTelemetry.ServerStageSseSend,
                    sent ? CrawlSharpTelemetry.OutcomeSuccess : CrawlSharpTelemetry.OutcomeFailure,
                    ServerInstruments.ElapsedSeconds(startTimestamp));

                if (sent) ServerInstruments.RecordEvent(data != null ? Encoding.UTF8.GetByteCount(data) : 0);
            }
        }

        private static async Task PreflightRoute(HttpContextBase ctx)
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, HEAD, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            ctx.Response.StatusCode = 200;
            await ctx.Response.Send();
        }

        private static async Task PreRoutingRoute(HttpContextBase ctx)
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
        }

        private static async Task PostRoutingRoute(HttpContextBase ctx)
        {
            ctx.Request.Timestamp.End = DateTime.UtcNow;
            LogDebug(ctx.Request.Method + " " + ctx.Request.Url.RawWithQuery + ": " + ctx.Response.StatusCode + " (" + ctx.Request.Timestamp.TotalMs + "ms)");
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    }
}
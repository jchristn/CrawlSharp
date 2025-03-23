namespace CrawlSharp.Server
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Loader;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
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

        private static string _Hostname = "localhost";
        private static int _Port = 8000;

        public static void Main(string[] args)
        {
            Welcome();
            ParseArguments(args);

            _Logging = new LoggingModule("127.0.0.1", 514, true);
            _Logging.Settings.EnableColors = false;
            _Logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
            _Logging.Settings.LogFilename = "crawlsharp.log";

            _Webserver = new Webserver(new WebserverSettings
            {
                Hostname = _Hostname,
                Port = _Port,
                Ssl = new WebserverSettings.SslSettings
                {
                    Enable = false
                }
            }, DefaultRoute);

            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.HEAD, "/", RootRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", RootRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.HEAD, "/favicon.ico", FaviconIconRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/favicon.ico", FaviconIconRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.HEAD, "/favicon.png", FaviconPngRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/favicon.png", FaviconPngRoute, ExceptionRoute);
            _Webserver.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/crawl", CrawlRoute, ExceptionRoute);
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

            _Logging.Info(_Header + "server started");

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

            _Logging.Info(_Header + "server stopped");
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

        private static async Task ExceptionRoute(HttpContextBase ctx, Exception e)
        {
            _Logging.Warn(_Header + "exception encountered:" + Environment.NewLine + e.ToString());

            ctx.Response.ContentType = Constants.JsonContentType;

            if (e is JsonException)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.DeserializationError), true));
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

            if (ctx.Request.DataAsString == null || ctx.Request.DataAsString.Length < 1)
            {
                _Logging.Warn(_Header + "no request body from " + ctx.Request.Source.IpAddress);
                ctx.Response.StatusCode = 400;
                await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.BadRequest), true));
                return;
            }

            Settings settings = _Serializer.DeserializeJson<Settings>(ctx.Request.DataAsString);

            WebCrawler crawler = new WebCrawler(settings);

            ctx.Response.ServerSentEvents = true;

            using (Timestamp ts = new Timestamp())
            {
                ts.Start = DateTime.UtcNow;

                _Logging.Debug(_Header + "crawl request received from " + ctx.Request.Source.IpAddress + " for " + settings.Crawl.StartUrl);

                await foreach (WebResource resource in crawler.CrawlAsync())
                {
                    await ctx.Response.SendEvent(_Serializer.SerializeJson(resource, false), false);
                }

                await ctx.Response.SendEvent("[end]", true);

                ts.End = DateTime.UtcNow;

                _Logging.Debug(_Header + "completed crawl request from " + ctx.Request.Source.IpAddress + " for " + settings.Crawl.StartUrl + " (" + ts.TotalMs + "ms)");
            }
        }

        private static async Task PostRoutingRoute(HttpContextBase ctx)
        {
            ctx.Request.Timestamp.End = DateTime.UtcNow;
            _Logging.Debug(_Header + ctx.Request.Method + " " + ctx.Request.Url.RawWithQuery + ": " + ctx.Response.StatusCode + " (" + ctx.Request.Timestamp.TotalMs + "ms)");
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    }
}
namespace Test
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using CrawlSharp.Web;
    using GetSomeInput;
    using SerializationHelper;

    public static class Program
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static bool _RunForever = true;
        private static Serializer _Serializer = new Serializer();

        public static async Task Main(string[] args)
        {
            while (_RunForever)
            {
                string userInput = Inputty.GetString("Command [?/help]:", null, false);

                switch (userInput)
                {
                    case "q":
                        _RunForever = false;
                        break;
                    case "cls":
                        Console.Clear();
                        break;
                    case "?":
                        Menu();
                        break;
                    case "r":
                        ParseRobotsTextFile();
                        break;
                    case "crawl":
                        await Crawl();
                        break;
                }
            }
        }

        private static void Menu()
        {
            Console.WriteLine("");
            Console.WriteLine("Available commands:");
            Console.WriteLine("q              quit");
            Console.WriteLine("cls            clear the screen");
            Console.WriteLine("?              help, this menu");
            Console.WriteLine("r              parse a robots.txt file");
            Console.WriteLine("crawl          crawl a URL");
            Console.WriteLine("");
        }

        private static void ParseRobotsTextFile()
        {
            string file = Inputty.GetString("Filename:", null, true);
            if (String.IsNullOrEmpty(file)) return;
            byte[] bytes = File.ReadAllBytes(file);
            RobotsFile robots = new RobotsFile(bytes);
            Console.WriteLine(_Serializer.SerializeJson(robots, true));
        }

        private static async Task Crawl()
        {
            string url = Inputty.GetString("URL:", null, true);
            if (String.IsNullOrEmpty(url)) return;

            Settings settings = new Settings();
            settings.Crawl.StartUrl = url;
            settings.Crawl.MaxParallelTasks = 16;
            settings.Crawl.RestrictToChildUrls = false;
            settings.Crawl.RestrictToSameDomain = false;
            settings.Crawl.MaxCrawlDepth = 1;
            settings.Crawl.AllowedDomains.Add("soundcloud.com");
            settings.Crawl.AllowedDomains.Add("timberstud.io");

            WebCrawler crawler = new WebCrawler(settings);
            crawler.Logger = Console.WriteLine;

            Console.WriteLine("Settings:" + Environment.NewLine + _Serializer.SerializeJson(settings, true));

            long bytesCrawled = 0;
            int resourcesCrawled = 0;
            List<string> urls = new List<string>();

            await foreach (WebResource resource in crawler.Crawl())
            {
                string parentUrl = ".";
                if (!String.IsNullOrEmpty(resource.ParentUrl)) parentUrl = resource.ParentUrl;
                Console.WriteLine("[" + resource.Status.ToString("D3") + "] " + resource.Url + " (parent " + parentUrl + ")");
                bytesCrawled += resource.ContentLength;
                resourcesCrawled++;
                urls.Add("[" + resource.Status.ToString("D3") + "] " + resource.Url);
            }

            Console.WriteLine("");
            Console.WriteLine("Crawled " + resourcesCrawled + " resources containing " + bytesCrawled + " bytes");
            Console.WriteLine("");
            Console.WriteLine("URLs:");
            foreach (string visited in urls)
                Console.WriteLine("| " + visited);
            Console.WriteLine("");
            Console.WriteLine("Visited URLs:");
            foreach (KeyValuePair<Uri, WebResource> kvp in crawler.VisitedLinks)
                Console.WriteLine("| " + kvp.Key.ToString());
            Console.WriteLine("");
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }
}
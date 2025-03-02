namespace CrawlSharp.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Constants.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Logo.
        /// </summary>
        public static string Logo =
            @"                          _     _  _    " + Environment.NewLine +
            @"   ___ _ __ __ ___      _| |  _| || |_  " + Environment.NewLine +
            @"  / __| '__/ _` \ \ /\ / / | |_  ..  _| " + Environment.NewLine +
            @" | (__| | | (_| |\ V  V /| | |_      _| " + Environment.NewLine +
            @"  \___|_|  \__,_| \_/\_/ |_|   |_||_|   " + Environment.NewLine + Environment.NewLine;

        /// <summary>
        /// Copyright.
        /// </summary>
        public static string Copyright = "(c)2025 Joel Christner";

        /// <summary>
        /// Default HTML homepage.
        /// </summary>
        public static string HtmlHomepage =
            @"<html>" + Environment.NewLine +
            @"  <head>" + Environment.NewLine +
            @"    <title>Node is Operational</title>" + Environment.NewLine +
            @"  </head>" + Environment.NewLine +
            @"  <body>" + Environment.NewLine +
            @"    <div>" + Environment.NewLine +
            @"      <pre>" + Environment.NewLine + Environment.NewLine +
            Logo + Environment.NewLine +
            @"      </pre>" + Environment.NewLine +
            @"    </div>" + Environment.NewLine +
            @"    <div style='font-family: Arial, sans-serif;'>" + Environment.NewLine +
            @"      <h2>Your node is operational</h2>" + Environment.NewLine +
            @"      <p>Congratulations, your node is operational.  Please refer to the documentation for use.</p>" + Environment.NewLine +
            @"    <div>" + Environment.NewLine +
            @"  </body>" + Environment.NewLine +
            @"</html>" + Environment.NewLine;

        /// <summary>
        /// Binary content type.
        /// </summary>
        public static string BinaryContentType = "application/octet-stream";

        /// <summary>
        /// JSON content type.
        /// </summary>
        public static string JsonContentType = "application/json";

        /// <summary>
        /// HTML content type.
        /// </summary>
        public static string HtmlContentType = "text/html";

        /// <summary>
        /// PNG content type.
        /// </summary>
        public static string PngContentType = "image/png";

        /// <summary>
        /// Text content type.
        /// </summary>
        public static string TextContentType = "text/plain";

        /// <summary>
        /// Favicon filename.
        /// </summary>
        public static string FaviconIconFilename = "assets/favicon.ico";

        /// <summary>
        /// Favicon content type.
        /// </summary>
        public static string FaviconIconContentType = "image/x-icon";

        /// <summary>
        /// Favicon filename.
        /// </summary>
        public static string FaviconPngFilename = "assets/favicon.png";

        /// <summary>
        /// Favicon content type.
        /// </summary>
        public static string FaviconPngContentType = "image/png";
    }
}

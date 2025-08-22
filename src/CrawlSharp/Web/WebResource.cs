namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection.PortableExecutable;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Web resource, an item identified during a crawl.
    /// </summary>
    public class WebResource
    {
        #region Public-Members

        /// <summary>
        /// URL of the web resource.
        /// </summary>
        public string Url { get; set; } = null;

        /// <summary>
        /// Parent URL.
        /// </summary>
        public string ParentUrl { get; set; } = null;

        /// <summary>
        /// Filename.
        /// </summary>
        public string Filename
        {
            get
            {
                if (!String.IsNullOrEmpty(Url))
                {
                    try
                    {
                        Uri uri = new Uri(Url);
                        string path = Uri.UnescapeDataString(uri.AbsolutePath);
                        return Path.GetFileName(path);
                    }
                    catch (UriFormatException)
                    {
                    }
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Depth.
        /// </summary>
        public int Depth
        {
            get
            {
                return _Depth;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(Depth));
                _Depth = value;
            }
        }

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int Status
        {
            get
            {
                return _Status;
            }
            set
            {
                if (value < 0 || value > 599) value = 400;
                _Status = value;
            }
        }

        /// <summary>
        /// Content-length.
        /// </summary>
        public long ContentLength
        {
            get
            {
                if (Data != null) return Data.Length;
                return 0;
            }
        }

        /// <summary>
        /// The Content-Type header value of the resource.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Value from the ETag header.
        /// </summary>
        public string ETag { get; set; } = null;

        /// <summary>
        /// MD5 of the content.
        /// </summary>
        public string MD5Hash { get; set; } = null;

        /// <summary>
        /// SHA1 of the content.
        /// </summary>
        public string SHA1Hash { get; set; } = null;

        /// <summary>
        /// SHA256 of the content.
        /// </summary>
        public string SHA256Hash { get; set; } = null;

        /// <summary>
        /// Last modified timestamp, from the Last-Modified header, if it exists.
        /// </summary>
        public DateTime? LastModified
        {
            get
            {
                if (!String.IsNullOrEmpty(_Headers.Get("Last-Modified")))
                {
                    DateTime result;

                    if (DateTime.TryParseExact(
                        _Headers.Get("Last-Modified"),
                        _LastModifiedFormats,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out result))
                    {
                        return result;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Headers from the web resource.
        /// </summary>
        public NameValueCollection Headers
        {
            get
            {
                return _Headers;
            }
            set
            {
                if (value == null) value = new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
                _Headers = value;
            }
        }

        /// <summary>
        /// Data.
        /// </summary>
        public byte[] Data { get; set; } = null;

        #endregion

        #region Private-Members

        private int _Depth = 0;
        private int _Status = 0;
        private NameValueCollection _Headers = new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);

        private string[] _LastModifiedFormats = new[]
        {
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'",  // RFC 7232 / RFC 1123 format
            "dddd, dd-MMM-yy HH:mm:ss 'GMT'",   // RFC 850 format
            "ddd MMM d HH:mm:ss yyyy"           // ANSI C's asctime() format
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Web resource, an item identified during a crawl.
        /// </summary>
        public WebResource()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}

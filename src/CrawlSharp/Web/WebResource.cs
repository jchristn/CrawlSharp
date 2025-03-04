namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
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
        /// Content-type.
        /// </summary>
        public string ContentType
        {
            get
            {
                if (_Headers != null)
                {
                    string typeKey = _Headers.AllKeys.FirstOrDefault(k => string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase));
                    string contentType = typeKey != null ? _Headers[typeKey] : null;
                }

                return null;
            }
        }

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

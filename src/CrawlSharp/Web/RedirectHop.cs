namespace CrawlSharp.Web
{
    /// <summary>
    /// One redirect received while retrieving a resource.
    /// Instances are not thread-safe; each <see cref="WebResource"/> owns its own list of hops.
    /// </summary>
    public class RedirectHop
    {
        #region Public-Members

        /// <summary>
        /// URL that returned the redirect response.
        /// Default is null.
        /// </summary>
        public string Url { get; set; } = null;

        /// <summary>
        /// HTTP status code of the redirect response, for example 301 or 302.
        /// Default is 0.
        /// </summary>
        public int Status { get; set; } = 0;

        /// <summary>
        /// Redirect target: the absolute URL resolved from the Location header, or the raw header value when it could not be resolved.
        /// Default is null.
        /// </summary>
        public string Location { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty hop.
        /// </summary>
        public RedirectHop()
        {

        }

        /// <summary>
        /// Instantiate a hop.
        /// </summary>
        /// <param name="url">URL that returned the redirect response.</param>
        /// <param name="status">HTTP status code of the redirect response.</param>
        /// <param name="location">Resolved redirect target, or the raw Location header value.</param>
        public RedirectHop(string url, int status, string location)
        {
            Url = url;
            Status = status;
            Location = location;
        }

        #endregion
    }
}

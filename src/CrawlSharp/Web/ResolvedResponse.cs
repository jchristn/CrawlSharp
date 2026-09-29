namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;

    /// <summary>
    /// Result of following one requested URL through its redirect chain.
    /// Produced and consumed by <see cref="WebCrawler"/> on a single task; not thread-safe.
    /// </summary>
    internal sealed class ResolvedResponse
    {
        #region Internal-Members

        /// <summary>
        /// URL that was requested.
        /// </summary>
        internal Uri RequestedUri { get; set; } = null;

        /// <summary>
        /// Last URL actually requested; the status, headers and body below came from it.
        /// </summary>
        internal Uri FinalUri { get; set; } = null;

        /// <summary>
        /// Every URL actually requested for this chain, in order, including <see cref="RequestedUri"/> and <see cref="FinalUri"/>.
        /// </summary>
        internal List<Uri> RequestedUris { get; } = new List<Uri>();

        /// <summary>
        /// Redirects received, in order.
        /// </summary>
        internal List<RedirectHop> Chain { get; } = new List<RedirectHop>();

        /// <summary>
        /// Why the chain stopped.
        /// </summary>
        internal RedirectOutcomeEnum Outcome { get; set; } = RedirectOutcomeEnum.None;

        /// <summary>
        /// HTTP status code of the last response.
        /// </summary>
        internal int Status { get; set; } = 0;

        /// <summary>
        /// Headers of the last response.
        /// </summary>
        internal NameValueCollection Headers { get; set; } = null;

        /// <summary>
        /// Media type from the last response's Content-Type header, lower-cased, or null when the header was absent.
        /// </summary>
        internal string MediaType { get; set; } = null;

        /// <summary>
        /// ETag of the last response, without quotes or the weak prefix.
        /// </summary>
        internal string ETag { get; set; } = null;

        /// <summary>
        /// Body of the last response; null for HEAD requests and empty bodies.
        /// </summary>
        internal byte[] Data { get; set; } = null;

        /// <summary>
        /// Set when a redirect target had already been retrieved by another path.  The target was not requested again,
        /// and this is the resource already retrieved for it.
        /// </summary>
        internal WebResource AliasOf { get; set; } = null;

        #endregion
    }
}

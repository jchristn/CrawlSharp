namespace CrawlSharp.Web
{
    /// <summary>
    /// Why the crawler stopped following the redirect chain for a resource.
    /// </summary>
    public enum RedirectOutcomeEnum
    {
        /// <summary>
        /// The first response was not a redirect.
        /// </summary>
        None,
        /// <summary>
        /// One or more redirects were followed and the chain ended in a response that is not a redirect.
        /// </summary>
        Followed,
        /// <summary>
        /// The first response was a redirect and <see cref="CrawlSettings.FollowRedirects"/> is false, so it was not followed.
        /// </summary>
        NotFollowed,
        /// <summary>
        /// The chain returned to a URL it had already requested, with the same cookies, so following it would repeat forever.
        /// </summary>
        LoopDetected,
        /// <summary>
        /// The chain was longer than <see cref="CrawlSettings.MaxRedirects"/>.
        /// </summary>
        MaxRedirectsExceeded,
        /// <summary>
        /// The redirect target is outside the crawl scope (domain, child URL, external link or exclusion rules), so it was not requested.
        /// </summary>
        OutOfScope,
        /// <summary>
        /// The redirect target is disallowed by robots.txt, so it was not requested.
        /// </summary>
        RobotsDisallowed,
        /// <summary>
        /// The redirect response carried no Location header.
        /// </summary>
        MissingLocation,
        /// <summary>
        /// The redirect response carried a Location header that is not a valid http or https URL.
        /// </summary>
        InvalidLocation
    }
}

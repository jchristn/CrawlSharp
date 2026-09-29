namespace CrawlSharp.Web
{
    using System;
    using System.Net.Http;

    /// <summary>
    /// Stateless rules the crawler uses when following redirects: which statuses are redirects, which HTTP method
    /// the next request uses, how a Location header is resolved, and how origins are compared.
    /// All members are static and hold no state, so they are safe to call from multiple threads.
    /// </summary>
    public static class RedirectPolicy
    {
        #region Public-Methods

        /// <summary>
        /// Determine whether a response status is a redirect the crawler can follow.
        /// 301, 302, 303, 307 and 308 are redirects; 300 is treated as a redirect only when it carries a Location header.
        /// 304, 305, 306 and every other status are not redirects.
        /// </summary>
        /// <param name="status">HTTP status code.</param>
        /// <param name="hasLocation">True if the response carried a non-empty Location header.</param>
        /// <returns>True if the status is a redirect.</returns>
        public static bool IsRedirectStatus(int status, bool hasLocation)
        {
            switch (status)
            {
                case 301:
                case 302:
                case 303:
                case 307:
                case 308:
                    return true;
                case 300:
                    return hasLocation;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determine the HTTP method for the request that follows a redirect.
        /// 300, 301 and 302 change POST to GET and keep any other method; 303 changes everything except HEAD to GET;
        /// 307 and 308 keep the original method.  Statuses that are not redirects keep the original method.
        /// </summary>
        /// <param name="method">Method of the request that received the redirect.</param>
        /// <param name="status">HTTP status code of the redirect response.</param>
        /// <returns>Method to use for the next request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when method is null.</exception>
        public static HttpMethod GetRedirectMethod(HttpMethod method, int status)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            switch (status)
            {
                case 300:
                case 301:
                case 302:
                    return method == HttpMethod.Post ? HttpMethod.Get : method;
                case 303:
                    return method == HttpMethod.Head ? HttpMethod.Head : HttpMethod.Get;
                default:
                    return method;
            }
        }

        /// <summary>
        /// Resolve a Location header value against the URL that returned it, following RFC 3986 reference resolution.
        /// Relative references such as "b", "../b", "/b", "?q=1" and "//host/b" are supported.  The fragment is removed.
        /// </summary>
        /// <param name="current">Absolute URL that returned the redirect.</param>
        /// <param name="location">Location header value.</param>
        /// <returns>Absolute http or https URL, or null if the value is empty, cannot be parsed, or uses another scheme.</returns>
        /// <exception cref="ArgumentNullException">Thrown when current is null.</exception>
        /// <exception cref="ArgumentException">Thrown when current is not an absolute URL.</exception>
        public static Uri ResolveLocation(Uri current, string location)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (!current.IsAbsoluteUri) throw new ArgumentException("The current URL must be absolute.", nameof(current));
            if (String.IsNullOrWhiteSpace(location)) return null;

            Uri target;
            if (!Uri.TryCreate(current, location.Trim(), out target)) return null;
            if (!IsHttpScheme(target)) return null;
            if (String.IsNullOrEmpty(target.Host)) return null;

            if (!String.IsNullOrEmpty(target.Fragment))
            {
                UriBuilder builder = new UriBuilder(target);
                builder.Fragment = String.Empty;
                target = builder.Uri;
            }

            return target;
        }

        /// <summary>
        /// Build the origin (scheme, host and port) of an absolute URL, for example "https://example.com:443".
        /// Scheme and host are lower-cased and the port is always explicit, so "http://h" and "http://h:80" have the same origin.
        /// </summary>
        /// <param name="uri">Absolute URL.</param>
        /// <returns>Origin string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when uri is null.</exception>
        /// <exception cref="ArgumentException">Thrown when uri is not absolute.</exception>
        public static string GetOrigin(Uri uri)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (!uri.IsAbsoluteUri) throw new ArgumentException("The URL must be absolute.", nameof(uri));
            return uri.Scheme.ToLowerInvariant() + "://" + uri.Host.ToLowerInvariant() + ":" + uri.Port.ToString();
        }

        /// <summary>
        /// Determine whether two absolute URLs share an origin (scheme, host and port).
        /// </summary>
        /// <param name="a">First URL.</param>
        /// <param name="b">Second URL.</param>
        /// <returns>True if the origins match.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either URL is null.</exception>
        public static bool IsSameOrigin(Uri a, Uri b)
        {
            return String.Equals(GetOrigin(a), GetOrigin(b), StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static bool IsHttpScheme(Uri uri)
        {
            return uri.IsAbsoluteUri
                && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}

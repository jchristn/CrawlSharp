namespace Test.Shared
{
    using System;
    using System.Collections.Specialized;

    /// <summary>
    /// A request received by a <see cref="FixtureServer"/>, captured so tests can assert on what the crawler sent.
    /// </summary>
    public sealed class RecordedRequest
    {
        /// <summary>
        /// Request path, without the query string.
        /// </summary>
        public string Path { get; set; } = null;

        /// <summary>
        /// Query string, including the leading question mark, or empty.
        /// </summary>
        public string Query { get; set; } = String.Empty;

        /// <summary>
        /// HTTP method, for example GET or HEAD.
        /// </summary>
        public string Method { get; set; } = null;

        /// <summary>
        /// Copy of the request headers.
        /// </summary>
        public NameValueCollection Headers { get; set; } = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
    }
}

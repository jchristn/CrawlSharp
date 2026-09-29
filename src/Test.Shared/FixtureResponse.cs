namespace Test.Shared
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response returned by a <see cref="FixtureServer"/> handler.
    /// </summary>
    public sealed class FixtureResponse
    {
        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Content-Type header value.
        /// </summary>
        public string ContentType { get; set; } = "text/plain; charset=utf-8";

        /// <summary>
        /// Response body.
        /// </summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Additional response headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

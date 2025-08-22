namespace CrawlSharp.Web
{
    /// <summary>
    /// Content type information from HTTP headers.
    /// </summary>
    /// <remarks>
    /// This class encapsulates the results of a content type check performed via HTTP HEAD request.
    /// It is used to determine whether content should be navigated to in a browser context
    /// or downloaded directly via HTTP client.
    /// </remarks>
    public class ContentTypeInfo
    {
        /// <summary>
        /// Gets or sets a value indicating whether the content is navigable in a browser context.
        /// </summary>
        /// <value>
        /// <c>true</c> if the content type indicates HTML or other browser-renderable content
        /// (e.g., text/html, application/xhtml+xml); otherwise, <c>false</c> for downloadable
        /// content such as PDFs, images, or other binary files.
        /// </value>
        /// <remarks>
        /// When this value is <c>true</c>, the content can be loaded using browser automation tools
        /// like Playwright. When <c>false</c>, the content should be retrieved directly via HTTP client
        /// to preserve the binary data.
        /// </remarks>
        public bool IsNavigable { get; set; }

        /// <summary>
        /// Gets or sets the MIME type of the content as reported by the Content-Type header.
        /// </summary>
        /// <value>
        /// The media type string (e.g., "text/html", "application/pdf", "image/jpeg").
        /// May be <c>null</c> if the Content-Type header was not present or if the check failed.
        /// </value>
        /// <remarks>
        /// This value is normalized to lowercase and excludes any charset or other parameters
        /// that may be present in the Content-Type header.
        /// </remarks>
        public string MediaType { get; set; }

        /// <summary>
        /// Gets or sets the size of the content in bytes as reported by the Content-Length header.
        /// </summary>
        /// <value>
        /// The content length in bytes, or <c>null</c> if the Content-Length header was not present
        /// or if the check failed.
        /// </value>
        /// <remarks>
        /// This can be useful for determining whether to download large files or for providing
        /// progress information during downloads. Note that some servers may not provide
        /// accurate Content-Length headers, especially for dynamically generated content.
        /// </remarks>
        public long? ContentLength { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the content type check completed successfully.
        /// </summary>
        /// <value>
        /// <c>true</c> if the HEAD request succeeded and headers were retrieved; otherwise, <c>false</c>
        /// if the request failed due to network issues, authentication problems, or server errors.
        /// </value>
        /// <remarks>
        /// When this value is <c>false</c>, the <see cref="IsNavigable"/> property defaults to <c>true</c>
        /// as a safe fallback, allowing the crawler to attempt browser navigation. The crawler can then
        /// handle any download triggers that may occur during navigation.
        /// </remarks>
        public bool CheckSucceeded { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentTypeInfo"/> class.
        /// </summary>
        /// <remarks>
        /// Creates a new ContentTypeInfo with default values. By default, <see cref="IsNavigable"/>
        /// is set to <c>false</c>, <see cref="CheckSucceeded"/> is set to <c>false</c>,
        /// and both <see cref="MediaType"/> and <see cref="ContentLength"/> are <c>null</c>.
        /// </remarks>
        public ContentTypeInfo()
        {
            IsNavigable = false;
            MediaType = null;
            ContentLength = null;
            CheckSucceeded = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentTypeInfo"/> class with specified values.
        /// </summary>
        /// <param name="isNavigable">Whether the content is navigable in a browser.</param>
        /// <param name="mediaType">The MIME type of the content.</param>
        /// <param name="contentLength">The size of the content in bytes.</param>
        /// <param name="checkSucceeded">Whether the content type check succeeded.</param>
        public ContentTypeInfo(bool isNavigable, string mediaType, long? contentLength, bool checkSucceeded)
        {
            IsNavigable = isNavigable;
            MediaType = mediaType;
            ContentLength = contentLength;
            CheckSucceeded = checkSucceeded;
        }

        /// <summary>
        /// Returns a string representation of the <see cref="ContentTypeInfo"/> instance.
        /// </summary>
        /// <returns>
        /// A string containing the media type, navigability status, content length (if available),
        /// and check success status.
        /// </returns>
        public override string ToString()
        {
            return $"ContentTypeInfo: MediaType={MediaType ?? "null"}, IsNavigable={IsNavigable}, " +
                   $"ContentLength={ContentLength?.ToString() ?? "null"}, CheckSucceeded={CheckSucceeded}";
        }
    }
}
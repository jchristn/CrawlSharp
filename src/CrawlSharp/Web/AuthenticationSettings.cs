namespace CrawlSharp.Web
{
    /// <summary>
    /// Authentication settings.
    /// </summary>
    public class AuthenticationSettings
    {
        #region Public-Members

        /// <summary>
        /// Authentication type.
        /// </summary>
        public AuthenticationTypeEnum Type { get; set; } = AuthenticationTypeEnum.None;

        /// <summary>
        /// Username for basic authentication.
        /// </summary>
        public string Username { get; set; } = null;

        /// <summary>
        /// Password for basic authentication.
        /// </summary>
        public string Password { get; set; } = null;

        /// <summary>
        /// Header to use for attaching an API key to the request.
        /// </summary>
        public string ApiKeyHeader { get; set; } = null;

        /// <summary>
        /// API key to attach.
        /// </summary>
        public string ApiKey { get; set; } = null;

        /// <summary>
        /// Bearer token to use in the authorization header.
        /// </summary>
        public string BearerToken { get; set; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Authentication settings.
        /// </summary>
        public AuthenticationSettings()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}

namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Authentication settings.
    /// Credentials are sent only to origins in the credential scope: the origin of <see cref="CrawlSettings.StartUrl"/>,
    /// its HTTPS upgrade when the start URL is plain HTTP, and any origins listed in <see cref="CredentialOrigins"/>.
    /// </summary>
    public class AuthenticationSettings
    {
        #region Public-Members

        /// <summary>
        /// Authentication type.  Default is None.
        /// When None, every credential field must be empty; <see cref="Validate"/> throws otherwise.
        /// </summary>
        public AuthenticationTypeEnum Type { get; set; } = AuthenticationTypeEnum.None;

        /// <summary>
        /// Username for basic authentication.  Required when <see cref="Type"/> is Basic.  Default is null.
        /// </summary>
        public string Username { get; set; } = null;

        /// <summary>
        /// Password for basic authentication.  Optional when <see cref="Type"/> is Basic.  Default is null.
        /// </summary>
        public string Password { get; set; } = null;

        /// <summary>
        /// Header to use for attaching an API key to the request.  Required when <see cref="Type"/> is ApiKey.  Default is null.
        /// </summary>
        public string ApiKeyHeader { get; set; } = null;

        /// <summary>
        /// API key to attach.  Required when <see cref="Type"/> is ApiKey.  Default is null.
        /// </summary>
        public string ApiKey { get; set; } = null;

        /// <summary>
        /// Bearer token to use in the authorization header.  Required when <see cref="Type"/> is BearerToken.  Default is null.
        /// </summary>
        public string BearerToken { get; set; } = null;

        /// <summary>
        /// Additional origins that receive credentials, for sites that span more than one origin.
        /// Each entry must be an absolute http or https URL; only its scheme, host and port are used, for example "https://docs.example.com".
        /// Default is an empty list.  Assigning null results in an empty list.
        /// </summary>
        public List<string> CredentialOrigins
        {
            get
            {
                return _CredentialOrigins;
            }
            set
            {
                if (value == null) value = new List<string>();
                _CredentialOrigins = value;
            }
        }

        #endregion

        #region Private-Members

        private List<string> _CredentialOrigins = new List<string>();

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

        /// <summary>
        /// Check that the settings are complete and unambiguous.
        /// Called by the <see cref="WebCrawler"/> constructor, so a bad configuration fails before a crawl starts.
        /// Empty or whitespace strings count as unset.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <see cref="Type"/> is None but a credential field is set; when the fields required by <see cref="Type"/>
        /// are missing; or when a <see cref="CredentialOrigins"/> entry is not an absolute http or https URL.
        /// </exception>
        public void Validate()
        {
            switch (Type)
            {
                case AuthenticationTypeEnum.None:
                    List<string> populated = new List<string>();
                    if (IsSet(Username)) populated.Add(nameof(Username));
                    if (IsSet(Password)) populated.Add(nameof(Password));
                    if (IsSet(BearerToken)) populated.Add(nameof(BearerToken));
                    if (IsSet(ApiKeyHeader)) populated.Add(nameof(ApiKeyHeader));
                    if (IsSet(ApiKey)) populated.Add(nameof(ApiKey));

                    if (populated.Count > 0)
                    {
                        throw new ArgumentException(
                            "Authentication.Type is None, but " + String.Join(", ", populated) + (populated.Count == 1 ? " is" : " are")
                            + " set and would never be sent.  Set Authentication.Type to Basic, ApiKey or BearerToken, or clear the credential fields.");
                    }
                    break;

                case AuthenticationTypeEnum.Basic:
                    if (!IsSet(Username))
                        throw new ArgumentException("Authentication.Type is Basic, but Username is not set.", nameof(Username));
                    break;

                case AuthenticationTypeEnum.BearerToken:
                    if (!IsSet(BearerToken))
                        throw new ArgumentException("Authentication.Type is BearerToken, but BearerToken is not set.", nameof(BearerToken));
                    break;

                case AuthenticationTypeEnum.ApiKey:
                    if (!IsSet(ApiKeyHeader))
                        throw new ArgumentException("Authentication.Type is ApiKey, but ApiKeyHeader is not set.", nameof(ApiKeyHeader));
                    if (!IsSet(ApiKey))
                        throw new ArgumentException("Authentication.Type is ApiKey, but ApiKey is not set.", nameof(ApiKey));
                    break;

                default:
                    throw new ArgumentException("Unknown authentication type " + Type.ToString() + ".", nameof(Type));
            }

            foreach (string origin in _CredentialOrigins)
            {
                Uri uri;
                if (!IsSet(origin)
                    || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out uri)
                    || String.IsNullOrEmpty(uri.Host)
                    || !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                        || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException(
                        "Authentication.CredentialOrigins entry '" + (origin ?? "null") + "' is not an absolute http or https URL.",
                        nameof(CredentialOrigins));
                }
            }
        }

        #endregion

        #region Private-Methods

        private static bool IsSet(string value)
        {
            return !String.IsNullOrWhiteSpace(value);
        }

        #endregion
    }
}

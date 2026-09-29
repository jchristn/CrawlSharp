namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Coverage for <see cref="Settings"/> and <see cref="AuthenticationSettings"/>.
    /// </summary>
    public static class SettingsSuite
    {
        private const string Id = "Settings";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Defaults", "Settings expose non-null Authentication and Crawl by default", () =>
                {
                    Settings s = new Settings();
                    Check.NotNull(s.Authentication);
                    Check.NotNull(s.Crawl);
                }),

                Case.Sync(Id, "Authentication_Null_ReplacedWithDefault", "Authentication null assignment becomes a default instance", () =>
                {
                    Settings s = new Settings();
                    s.Authentication = null;
                    Check.NotNull(s.Authentication);
                    Check.Equal(AuthenticationTypeEnum.None, s.Authentication.Type);
                }),

                Case.Sync(Id, "Authentication_Assign", "Authentication accepts an assigned instance", () =>
                {
                    Settings s = new Settings();
                    AuthenticationSettings auth = new AuthenticationSettings { Type = AuthenticationTypeEnum.Basic };
                    s.Authentication = auth;
                    Check.Equal(AuthenticationTypeEnum.Basic, s.Authentication.Type);
                }),

                Case.Sync(Id, "Crawl_Null_Throws", "Crawl rejects null", () =>
                {
                    Settings s = new Settings();
                    Check.Throws<ArgumentNullException>(() => s.Crawl = null);
                }),

                Case.Sync(Id, "Crawl_Assign", "Crawl accepts an assigned instance", () =>
                {
                    Settings s = new Settings();
                    CrawlSettings crawl = new CrawlSettings { MaxCrawlDepth = 9 };
                    s.Crawl = crawl;
                    Check.Equal(9, s.Crawl.MaxCrawlDepth);
                }),

                Case.Sync(Id, "Auth_Defaults", "AuthenticationSettings defaults are None with null credentials", () =>
                {
                    AuthenticationSettings a = new AuthenticationSettings();
                    Check.Equal(AuthenticationTypeEnum.None, a.Type);
                    Check.Null(a.Username);
                    Check.Null(a.Password);
                    Check.Null(a.ApiKeyHeader);
                    Check.Null(a.ApiKey);
                    Check.Null(a.BearerToken);
                }),

                Case.Sync(Id, "Auth_RoundTrip", "AuthenticationSettings stores assigned credentials", () =>
                {
                    AuthenticationSettings a = new AuthenticationSettings
                    {
                        Type = AuthenticationTypeEnum.BearerToken,
                        Username = "u",
                        Password = "p",
                        ApiKeyHeader = "x-api-key",
                        ApiKey = "abc",
                        BearerToken = "tok"
                    };
                    Check.Equal(AuthenticationTypeEnum.BearerToken, a.Type);
                    Check.Equal("u", a.Username);
                    Check.Equal("p", a.Password);
                    Check.Equal("x-api-key", a.ApiKeyHeader);
                    Check.Equal("abc", a.ApiKey);
                    Check.Equal("tok", a.BearerToken);
                }),

                Case.Sync(Id, "Auth_CredentialOrigins_Default", "CredentialOrigins defaults to an empty list and null becomes an empty list", () =>
                {
                    AuthenticationSettings a = new AuthenticationSettings();
                    Check.NotNull(a.CredentialOrigins);
                    Check.Empty(a.CredentialOrigins);
                    a.CredentialOrigins = null;
                    Check.NotNull(a.CredentialOrigins);
                    Check.Empty(a.CredentialOrigins);
                }),

                Case.Sync(Id, "Validate_None_Empty_Passes", "Validate accepts Type None with no credentials", () =>
                {
                    new AuthenticationSettings().Validate();
                }),

                Case.Sync(Id, "Validate_None_WithAnyCredential_Throws", "Validate rejects Type None when any single credential field is set", () =>
                {
                    Check.Throws<ArgumentException>(() => new AuthenticationSettings { Username = "u" }.Validate());
                    Check.Throws<ArgumentException>(() => new AuthenticationSettings { Password = "p" }.Validate());
                    Check.Throws<ArgumentException>(() => new AuthenticationSettings { BearerToken = "t" }.Validate());
                    Check.Throws<ArgumentException>(() => new AuthenticationSettings { ApiKeyHeader = "x-api-key" }.Validate());
                    Check.Throws<ArgumentException>(() => new AuthenticationSettings { ApiKey = "k" }.Validate());
                }),

                Case.Sync(Id, "Validate_None_Whitespace_Passes", "Validate treats empty and whitespace fields as unset", () =>
                {
                    new AuthenticationSettings { Username = "", Password = " ", BearerToken = "\t", ApiKeyHeader = "", ApiKey = "" }.Validate();
                }),

                Case.Sync(Id, "Validate_Basic", "Validate requires Username for Basic and allows an empty password", () =>
                {
                    new AuthenticationSettings { Type = AuthenticationTypeEnum.Basic, Username = "u" }.Validate();
                    new AuthenticationSettings { Type = AuthenticationTypeEnum.Basic, Username = "u", Password = "p" }.Validate();
                    ArgumentException ex = Check.Throws<ArgumentException>(() => new AuthenticationSettings { Type = AuthenticationTypeEnum.Basic, Password = "p" }.Validate());
                    Check.Equal("Username", ex.ParamName);
                }),

                Case.Sync(Id, "Validate_Bearer", "Validate requires BearerToken for BearerToken", () =>
                {
                    new AuthenticationSettings { Type = AuthenticationTypeEnum.BearerToken, BearerToken = "t" }.Validate();
                    ArgumentException ex = Check.Throws<ArgumentException>(() => new AuthenticationSettings { Type = AuthenticationTypeEnum.BearerToken, BearerToken = " " }.Validate());
                    Check.Equal("BearerToken", ex.ParamName);
                }),

                Case.Sync(Id, "Validate_ApiKey", "Validate requires both ApiKeyHeader and ApiKey for ApiKey", () =>
                {
                    new AuthenticationSettings { Type = AuthenticationTypeEnum.ApiKey, ApiKeyHeader = "x-api-key", ApiKey = "k" }.Validate();
                    ArgumentException noHeader = Check.Throws<ArgumentException>(() => new AuthenticationSettings { Type = AuthenticationTypeEnum.ApiKey, ApiKey = "k" }.Validate());
                    Check.Equal("ApiKeyHeader", noHeader.ParamName);
                    ArgumentException noKey = Check.Throws<ArgumentException>(() => new AuthenticationSettings { Type = AuthenticationTypeEnum.ApiKey, ApiKeyHeader = "x-api-key" }.Validate());
                    Check.Equal("ApiKey", noKey.ParamName);
                }),

                Case.Sync(Id, "Validate_TypeSet_OtherFieldsIgnored", "Validate allows extra fields from another credential family when Type is set", () =>
                {
                    new AuthenticationSettings { Type = AuthenticationTypeEnum.BearerToken, BearerToken = "t", Username = "u" }.Validate();
                }),

                Case.Sync(Id, "Validate_UnknownType_Throws", "Validate rejects an undefined authentication type", () =>
                {
                    Check.Throws<ArgumentException>(() => new AuthenticationSettings { Type = (AuthenticationTypeEnum)99 }.Validate());
                }),

                Case.Sync(Id, "Validate_CredentialOrigins", "Validate accepts absolute http and https origins and rejects anything else", () =>
                {
                    new AuthenticationSettings
                    {
                        Type = AuthenticationTypeEnum.Basic,
                        Username = "u",
                        CredentialOrigins = new List<string> { "https://docs.example.com", "http://127.0.0.1:8080/path" }
                    }.Validate();

                    foreach (string bad in new[] { "ftp://example.com", "example.com", "", " ", null, "mailto:a@b.c" })
                    {
                        AuthenticationSettings a = new AuthenticationSettings
                        {
                            Type = AuthenticationTypeEnum.Basic,
                            Username = "u",
                            CredentialOrigins = new List<string> { bad }
                        };

                        ArgumentException ex = Check.Throws<ArgumentException>(() => a.Validate(), "Expected '" + (bad ?? "null") + "' to be rejected.");
                        Check.Equal("CredentialOrigins", ex.ParamName);
                    }
                }),

                Case.Sync(Id, "Auth_EnumValues", "AuthenticationTypeEnum defines the expected members", () =>
                {
                    Check.Equal(0, (int)AuthenticationTypeEnum.None);
                    Check.Equal(1, (int)AuthenticationTypeEnum.Basic);
                    Check.Equal(2, (int)AuthenticationTypeEnum.ApiKey);
                    Check.Equal(3, (int)AuthenticationTypeEnum.BearerToken);
                }),
            };

            return new TestSuiteDescriptor(Id, "Settings and authentication", cases);
        }
    }
}

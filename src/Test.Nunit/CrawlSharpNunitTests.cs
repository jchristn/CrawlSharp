namespace Test.Nunit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// NUnit host for the shared CrawlSharp Touchstone suites, using TestCaseSource so each
    /// descriptor is surfaced as an individual NUnit test case.
    /// </summary>
    [TestFixture]
    public sealed class CrawlSharpNunitTests
    {
        private static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(CrawlSharpSuites.All);
        }

        /// <summary>
        /// Execute a single descriptor.
        /// </summary>
        /// <param name="testCase">Test case descriptor.</param>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}

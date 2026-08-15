namespace Test.Xunit
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using global::Xunit;
    using global::Xunit.Abstractions;

    /// <summary>
    /// xUnit host for the shared CrawlSharp Touchstone suites.  Each non-skipped descriptor becomes
    /// a separate theory row; skipped descriptors are surfaced through xUnit's skip mechanism.
    /// </summary>
    public sealed class CrawlSharpXunitTests
    {
        private readonly ITestOutputHelper _Output;

        /// <summary>
        /// Instantiate with the xUnit output helper.
        /// </summary>
        /// <param name="output">Output helper.</param>
        public CrawlSharpXunitTests(ITestOutputHelper output)
        {
            _Output = output;
        }

        /// <summary>
        /// Non-skipped test cases.
        /// </summary>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();

            foreach (TestSuiteDescriptor suite in CrawlSharpSuites.All)
                foreach (TestCaseDescriptor testCase in suite.Cases)
                    if (!testCase.Skip)
                        data.Add(testCase);

            return data;
        }

        /// <summary>
        /// Skipped test cases.
        /// </summary>
        public static TheoryData<TestCaseDescriptor> SkippedCases()
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();

            foreach (TestSuiteDescriptor suite in CrawlSharpSuites.All)
                foreach (TestCaseDescriptor testCase in suite.Cases)
                    if (testCase.Skip)
                        data.Add(testCase);

            return data;
        }

        /// <summary>
        /// Execute a single descriptor.
        /// </summary>
        /// <param name="testCase">Test case descriptor.</param>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }

        /// <summary>
        /// Report skipped descriptors through xUnit's skip mechanism.
        /// </summary>
        /// <param name="testCase">Skipped test case descriptor.</param>
        [Theory(Skip = "Dynamically skipped test cases")]
        [MemberData(nameof(SkippedCases))]
        public Task Skipped(TestCaseDescriptor testCase)
        {
            _Output.WriteLine("Skipped: " + testCase.SkipReason);
            return Task.CompletedTask;
        }
    }
}

namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Touchstone CLI runner for the CrawlSharp test suites.  Executes every suite defined in
    /// <see cref="CrawlSharpSuites.All"/> and returns a non-zero exit code if any case fails.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Optional "--results &lt;path&gt;" to export JSON results.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            string resultsPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--results", StringComparison.Ordinal) && i + 1 < args.Length)
                {
                    resultsPath = args[i + 1];
                    i++;
                }
            }

            return await ConsoleRunner.RunAsync(CrawlSharpSuites.All, resultsPath: resultsPath);
        }
    }
}

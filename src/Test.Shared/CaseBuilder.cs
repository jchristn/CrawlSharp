namespace Test.Shared
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Convenience factory methods for building <see cref="TestCaseDescriptor"/> instances
    /// from synchronous or asynchronous test bodies.
    /// </summary>
    internal static class Case
    {
        /// <summary>
        /// Build a descriptor from a synchronous test body.
        /// </summary>
        internal static TestCaseDescriptor Sync(string suiteId, string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId,
                caseId,
                displayName,
                _ =>
                {
                    body();
                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Build a descriptor from an asynchronous test body.
        /// </summary>
        internal static TestCaseDescriptor Async(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, body);
        }
    }
}

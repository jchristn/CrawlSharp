namespace Test.Shared
{
    using System;

    /// <summary>
    /// Exception thrown when a <see cref="Check"/> assertion fails.
    /// </summary>
    public class CheckException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        public CheckException(string message) : base(message)
        {
        }
    }
}

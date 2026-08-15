namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Lightweight assertion helper for Touchstone descriptors.  Each method throws
    /// <see cref="CheckException"/> when the assertion fails; Touchstone treats a thrown
    /// exception as a failed test case.
    /// </summary>
    public static class Check
    {
        /// <summary>
        /// Assert that a condition is true.
        /// </summary>
        public static void True(bool condition, string message = null)
        {
            if (!condition) throw new CheckException(message ?? "Expected condition to be true.");
        }

        /// <summary>
        /// Assert that a condition is false.
        /// </summary>
        public static void False(bool condition, string message = null)
        {
            if (condition) throw new CheckException(message ?? "Expected condition to be false.");
        }

        /// <summary>
        /// Assert that two values are equal.
        /// </summary>
        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new CheckException(message ?? ("Expected [" + Describe(expected) + "] but was [" + Describe(actual) + "]."));
        }

        /// <summary>
        /// Assert that two values are not equal.
        /// </summary>
        public static void NotEqual<T>(T notExpected, T actual, string message = null)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
                throw new CheckException(message ?? ("Expected value to differ from [" + Describe(actual) + "]."));
        }

        /// <summary>
        /// Assert that two byte arrays are equal.
        /// </summary>
        public static void BytesEqual(byte[] expected, byte[] actual, string message = null)
        {
            if (expected == null && actual == null) return;
            if (expected == null || actual == null || !expected.SequenceEqual(actual))
                throw new CheckException(message ?? "Expected byte arrays to be equal.");
        }

        /// <summary>
        /// Assert that a value is null.
        /// </summary>
        public static void Null(object value, string message = null)
        {
            if (value != null) throw new CheckException(message ?? ("Expected null but was [" + Describe(value) + "]."));
        }

        /// <summary>
        /// Assert that a value is not null.
        /// </summary>
        public static void NotNull(object value, string message = null)
        {
            if (value == null) throw new CheckException(message ?? "Expected non-null value.");
        }

        /// <summary>
        /// Assert that a string is null or empty.
        /// </summary>
        public static void Empty(string value, string message = null)
        {
            if (!String.IsNullOrEmpty(value)) throw new CheckException(message ?? ("Expected empty string but was [" + value + "]."));
        }

        /// <summary>
        /// Assert that a collection is empty.
        /// </summary>
        public static void Empty<T>(IEnumerable<T> collection, string message = null)
        {
            if (collection == null || collection.Any()) throw new CheckException(message ?? "Expected empty collection.");
        }

        /// <summary>
        /// Assert that a collection contains a given number of items.
        /// </summary>
        public static void Count<T>(int expected, IEnumerable<T> collection, string message = null)
        {
            int actual = collection == null ? -1 : collection.Count();
            if (actual != expected)
                throw new CheckException(message ?? ("Expected collection count [" + expected + "] but was [" + actual + "]."));
        }

        /// <summary>
        /// Assert that a collection contains a single item and return it.
        /// </summary>
        public static T Single<T>(IEnumerable<T> collection, string message = null)
        {
            if (collection == null) throw new CheckException(message ?? "Expected single item but collection was null.");
            List<T> items = collection.ToList();
            if (items.Count != 1) throw new CheckException(message ?? ("Expected single item but collection had [" + items.Count + "]."));
            return items[0];
        }

        /// <summary>
        /// Assert that a string contains a substring.
        /// </summary>
        public static void Contains(string substring, string value, string message = null)
        {
            if (value == null || !value.Contains(substring, StringComparison.Ordinal))
                throw new CheckException(message ?? ("Expected string to contain [" + substring + "]."));
        }

        /// <summary>
        /// Assert that a string does not contain a substring.
        /// </summary>
        public static void DoesNotContain(string substring, string value, string message = null)
        {
            if (value != null && value.Contains(substring, StringComparison.Ordinal))
                throw new CheckException(message ?? ("Expected string to not contain [" + substring + "]."));
        }

        /// <summary>
        /// Assert that a sequence contains an item matching a predicate.
        /// </summary>
        public static void Contains<T>(IEnumerable<T> collection, Func<T, bool> predicate, string message = null)
        {
            if (collection == null || !collection.Any(predicate))
                throw new CheckException(message ?? "Expected collection to contain a matching item.");
        }

        /// <summary>
        /// Assert that a sequence does not contain an item matching a predicate.
        /// </summary>
        public static void DoesNotContain<T>(IEnumerable<T> collection, Func<T, bool> predicate, string message = null)
        {
            if (collection != null && collection.Any(predicate))
                throw new CheckException(message ?? "Expected collection to not contain a matching item.");
        }

        /// <summary>
        /// Assert that a string starts with a given prefix.
        /// </summary>
        public static void StartsWith(string prefix, string value, string message = null)
        {
            if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal))
                throw new CheckException(message ?? ("Expected string to start with [" + prefix + "]."));
        }

        /// <summary>
        /// Assert that invoking an action throws an exception of type TException (or a derived type).
        /// </summary>
        public static TException Throws<TException>(Action action, string message = null) where TException : Exception
        {
            if (action == null) throw new CheckException("No action supplied to Throws.");

            try
            {
                action();
            }
            catch (TException ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new CheckException(message ?? ("Expected exception of type [" + typeof(TException).Name + "] but caught [" + ex.GetType().Name + "]: " + ex.Message));
            }

            throw new CheckException(message ?? ("Expected exception of type [" + typeof(TException).Name + "] but none was thrown."));
        }

        /// <summary>
        /// Assert that invoking an async function throws an exception of type TException (or a derived type).
        /// </summary>
        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message = null) where TException : Exception
        {
            if (action == null) throw new CheckException("No action supplied to ThrowsAsync.");

            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new CheckException(message ?? ("Expected exception of type [" + typeof(TException).Name + "] but caught [" + ex.GetType().Name + "]: " + ex.Message));
            }

            throw new CheckException(message ?? ("Expected exception of type [" + typeof(TException).Name + "] but none was thrown."));
        }

        private static string Describe(object value)
        {
            if (value == null) return "null";
            return value.ToString();
        }
    }
}

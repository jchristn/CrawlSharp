namespace Test.Shared
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One measurement captured by <see cref="TelemetryCapture"/>.
    /// </summary>
    public sealed class RecordedMeasurement
    {
        /// <summary>
        /// Instrument name.
        /// </summary>
        public string Instrument { get; set; } = null;

        /// <summary>
        /// Measured value.
        /// </summary>
        public double Value { get; set; } = 0;

        /// <summary>
        /// Labels attached to the measurement.
        /// </summary>
        public Dictionary<string, string> Tags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Return the label value for a key, or null when absent.
        /// </summary>
        public string Tag(string key)
        {
            string value;
            return Tags.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>
        /// True when every key/value pair in <paramref name="expected"/> is present on this measurement.
        /// </summary>
        public bool Matches(IDictionary<string, string> expected)
        {
            if (expected == null) return true;

            foreach (KeyValuePair<string, string> pair in expected)
            {
                if (!String.Equals(Tag(pair.Key), pair.Value, StringComparison.Ordinal)) return false;
            }

            return true;
        }
    }
}

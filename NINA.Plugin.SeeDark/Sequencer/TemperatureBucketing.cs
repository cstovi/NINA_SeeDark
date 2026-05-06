using System;

namespace NINA.Plugin.SeeDark.Sequencer {
    internal static class TemperatureBucketing {
        // Assigns temperatures into non-overlapping bands centered on bucket values.
        // Example with step=2: [15.0,17.0)->16, [17.0,19.0)->18, etc.
        public static int ToBucket(double temperatureC, int bucketStepC) {
            int step = Math.Max(1, bucketStepC);
            double halfStep = step / 2.0;
            return (int)(Math.Floor((temperatureC + halfStep) / step) * step);
        }
    }
}

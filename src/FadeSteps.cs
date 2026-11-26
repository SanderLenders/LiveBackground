using System;

namespace LiveBackground
{
    // The "Allow half-second steps" checkbox changes the slider granularity,
    // not the available range. One-second steps remain the default.
    public static class FadeSteps
    {
        public const double FineStep = 0.5;
        public const double CoarseStep = 1.0;
        // Snapping is anchored to the slider minimum. The video slider only
        // starts at half a second while fine steps are on; with coarse steps
        // its minimum must be a whole second, otherwise a 1 s grid would land
        // on 0.5, 1.5, 2.5 ... instead of 1, 2, 3 ...
        public const double VideoFineMinimum = 0.5;
        public const double VideoCoarseMinimum = 1.0;
        public const double ImageMinimum = 0.0;

        public static double Step(bool fine)
        {
            return fine ? FineStep : CoarseStep;
        }

        public static double Snap(double value, double minimum, double maximum, bool fine)
        {
            if (double.IsNaN(value)) return minimum;
            double step = Step(fine);
            double snapped = minimum + Math.Round((value - minimum) / step, MidpointRounding.AwayFromZero) * step;
            if (snapped < minimum) snapped = minimum;
            if (snapped > maximum) snapped = maximum;
            return snapped;
        }
    }
}

namespace GuitarBassTuner.Audio;

internal sealed class PitchStabilizer
{
    private const double JumpThresholdCents = 15;
    private const double CandidateToleranceCents = 20;
    private const int MedianWindow = 5;
    private const int OnsetFrames = 2;
    // Analyses are 30 ms apart, so the fourth observation confirms a 90 ms jump.
    private const int JumpFrames = 4;
    private const int SilenceFrames = 2;

    private readonly double[] nearbyFrequencies = new double[MedianWindow];
    private double? output;
    private double candidateFrequency;
    private int candidateFrames;
    private int nearbyIndex;
    private int silentFrames;

    public double? Update(double? estimate)
    {
        if (estimate is not double frequency)
        {
            candidateFrames = 0;
            if (output.HasValue && ++silentFrames >= SilenceFrames)
                Reset();
            return output;
        }

        silentFrames = 0;
        if (output is double previous && IsNear(frequency, previous, JumpThresholdCents))
        {
            candidateFrames = 0;
            output = MedianWith(frequency);
            return output;
        }

        if (candidateFrames > 0 && !IsNear(frequency, candidateFrequency, CandidateToleranceCents))
            candidateFrames = 0;
        candidateFrequency = frequency;

        if (++candidateFrames >= (output.HasValue ? JumpFrames : OnsetFrames))
        {
            Array.Fill(nearbyFrequencies, frequency);
            nearbyIndex = 0;
            output = frequency;
            candidateFrames = 0;
        }
        return output;
    }

    public void Reset()
    {
        output = null;
        candidateFrames = 0;
        silentFrames = 0;
    }

    private double MedianWith(double frequency)
    {
        nearbyFrequencies[nearbyIndex] = frequency;
        nearbyIndex = (nearbyIndex + 1) % MedianWindow;
        Span<double> sorted = stackalloc double[MedianWindow];
        nearbyFrequencies.AsSpan().CopyTo(sorted);
        sorted.Sort();
        return sorted[MedianWindow / 2];
    }

    private static bool IsNear(double frequency, double reference, double tolerance) =>
        Math.Abs(Tuning.Cents(frequency, reference)) <= tolerance;
}

using System.Numerics;

namespace GuitarBassTuner.Audio;

internal readonly record struct PitchReading(long Timestamp, double? Frequency, double Rms);

internal sealed class PitchTracker
{
    private const double MinimumFrequency = 23;
    private const double MaximumFrequency = 720;
    private const double MinimumRms = 0.0012;
    private readonly int decimation;
    private readonly double analysisRate;
    private readonly float highPass;
    private readonly int minimumLag;
    private readonly int maximumLag;
    private readonly int comparisonLength;
    private readonly int hop;
    private readonly float[] ring;
    private readonly float[] frame;
    private readonly float[] difference;
    private int writeIndex;
    private int untilAnalysis;
    private int samplesToAverage;
    private float sampleSum;
    private float previousInput;
    private float previousOutput;
    private readonly PitchStabilizer stabilizer = new();

    public PitchTracker(int sampleRate)
    {
        decimation = Math.Max(1, (int)Math.Round(sampleRate / 12000.0));
        analysisRate = (double)sampleRate / decimation;
        highPass = (float)Math.Exp(-2 * Math.PI * 12 / analysisRate);
        minimumLag = Math.Max(2, (int)Math.Floor(analysisRate / MaximumFrequency));
        maximumLag = (int)Math.Ceiling(analysisRate / MinimumFrequency);
        comparisonLength = (int)Math.Ceiling(analysisRate * 0.060);
        hop = (int)Math.Round(analysisRate * 0.030);
        ring = new float[comparisonLength + maximumLag + 1];
        frame = new float[ring.Length];
        difference = new float[maximumLag + 2];
        Reset();
    }

    public void Reset()
    {
        writeIndex = 0;
        untilAnalysis = ring.Length;
        samplesToAverage = decimation;
        sampleSum = 0;
        previousInput = 0;
        previousOutput = 0;
        stabilizer.Reset();
    }

    public PitchReading? AddSamples(ReadOnlySpan<float> samples, long timestamp)
    {
        PitchReading? result = null;
        foreach (float sample in samples)
        {
            sampleSum += sample;
            if (--samplesToAverage > 0)
                continue;

            float input = sampleSum / decimation;
            sampleSum = 0;
            samplesToAverage = decimation;
            float filtered = input - previousInput + highPass * previousOutput;
            previousInput = input;
            previousOutput = filtered;

            ring[writeIndex++] = filtered;
            if (writeIndex == ring.Length)
                writeIndex = 0;
            if (--untilAnalysis > 0)
                continue;

            untilAnalysis = hop;
            ring.AsSpan(writeIndex).CopyTo(frame);
            ring.AsSpan(0, writeIndex).CopyTo(frame.AsSpan(ring.Length - writeIndex));
            result = Analyze(timestamp);
        }
        return result;
    }

    private PitchReading Analyze(long timestamp)
    {
        double energy = 0;
        foreach (float sample in frame.AsSpan(frame.Length - hop))
            energy += sample * sample;
        double rms = Math.Sqrt(energy / hop);

        double? estimate = rms >= MinimumRms ? EstimateFrequency() : null;
        return new(timestamp, stabilizer.Update(estimate), rms);
    }

    private double? EstimateFrequency()
    {
        // YIN normalizes each difference by the mean at shorter lags.
        float runningSum = 0;
        difference[0] = 1;
        for (int lag = 1; lag < difference.Length; lag++)
        {
            float sum = DifferenceAt(lag);
            runningSum += sum;
            difference[lag] = runningSum > 0 ? sum * lag / runningSum : 1;
        }

        int best = -1;
        float bestScore = 0.19f;
        for (int lag = minimumLag; lag <= maximumLag; lag++)
        {
            float score = difference[lag];
            if (score >= bestScore || score > difference[lag - 1] || score >= difference[lag + 1])
                continue;

            best = lag;
            bestScore = score;
            if (score < 0.11f)
                break;
        }
        if (best < 0)
            return null;

        // A strong second harmonic can produce a shallower minimum at half the period.
        int doubled = best * 2;
        if (bestScore > 0.028f && doubled + 3 <= maximumLag)
        {
            int octaveCandidate = doubled;
            for (int lag = doubled - 3; lag <= doubled + 3; lag++)
            {
                if (difference[lag] < difference[octaveCandidate])
                    octaveCandidate = lag;
            }
            if (difference[octaveCandidate] < bestScore * 0.42f)
                best = octaveCandidate;
        }

        double left = difference[best - 1];
        double center = difference[best];
        double right = difference[best + 1];
        double curvature = left - 2 * center + right;
        double correction = curvature > 0 ? Math.Clamp((left - right) / (2 * curvature), -0.5, 0.5) : 0;
        double frequency = analysisRate / (best + correction);
        return frequency >= MinimumFrequency && frequency <= MaximumFrequency ? frequency : null;
    }

    private float DifferenceAt(int lag)
    {
        Vector<float> energy = Vector<float>.Zero;
        int index = 0;
        for (; index <= comparisonLength - Vector<float>.Count; index += Vector<float>.Count)
        {
            var delta = new Vector<float>(frame, index) - new Vector<float>(frame, index + lag);
            energy += delta * delta;
        }

        float sum = Vector.Sum(energy);
        for (; index < comparisonLength; index++)
        {
            float delta = frame[index] - frame[index + lag];
            sum += delta * delta;
        }
        return sum;
    }
}

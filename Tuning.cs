namespace GuitarBassTuner;

internal enum Instrument { Guitar, Bass }

internal sealed record Tuning(string Name, int[] Notes, bool PreferFlats = false)
{
    private static readonly Tuning[] Guitar =
    [
        new("标准调弦", [40, 45, 50, 55, 59, 64]),
        new("全弦降半音", [39, 44, 49, 54, 58, 63], PreferFlats: true),
        new("全弦降全音", [38, 43, 48, 53, 57, 62]),
        new("Drop D", [38, 45, 50, 55, 59, 64]),
        new("Drop C", [36, 43, 48, 53, 57, 62]),
        new("DADGAD", [38, 45, 50, 55, 57, 62]),
        new("Open G", [38, 43, 50, 55, 59, 62])
    ];

    private static readonly Tuning[] Bass =
    [
        new("标准调弦", [28, 33, 38, 43]),
        new("全弦降半音", [27, 32, 37, 42], PreferFlats: true),
        new("全弦降全音", [26, 31, 36, 41]),
        new("Drop D", [26, 33, 38, 43]),
        new("Drop C", [24, 31, 36, 41])
    ];

    private static readonly string[] SharpNames = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
    private static readonly string[] FlatNames = ["C", "D♭", "D", "E♭", "E", "F", "G♭", "G", "A♭", "A", "B♭", "B"];

    public static IReadOnlyList<Tuning> For(Instrument instrument) => instrument == Instrument.Guitar ? Guitar : Bass;

    public static double Frequency(int midiNote) => 440 * Math.Pow(2, (midiNote - 69) / 12.0);

    public static double Midi(double frequency) => 69 + 12 * Math.Log2(frequency / 440);

    public static double Cents(double frequency, double target) => 1200 * Math.Log2(frequency / target);

    public string NoteName(int midiNote)
    {
        int octave = (int)Math.Floor(midiNote / 12.0) - 1;
        int pitchClass = midiNote - (octave + 1) * 12;
        return (PreferFlats ? FlatNames : SharpNames)[pitchClass] + octave;
    }

    public int NearestString(double frequency)
    {
        double pitch = Midi(frequency);
        int nearest = 0;
        double distance = Math.Abs(pitch - Notes[0]);
        for (int index = 1; index < Notes.Length; index++)
        {
            double candidate = Math.Abs(pitch - Notes[index]);
            if (candidate >= distance)
                continue;

            nearest = index;
            distance = candidate;
        }
        return nearest;
    }
}

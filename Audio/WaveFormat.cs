using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace GuitarBassTuner.Audio;

internal readonly record struct WaveFormat(int SampleRate, int Channels, int Bits, int BlockAlign, bool FloatingPoint)
{
    private static readonly Guid PcmFormat = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid FloatFormat = new("00000003-0000-0010-8000-00AA00389B71");

    public static WaveFormat Read(IntPtr pointer)
    {
        int tag = (ushort)Marshal.ReadInt16(pointer, 0);
        int channels = (ushort)Marshal.ReadInt16(pointer, 2);
        int rate = Marshal.ReadInt32(pointer, 4);
        int align = (ushort)Marshal.ReadInt16(pointer, 12);
        int bits = (ushort)Marshal.ReadInt16(pointer, 14);
        if (tag == 0xfffe && Marshal.ReadInt16(pointer, 16) >= 22)
        {
            Guid subFormat = Marshal.PtrToStructure<Guid>(pointer + 24);
            tag = subFormat == PcmFormat ? 1 : subFormat == FloatFormat ? 3 : 0;
        }

        bool supported = (tag == 1 && bits is 8 or 16 or 24 or 32) ||
                         (tag == 3 && bits is 32 or 64);
        if (!supported || rate <= 0 || channels <= 0 || align != channels * (bits / 8))
            throw new NotSupportedException("Unsupported WASAPI capture format.");

        return new(rate, channels, bits, align, tag == 3);
    }

    public unsafe void DecodeMono(IntPtr data, Span<float> destination)
    {
        var bytes = new ReadOnlySpan<byte>(data.ToPointer(), destination.Length * BlockAlign);
        int sampleSize = Bits / 8;
        int offset = 0;
        for (int frame = 0; frame < destination.Length; frame++)
        {
            float sum = 0;
            for (int channel = 0; channel < Channels; channel++, offset += sampleSize)
                sum += DecodeSample(bytes.Slice(offset, sampleSize));

            float sample = sum / Channels;
            destination[frame] = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
        }
    }

    private float DecodeSample(ReadOnlySpan<byte> data)
    {
        if (FloatingPoint)
            return Bits == 32 ? BinaryPrimitives.ReadSingleLittleEndian(data) : (float)BinaryPrimitives.ReadDoubleLittleEndian(data);

        return Bits switch
        {
            8 => (data[0] - 128) / 128f,
            16 => BinaryPrimitives.ReadInt16LittleEndian(data) / 32768f,
            24 => (data[0] | (data[1] << 8) | ((sbyte)data[2] << 16)) / 8388608f,
            _ => BinaryPrimitives.ReadInt32LittleEndian(data) / 2147483648f
        };
    }
}

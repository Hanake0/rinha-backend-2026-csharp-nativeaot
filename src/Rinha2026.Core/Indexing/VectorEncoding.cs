using System.Buffers.Binary;

namespace Rinha2026.Core.Indexing;

public static class VectorEncoding {
	public static void EncodeFloat16(ReadOnlySpan<float> source, Span<byte> destination) {
		int requiredLength = checked(source.Length * sizeof(ushort));

		if (destination.Length < requiredLength) {
			throw new ArgumentException("Destination span is too small for float16 encoding.", nameof(destination));
		}

		for (int index = 0; index < source.Length; index++) {
			ushort bits = BitConverter.HalfToUInt16Bits((Half)source[index]);
			BinaryPrimitives.WriteUInt16LittleEndian(destination[(index * sizeof(ushort))..], bits);
		}
	}

	public static void EncodeQ8Symmetric(ReadOnlySpan<float> source, Span<byte> destination) {
		if (destination.Length < source.Length) {
			throw new ArgumentException("Destination span is too small for q8 encoding.", nameof(destination));
		}

		for (int index = 0; index < source.Length; index++) {
			sbyte quantized = QuantizeQ8Symmetric(source[index]);
			destination[index] = unchecked((byte)quantized);
		}
	}

	public static sbyte QuantizeQ8Symmetric(float value) {
		float clamped = Math.Clamp(value, -1f, 1f);
		int rounded = (int)MathF.Round(clamped * 127f, MidpointRounding.AwayFromZero);
		int saturated = Math.Clamp(rounded, -127, 127);
		return unchecked((sbyte)saturated);
	}
}

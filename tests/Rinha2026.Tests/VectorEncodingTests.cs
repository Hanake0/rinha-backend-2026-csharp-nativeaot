using Rinha2026.Core.Indexing;

namespace Rinha2026.Tests;

public sealed class VectorEncodingTests {
	[Fact]
	public void EncodeFloat16WritesExpectedByteCount() {
		float[] source = [0f, 0.5f, 1f, -1f];
		byte[] destination = new byte[source.Length * sizeof(ushort)];

		VectorEncoding.EncodeFloat16(source, destination);

		Assert.Equal(8, destination.Length);
	}

	[Theory]
	[InlineData(-1f, -127)]
	[InlineData(0f, 0)]
	[InlineData(1f, 127)]
	[InlineData(2f, 127)]
	public void QuantizeQ8SymmetricClampsAndRounds(float value, int expected) {
		sbyte actual = VectorEncoding.QuantizeQ8Symmetric(value);

		Assert.Equal(expected, actual);
	}
}

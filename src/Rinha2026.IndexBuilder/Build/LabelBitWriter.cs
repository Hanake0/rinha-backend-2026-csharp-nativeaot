namespace Rinha2026.IndexBuilder.Build;

public sealed class LabelBitWriter : IAsyncDisposable {
	private readonly Stream output;
	private readonly byte[] singleByteBuffer = new byte[1];
	private byte currentByte;
	private int bitCount;

	public LabelBitWriter(Stream output) {
		this.output = output ?? throw new ArgumentNullException(nameof(output));
	}

	public async ValueTask DisposeAsync() {
		await this.FlushAsync(CancellationToken.None);
		await this.output.DisposeAsync();
	}

	public async ValueTask FlushAsync(CancellationToken cancellationToken) {
		if (this.bitCount == 0) {
			return;
		}

		this.singleByteBuffer[0] = this.currentByte;
		await this.output.WriteAsync(this.singleByteBuffer, cancellationToken);
		this.currentByte = 0;
		this.bitCount = 0;
	}

	public async ValueTask WriteAsync(bool isFraud, CancellationToken cancellationToken) {
		if (isFraud) {
			this.currentByte |= (byte)(1 << this.bitCount);
		}

		this.bitCount++;

		if (this.bitCount < 8) {
			return;
		}

		this.singleByteBuffer[0] = this.currentByte;
		await this.output.WriteAsync(this.singleByteBuffer, cancellationToken);
		this.currentByte = 0;
		this.bitCount = 0;
	}
}

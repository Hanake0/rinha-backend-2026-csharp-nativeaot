using System.Globalization;
using System.Text;

using Rinha2026.Core.Configuration;

namespace Rinha2026.Api.Services;

public sealed class FraudResponseCache {
	private readonly ReadOnlyMemory<byte>[] responses;

	public FraudResponseCache(int topK, double approvalThreshold) {
		if (topK <= 0) {
			throw new ArgumentOutOfRangeException(nameof(topK));
		}

		int minDeniedCount = RuntimeDetectionConfig.ComputeMinDeniedCount(topK, approvalThreshold);
		this.responses = new ReadOnlyMemory<byte>[topK + 1];

		for (int fraudCount = 0; fraudCount <= topK; fraudCount++) {
			double fraudScore = fraudCount / (double)topK;
			bool approved = fraudCount < minDeniedCount;
			string fraudScoreText = fraudScore.ToString("0.################", CultureInfo.InvariantCulture);

			if (!fraudScoreText.Contains('.', StringComparison.Ordinal)) {
				fraudScoreText += ".0";
			}

			this.responses[fraudCount] = Encoding.UTF8.GetBytes(
				$"{{\"approved\":{(approved ? "true" : "false")},\"fraud_score\":{fraudScoreText}}}");
		}
	}

	public ReadOnlyMemory<byte> GetResponse(int fraudCount) => this.responses[fraudCount];
}

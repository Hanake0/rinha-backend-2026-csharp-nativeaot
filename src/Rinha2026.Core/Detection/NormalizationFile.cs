using System.Text.Json.Serialization;

namespace Rinha2026.Core.Detection;

public sealed class NormalizationFile {
	[JsonPropertyName("amount_vs_avg_ratio")]
	public double AmountVsAverageRatio { get; init; }

	[JsonPropertyName("max_amount")]
	public double MaxAmount { get; init; }

	[JsonPropertyName("max_installments")]
	public double MaxInstallments { get; init; }

	[JsonPropertyName("max_km")]
	public double MaxKilometers { get; init; }

	[JsonPropertyName("max_merchant_avg_amount")]
	public double MaxMerchantAverageAmount { get; init; }

	[JsonPropertyName("max_minutes")]
	public double MaxMinutes { get; init; }

	[JsonPropertyName("max_tx_count_24h")]
	public double MaxTransactionCountLast24Hours { get; init; }
}

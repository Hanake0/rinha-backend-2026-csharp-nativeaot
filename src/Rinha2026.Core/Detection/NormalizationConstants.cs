namespace Rinha2026.Core.Detection;

public readonly record struct NormalizationConstants(
	double AmountVsAverageRatio,
	double MaxAmount,
	double MaxInstallments,
	double MaxKilometers,
	double MaxMerchantAverageAmount,
	double MaxMinutes,
	double MaxTransactionCountLast24Hours) {
	public static NormalizationConstants Default { get; } = new(
		AmountVsAverageRatio: 10d,
		MaxAmount: 10_000d,
		MaxInstallments: 12d,
		MaxKilometers: 1_000d,
		MaxMerchantAverageAmount: 10_000d,
		MaxMinutes: 1_440d,
		MaxTransactionCountLast24Hours: 20d);
}

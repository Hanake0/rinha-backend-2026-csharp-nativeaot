using Rinha2026.Core.Detection;
using Rinha2026.Core.Model;

namespace Rinha2026.Core.Vectorization;

public static class FraudVectorizer {
	public const int Dimension = 14;

	public const int PaddedDimension = 16;

	public static void WriteVector(
		in FraudRequest request,
		in NormalizationConstants normalization,
		MccRiskTable mccRiskTable,
		Span<float> destination) {
		ArgumentNullException.ThrowIfNull(mccRiskTable);

		if (destination.Length < PaddedDimension) {
			throw new ArgumentException("Destination span must be at least 16 floats long.", nameof(destination));
		}

		destination[0] = NormalizeAndClamp(request.Transaction.Amount, normalization.MaxAmount);
		destination[1] = NormalizeAndClamp(request.Transaction.Installments, normalization.MaxInstallments);
		destination[2] = NormalizeAndClamp(
			request.Transaction.Amount / request.Customer.AverageAmount,
			normalization.AmountVsAverageRatio);
		destination[3] = (float)(request.Transaction.RequestedAt.Hour / 23d);
		destination[4] = (float)(request.Transaction.RequestedAt.GetMondayBasedDayOfWeek() / 6d);

		if (request.LastTransaction.HasValue) {
			destination[5] = NormalizeAndClamp(
				request.Transaction.RequestedAt.GetTotalMinutesSince(request.LastTransaction.Timestamp),
				normalization.MaxMinutes);
			destination[6] = NormalizeAndClamp(request.LastTransaction.KilometersFromCurrent, normalization.MaxKilometers);
		} else {
			destination[5] = -1f;
			destination[6] = -1f;
		}

		destination[7] = NormalizeAndClamp(request.Terminal.KilometersFromHome, normalization.MaxKilometers);
		destination[8] = NormalizeAndClamp(request.Customer.TransactionCountLast24Hours, normalization.MaxTransactionCountLast24Hours);
		destination[9] = request.Terminal.IsOnline ? 1f : 0f;
		destination[10] = request.Terminal.CardPresent ? 1f : 0f;
		destination[11] = request.Customer.KnowsMerchant(request.Merchant.Id) ? 0f : 1f;
		destination[12] = mccRiskTable.GetRisk(request.Merchant.Mcc);
		destination[13] = NormalizeAndClamp(request.Merchant.AverageAmount, normalization.MaxMerchantAverageAmount);
		destination[14] = 0f;
		destination[15] = 0f;
	}

	private static float NormalizeAndClamp(double value, double maxValue) {
		double normalized = value / maxValue;

		if (normalized < 0d) {
			return 0f;
		}

		if (normalized > 1d) {
			return 1f;
		}

		return (float)normalized;
	}
}

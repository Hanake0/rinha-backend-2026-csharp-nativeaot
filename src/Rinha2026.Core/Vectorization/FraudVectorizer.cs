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

		destination[0] = NormalizeClampAndRound4(request.Transaction.Amount, normalization.MaxAmount);
		destination[1] = NormalizeClampAndRound4(request.Transaction.Installments, normalization.MaxInstallments);
		destination[2] = NormalizeClampAndRound4(
			request.Transaction.Amount / request.Customer.AverageAmount,
			normalization.AmountVsAverageRatio);
		destination[3] = Round4(request.Transaction.RequestedAt.Hour / 23d);
		destination[4] = Round4(request.Transaction.RequestedAt.GetMondayBasedDayOfWeek() / 6d);

		if (request.LastTransaction.HasValue) {
			destination[5] = NormalizeClampAndRound4(
				request.Transaction.RequestedAt.GetTotalMinutesSince(request.LastTransaction.Timestamp),
				normalization.MaxMinutes);
			destination[6] = NormalizeClampAndRound4(request.LastTransaction.KilometersFromCurrent, normalization.MaxKilometers);
		} else {
			destination[5] = -1f;
			destination[6] = -1f;
		}

		destination[7] = NormalizeClampAndRound4(request.Terminal.KilometersFromHome, normalization.MaxKilometers);
		destination[8] = NormalizeClampAndRound4(request.Customer.TransactionCountLast24Hours, normalization.MaxTransactionCountLast24Hours);
		destination[9] = request.Terminal.IsOnline ? 1f : 0f;
		destination[10] = request.Terminal.CardPresent ? 1f : 0f;
		destination[11] = request.Customer.KnowsMerchant(request.Merchant.Id) ? 0f : 1f;
		destination[12] = Round4(mccRiskTable.GetRisk(request.Merchant.Mcc));
		destination[13] = NormalizeClampAndRound4(request.Merchant.AverageAmount, normalization.MaxMerchantAverageAmount);
		destination[14] = 0f;
		destination[15] = 0f;
	}

	private static float NormalizeClampAndRound4(double value, double maxValue) {
		double normalized = value / maxValue;

		if (normalized < 0d) {
			return 0f;
		}

		if (normalized > 1d) {
			return 1f;
		}

		return Round4(normalized);
	}

	private static float Round4(double value) {
		return (float)(Math.Round(value * 10000d, MidpointRounding.AwayFromZero) * 0.0001d);
	}
}

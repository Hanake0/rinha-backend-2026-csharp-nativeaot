namespace Rinha2026.Core.Model;

public readonly record struct FraudRequest(
	TransactionData Transaction,
	CustomerData Customer,
	MerchantData Merchant,
	TerminalData Terminal,
	LastTransactionData LastTransaction);

public readonly record struct TransactionData(
	double Amount,
	int Installments,
	CompactTimestamp RequestedAt);

public readonly record struct CustomerData(
	double AverageAmount,
	MerchantIdList KnownMerchants,
	int TransactionCountLast24Hours) {
	public bool KnowsMerchant(int merchantId) => this.KnownMerchants.Contains(merchantId);
}

public readonly record struct MerchantData(
	double AverageAmount,
	int Id,
	int Mcc);

public readonly record struct TerminalData(
	bool CardPresent,
	bool IsOnline,
	double KilometersFromHome);

public readonly record struct LastTransactionData(
	bool HasValue,
	double KilometersFromCurrent,
	CompactTimestamp Timestamp);

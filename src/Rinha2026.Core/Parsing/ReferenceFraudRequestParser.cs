using System.Text.Json;

using Rinha2026.Core.Model;

namespace Rinha2026.Core.Parsing;

public static class ReferenceFraudRequestParser {
	public static bool TryParse(ReadOnlySpan<byte> utf8Json, out FraudRequest request) {
		Utf8JsonReader reader = new(utf8Json, isFinalBlock: true, state: default);
		request = default;

		if (!reader.Read() || (reader.TokenType != JsonTokenType.StartObject)) {
			return false;
		}

		TransactionData transaction = default;
		CustomerData customer = default;
		MerchantData merchant = default;
		TerminalData terminal = default;
		LastTransactionData lastTransaction = default;

		bool hasTransaction = false;
		bool hasCustomer = false;
		bool hasMerchant = false;
		bool hasTerminal = false;
		bool hasLastTransaction = false;

		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (!hasTransaction || !hasCustomer || !hasMerchant || !hasTerminal || !hasLastTransaction) {
					return false;
				}

				request = new FraudRequest(transaction, customer, merchant, terminal, lastTransaction);
				return true;
			}

			if (reader.TokenType != JsonTokenType.PropertyName) {
				return false;
			}

			if (reader.ValueTextEquals("id"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.String)) {
					return false;
				}

				continue;
			}

			if (reader.ValueTextEquals("transaction"u8)) {
				if (!TryReadTransaction(ref reader, out transaction)) {
					return false;
				}

				hasTransaction = true;
				continue;
			}

			if (reader.ValueTextEquals("customer"u8)) {
				if (!TryReadCustomer(ref reader, out customer)) {
					return false;
				}

				hasCustomer = true;
				continue;
			}

			if (reader.ValueTextEquals("merchant"u8)) {
				if (!TryReadMerchant(ref reader, out merchant)) {
					return false;
				}

				hasMerchant = true;
				continue;
			}

			if (reader.ValueTextEquals("terminal"u8)) {
				if (!TryReadTerminal(ref reader, out terminal)) {
					return false;
				}

				hasTerminal = true;
				continue;
			}

			if (reader.ValueTextEquals("last_transaction"u8)) {
				if (!TryReadLastTransaction(ref reader, out lastTransaction)) {
					return false;
				}

				hasLastTransaction = true;
				continue;
			}

			if (!reader.Read()) {
				return false;
			}

			reader.Skip();
		}

		return false;
	}

	private static bool TryReadCustomer(ref Utf8JsonReader reader, out CustomerData customer) {
		customer = default;

		if (!reader.Read() || (reader.TokenType != JsonTokenType.StartObject)) {
			return false;
		}

		double averageAmount = default;
		int transactionCountLast24Hours = default;
		MerchantIdList knownMerchants = default;

		bool hasAverageAmount = false;
		bool hasTransactionCountLast24Hours = false;
		bool hasKnownMerchants = false;

		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (!hasAverageAmount || !hasTransactionCountLast24Hours || !hasKnownMerchants) {
					return false;
				}

				customer = new CustomerData(averageAmount, knownMerchants, transactionCountLast24Hours);
				return true;
			}

			if (reader.TokenType != JsonTokenType.PropertyName) {
				return false;
			}

			if (reader.ValueTextEquals("avg_amount"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.Number)) {
					return false;
				}

				averageAmount = reader.GetDouble();
				hasAverageAmount = true;
				continue;
			}

			if (reader.ValueTextEquals("tx_count_24h"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.Number)) {
					return false;
				}

				transactionCountLast24Hours = reader.GetInt32();
				hasTransactionCountLast24Hours = true;
				continue;
			}

			if (reader.ValueTextEquals("known_merchants"u8)) {
				if (!TryReadKnownMerchants(ref reader, out knownMerchants)) {
					return false;
				}

				hasKnownMerchants = true;
				continue;
			}

			if (!reader.Read()) {
				return false;
			}

			reader.Skip();
		}

		return false;
	}

	private static bool TryReadKnownMerchants(ref Utf8JsonReader reader, out MerchantIdList knownMerchants) {
		knownMerchants = default;

		if (!reader.Read() || (reader.TokenType != JsonTokenType.StartArray)) {
			return false;
		}

		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndArray) {
				return true;
			}

			if (reader.TokenType != JsonTokenType.String) {
				return false;
			}

			if (!TryParseMerchantId(reader.ValueSpan, out int merchantId) || !knownMerchants.Add(merchantId)) {
				return false;
			}
		}

		return false;
	}

	private static bool TryReadLastTransaction(ref Utf8JsonReader reader, out LastTransactionData lastTransaction) {
		lastTransaction = default;

		if (!reader.Read()) {
			return false;
		}

		if (reader.TokenType == JsonTokenType.Null) {
			lastTransaction = new LastTransactionData(false, 0d, default);
			return true;
		}

		if (reader.TokenType != JsonTokenType.StartObject) {
			return false;
		}

		double kilometersFromCurrent = default;
		CompactTimestamp timestamp = default;

		bool hasKilometersFromCurrent = false;
		bool hasTimestamp = false;

		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (!hasKilometersFromCurrent || !hasTimestamp) {
					return false;
				}

				lastTransaction = new LastTransactionData(true, kilometersFromCurrent, timestamp);
				return true;
			}

			if (reader.TokenType != JsonTokenType.PropertyName) {
				return false;
			}

			if (reader.ValueTextEquals("timestamp"u8)) {
				if (!TryReadTimestampValue(ref reader, out timestamp)) {
					return false;
				}

				hasTimestamp = true;
				continue;
			}

			if (reader.ValueTextEquals("km_from_current"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.Number)) {
					return false;
				}

				kilometersFromCurrent = reader.GetDouble();
				hasKilometersFromCurrent = true;
				continue;
			}

			if (!reader.Read()) {
				return false;
			}

			reader.Skip();
		}

		return false;
	}

	private static bool TryReadMerchant(ref Utf8JsonReader reader, out MerchantData merchant) {
		merchant = default;

		if (!reader.Read() || (reader.TokenType != JsonTokenType.StartObject)) {
			return false;
		}

		double averageAmount = default;
		int id = default;
		int mcc = default;

		bool hasAverageAmount = false;
		bool hasId = false;
		bool hasMcc = false;

		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (!hasAverageAmount || !hasId || !hasMcc) {
					return false;
				}

				merchant = new MerchantData(averageAmount, id, mcc);
				return true;
			}

			if (reader.TokenType != JsonTokenType.PropertyName) {
				return false;
			}

			if (reader.ValueTextEquals("id"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.String)) {
					return false;
				}

				if (!TryParseMerchantId(reader.ValueSpan, out id)) {
					return false;
				}

				hasId = true;
				continue;
			}

			if (reader.ValueTextEquals("mcc"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.String)) {
					return false;
				}

				if (!TryParsePositiveAsciiInt(reader.ValueSpan, out mcc)) {
					return false;
				}

				hasMcc = true;
				continue;
			}

			if (reader.ValueTextEquals("avg_amount"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.Number)) {
					return false;
				}

				averageAmount = reader.GetDouble();
				hasAverageAmount = true;
				continue;
			}

			if (!reader.Read()) {
				return false;
			}

			reader.Skip();
		}

		return false;
	}

	private static bool TryReadTerminal(ref Utf8JsonReader reader, out TerminalData terminal) {
		terminal = default;

		if (!reader.Read() || (reader.TokenType != JsonTokenType.StartObject)) {
			return false;
		}

		bool cardPresent = default;
		bool isOnline = default;
		double kilometersFromHome = default;

		bool hasCardPresent = false;
		bool hasIsOnline = false;
		bool hasKilometersFromHome = false;

		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (!hasCardPresent || !hasIsOnline || !hasKilometersFromHome) {
					return false;
				}

				terminal = new TerminalData(cardPresent, isOnline, kilometersFromHome);
				return true;
			}

			if (reader.TokenType != JsonTokenType.PropertyName) {
				return false;
			}

			if (reader.ValueTextEquals("card_present"u8)) {
				if (!reader.Read() || ((reader.TokenType != JsonTokenType.False) && (reader.TokenType != JsonTokenType.True))) {
					return false;
				}

				cardPresent = reader.GetBoolean();
				hasCardPresent = true;
				continue;
			}

			if (reader.ValueTextEquals("is_online"u8)) {
				if (!reader.Read() || ((reader.TokenType != JsonTokenType.False) && (reader.TokenType != JsonTokenType.True))) {
					return false;
				}

				isOnline = reader.GetBoolean();
				hasIsOnline = true;
				continue;
			}

			if (reader.ValueTextEquals("km_from_home"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.Number)) {
					return false;
				}

				kilometersFromHome = reader.GetDouble();
				hasKilometersFromHome = true;
				continue;
			}

			if (!reader.Read()) {
				return false;
			}

			reader.Skip();
		}

		return false;
	}

	private static bool TryReadTimestampValue(ref Utf8JsonReader reader, out CompactTimestamp timestamp) {
		timestamp = default;

		if (!reader.Read() || (reader.TokenType != JsonTokenType.String)) {
			return false;
		}

		return CompactTimestamp.TryParseIso8601Zulu(reader.ValueSpan, out timestamp);
	}

	private static bool TryReadTransaction(ref Utf8JsonReader reader, out TransactionData transaction) {
		transaction = default;

		if (!reader.Read() || (reader.TokenType != JsonTokenType.StartObject)) {
			return false;
		}

		double amount = default;
		int installments = default;
		CompactTimestamp requestedAt = default;

		bool hasAmount = false;
		bool hasInstallments = false;
		bool hasRequestedAt = false;

		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (!hasAmount || !hasInstallments || !hasRequestedAt) {
					return false;
				}

				transaction = new TransactionData(amount, installments, requestedAt);
				return true;
			}

			if (reader.TokenType != JsonTokenType.PropertyName) {
				return false;
			}

			if (reader.ValueTextEquals("amount"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.Number)) {
					return false;
				}

				amount = reader.GetDouble();
				hasAmount = true;
				continue;
			}

			if (reader.ValueTextEquals("installments"u8)) {
				if (!reader.Read() || (reader.TokenType != JsonTokenType.Number)) {
					return false;
				}

				installments = reader.GetInt32();
				hasInstallments = true;
				continue;
			}

			if (reader.ValueTextEquals("requested_at"u8)) {
				if (!TryReadTimestampValue(ref reader, out requestedAt)) {
					return false;
				}

				hasRequestedAt = true;
				continue;
			}

			if (!reader.Read()) {
				return false;
			}

			reader.Skip();
		}

		return false;
	}

	private static bool TryParseMerchantId(ReadOnlySpan<byte> utf8Value, out int merchantId) {
		merchantId = default;

		if (utf8Value.Length <= 5) {
			return false;
		}

		if (!utf8Value[0..5].SequenceEqual("MERC-"u8)) {
			return false;
		}

		return TryParsePositiveAsciiInt(utf8Value[5..], out merchantId);
	}

	private static bool TryParsePositiveAsciiInt(ReadOnlySpan<byte> utf8Value, out int number) {
		number = default;

		if (utf8Value.IsEmpty) {
			return false;
		}

		for (int index = 0; index < utf8Value.Length; index++) {
			int digit = utf8Value[index] - '0';

			if ((uint)digit > 9u) {
				return false;
			}

			number = checked((number * 10) + digit);
		}

		return true;
	}
}

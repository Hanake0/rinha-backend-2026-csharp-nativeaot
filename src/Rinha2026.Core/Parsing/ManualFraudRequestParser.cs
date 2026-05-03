using System.Buffers.Text;

using Rinha2026.Core.Model;

namespace Rinha2026.Core.Parsing;

public static class ManualFraudRequestParser {
	public static bool TryParse(ReadOnlySpan<byte> utf8Json, out FraudRequest request) {
		request = default;

		var cursor = new Utf8JsonCursor(utf8Json);

		if (!cursor.TryReadStartObject()) {
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

		while (true) {
			if (cursor.TryReadEndObject()) {
				break;
			}

			if (!cursor.TryReadPropertyName(out ReadOnlySpan<byte> propertyName) || !cursor.TryReadColon()) {
				return false;
			}

			if (propertyName.SequenceEqual("id"u8)) {
				if (!cursor.TrySkipString()) {
					return false;
				}
			} else if (propertyName.SequenceEqual("transaction"u8)) {
				if (!TryReadTransaction(ref cursor, out transaction)) {
					return false;
				}

				hasTransaction = true;
			} else if (propertyName.SequenceEqual("customer"u8)) {
				if (!TryReadCustomer(ref cursor, out customer)) {
					return false;
				}

				hasCustomer = true;
			} else if (propertyName.SequenceEqual("merchant"u8)) {
				if (!TryReadMerchant(ref cursor, out merchant)) {
					return false;
				}

				hasMerchant = true;
			} else if (propertyName.SequenceEqual("terminal"u8)) {
				if (!TryReadTerminal(ref cursor, out terminal)) {
					return false;
				}

				hasTerminal = true;
			} else if (propertyName.SequenceEqual("last_transaction"u8)) {
				if (!TryReadLastTransaction(ref cursor, out lastTransaction)) {
					return false;
				}

				hasLastTransaction = true;
			} else if (!cursor.TrySkipValue()) {
				return false;
			}

			if (cursor.TryReadComma()) {
				continue;
			}

			if (!cursor.TryReadEndObject()) {
				return false;
			}

			break;
		}

		if (!hasTransaction || !hasCustomer || !hasMerchant || !hasTerminal || !hasLastTransaction) {
			return false;
		}

		request = new FraudRequest(transaction, customer, merchant, terminal, lastTransaction);
		return true;
	}

	private static bool TryReadTransaction(ref Utf8JsonCursor cursor, out TransactionData transaction) {
		transaction = default;

		if (!cursor.TryReadStartObject()) {
			return false;
		}

		double amount = default;
		int installments = default;
		CompactTimestamp requestedAt = default;

		bool hasAmount = false;
		bool hasInstallments = false;
		bool hasRequestedAt = false;

		while (true) {
			if (cursor.TryReadEndObject()) {
				break;
			}

			if (!cursor.TryReadPropertyName(out ReadOnlySpan<byte> propertyName) || !cursor.TryReadColon()) {
				return false;
			}

			if (propertyName.SequenceEqual("amount"u8)) {
				if (!cursor.TryReadDouble(out amount)) {
					return false;
				}

				hasAmount = true;
			} else if (propertyName.SequenceEqual("installments"u8)) {
				if (!cursor.TryReadInt32(out installments)) {
					return false;
				}

				hasInstallments = true;
			} else if (propertyName.SequenceEqual("requested_at"u8)) {
				if (!TryReadTimestampValue(ref cursor, out requestedAt)) {
					return false;
				}

				hasRequestedAt = true;
			} else if (!cursor.TrySkipValue()) {
				return false;
			}

			if (cursor.TryReadComma()) {
				continue;
			}

			if (!cursor.TryReadEndObject()) {
				return false;
			}

			break;
		}

		if (!hasAmount || !hasInstallments || !hasRequestedAt) {
			return false;
		}

		transaction = new TransactionData(amount, installments, requestedAt);
		return true;
	}

	private static bool TryReadCustomer(ref Utf8JsonCursor cursor, out CustomerData customer) {
		customer = default;

		if (!cursor.TryReadStartObject()) {
			return false;
		}

		double averageAmount = default;
		int transactionCountLast24Hours = default;
		MerchantIdList knownMerchants = default;

		bool hasAverageAmount = false;
		bool hasTransactionCountLast24Hours = false;
		bool hasKnownMerchants = false;

		while (true) {
			if (cursor.TryReadEndObject()) {
				break;
			}

			if (!cursor.TryReadPropertyName(out ReadOnlySpan<byte> propertyName) || !cursor.TryReadColon()) {
				return false;
			}

			if (propertyName.SequenceEqual("avg_amount"u8)) {
				if (!cursor.TryReadDouble(out averageAmount)) {
					return false;
				}

				hasAverageAmount = true;
			} else if (propertyName.SequenceEqual("tx_count_24h"u8)) {
				if (!cursor.TryReadInt32(out transactionCountLast24Hours)) {
					return false;
				}

				hasTransactionCountLast24Hours = true;
			} else if (propertyName.SequenceEqual("known_merchants"u8)) {
				if (!TryReadKnownMerchants(ref cursor, out knownMerchants)) {
					return false;
				}

				hasKnownMerchants = true;
			} else if (!cursor.TrySkipValue()) {
				return false;
			}

			if (cursor.TryReadComma()) {
				continue;
			}

			if (!cursor.TryReadEndObject()) {
				return false;
			}

			break;
		}

		if (!hasAverageAmount || !hasTransactionCountLast24Hours || !hasKnownMerchants) {
			return false;
		}

		customer = new CustomerData(averageAmount, knownMerchants, transactionCountLast24Hours);
		return true;
	}

	private static bool TryReadKnownMerchants(ref Utf8JsonCursor cursor, out MerchantIdList knownMerchants) {
		knownMerchants = default;

		if (!cursor.TryReadStartArray()) {
			return false;
		}

		while (true) {
			if (cursor.TryReadEndArray()) {
				return true;
			}

			if (!cursor.TryReadString(out ReadOnlySpan<byte> merchantIdValue) ||
				!TryParseMerchantId(merchantIdValue, out int merchantId) ||
				!knownMerchants.Add(merchantId)) {
				return false;
			}

			if (cursor.TryReadComma()) {
				continue;
			}

			return cursor.TryReadEndArray();
		}
	}

	private static bool TryReadMerchant(ref Utf8JsonCursor cursor, out MerchantData merchant) {
		merchant = default;

		if (!cursor.TryReadStartObject()) {
			return false;
		}

		double averageAmount = default;
		int id = default;
		int mcc = default;

		bool hasAverageAmount = false;
		bool hasId = false;
		bool hasMcc = false;

		while (true) {
			if (cursor.TryReadEndObject()) {
				break;
			}

			if (!cursor.TryReadPropertyName(out ReadOnlySpan<byte> propertyName) || !cursor.TryReadColon()) {
				return false;
			}

			if (propertyName.SequenceEqual("id"u8)) {
				if (!cursor.TryReadString(out ReadOnlySpan<byte> merchantIdValue) ||
					!TryParseMerchantId(merchantIdValue, out id)) {
					return false;
				}

				hasId = true;
			} else if (propertyName.SequenceEqual("mcc"u8)) {
				if (!cursor.TryReadString(out ReadOnlySpan<byte> mccValue) ||
					!TryParsePositiveAsciiInt(mccValue, out mcc)) {
					return false;
				}

				hasMcc = true;
			} else if (propertyName.SequenceEqual("avg_amount"u8)) {
				if (!cursor.TryReadDouble(out averageAmount)) {
					return false;
				}

				hasAverageAmount = true;
			} else if (!cursor.TrySkipValue()) {
				return false;
			}

			if (cursor.TryReadComma()) {
				continue;
			}

			if (!cursor.TryReadEndObject()) {
				return false;
			}

			break;
		}

		if (!hasAverageAmount || !hasId || !hasMcc) {
			return false;
		}

		merchant = new MerchantData(averageAmount, id, mcc);
		return true;
	}

	private static bool TryReadTerminal(ref Utf8JsonCursor cursor, out TerminalData terminal) {
		terminal = default;

		if (!cursor.TryReadStartObject()) {
			return false;
		}

		bool cardPresent = default;
		bool isOnline = default;
		double kilometersFromHome = default;

		bool hasCardPresent = false;
		bool hasIsOnline = false;
		bool hasKilometersFromHome = false;

		while (true) {
			if (cursor.TryReadEndObject()) {
				break;
			}

			if (!cursor.TryReadPropertyName(out ReadOnlySpan<byte> propertyName) || !cursor.TryReadColon()) {
				return false;
			}

			if (propertyName.SequenceEqual("card_present"u8)) {
				if (!cursor.TryReadBoolean(out cardPresent)) {
					return false;
				}

				hasCardPresent = true;
			} else if (propertyName.SequenceEqual("is_online"u8)) {
				if (!cursor.TryReadBoolean(out isOnline)) {
					return false;
				}

				hasIsOnline = true;
			} else if (propertyName.SequenceEqual("km_from_home"u8)) {
				if (!cursor.TryReadDouble(out kilometersFromHome)) {
					return false;
				}

				hasKilometersFromHome = true;
			} else if (!cursor.TrySkipValue()) {
				return false;
			}

			if (cursor.TryReadComma()) {
				continue;
			}

			if (!cursor.TryReadEndObject()) {
				return false;
			}

			break;
		}

		if (!hasCardPresent || !hasIsOnline || !hasKilometersFromHome) {
			return false;
		}

		terminal = new TerminalData(cardPresent, isOnline, kilometersFromHome);
		return true;
	}

	private static bool TryReadLastTransaction(ref Utf8JsonCursor cursor, out LastTransactionData lastTransaction) {
		lastTransaction = default;

		if (cursor.TryReadNull()) {
			lastTransaction = new LastTransactionData(false, 0d, default);
			return true;
		}

		if (!cursor.TryReadStartObject()) {
			return false;
		}

		double kilometersFromCurrent = default;
		CompactTimestamp timestamp = default;

		bool hasKilometersFromCurrent = false;
		bool hasTimestamp = false;

		while (true) {
			if (cursor.TryReadEndObject()) {
				break;
			}

			if (!cursor.TryReadPropertyName(out ReadOnlySpan<byte> propertyName) || !cursor.TryReadColon()) {
				return false;
			}

			if (propertyName.SequenceEqual("timestamp"u8)) {
				if (!TryReadTimestampValue(ref cursor, out timestamp)) {
					return false;
				}

				hasTimestamp = true;
			} else if (propertyName.SequenceEqual("km_from_current"u8)) {
				if (!cursor.TryReadDouble(out kilometersFromCurrent)) {
					return false;
				}

				hasKilometersFromCurrent = true;
			} else if (!cursor.TrySkipValue()) {
				return false;
			}

			if (cursor.TryReadComma()) {
				continue;
			}

			if (!cursor.TryReadEndObject()) {
				return false;
			}

			break;
		}

		if (!hasKilometersFromCurrent || !hasTimestamp) {
			return false;
		}

		lastTransaction = new LastTransactionData(true, kilometersFromCurrent, timestamp);
		return true;
	}

	private static bool TryReadTimestampValue(ref Utf8JsonCursor cursor, out CompactTimestamp timestamp) {
		timestamp = default;
		return cursor.TryReadString(out ReadOnlySpan<byte> value) && CompactTimestamp.TryParseIso8601Zulu(value, out timestamp);
	}

	private static bool TryParseMerchantId(ReadOnlySpan<byte> utf8Value, out int merchantId) {
		merchantId = default;

		if (utf8Value.Length <= 5 || !utf8Value[0..5].SequenceEqual("MERC-"u8)) {
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

	private ref struct Utf8JsonCursor {
		private readonly ReadOnlySpan<byte> json;
		private int index;

		public Utf8JsonCursor(ReadOnlySpan<byte> json) {
			this.json = json;
			this.index = 0;
		}

		public bool TryReadBoolean(out bool value) {
			value = default;
			this.SkipWhitespace();

			if (this.TryMatchLiteral("true"u8)) {
				value = true;
				return true;
			}

			if (this.TryMatchLiteral("false"u8)) {
				value = false;
				return true;
			}

			return false;
		}

		public bool TryReadColon() => this.TryReadToken((byte)':');

		public bool TryReadComma() => this.TryReadToken((byte)',');

		public bool TryReadDouble(out double value) {
			value = default;
			this.SkipWhitespace();

			if (!Utf8Parser.TryParse(this.json[this.index..], out value, out int bytesConsumed) || (bytesConsumed == 0)) {
				return false;
			}

			if (!this.IsValueTerminator(this.index + bytesConsumed)) {
				return false;
			}

			this.index += bytesConsumed;
			return true;
		}

		public bool TryReadEndArray() => this.TryReadToken((byte)']');

		public bool TryReadEndObject() => this.TryReadToken((byte)'}');

		public bool TryReadInt32(out int value) {
			value = default;
			this.SkipWhitespace();

			if (!Utf8Parser.TryParse(this.json[this.index..], out value, out int bytesConsumed) || (bytesConsumed == 0)) {
				return false;
			}

			if (!this.IsValueTerminator(this.index + bytesConsumed)) {
				return false;
			}

			this.index += bytesConsumed;
			return true;
		}

		public bool TryReadNull() {
			this.SkipWhitespace();
			return this.TryMatchLiteral("null"u8);
		}

		public bool TryReadPropertyName(out ReadOnlySpan<byte> propertyName) => this.TryReadString(out propertyName);

		public bool TryReadStartArray() => this.TryReadToken((byte)'[');

		public bool TryReadStartObject() => this.TryReadToken((byte)'{');

		public bool TryReadString(out ReadOnlySpan<byte> value) {
			value = default;
			this.SkipWhitespace();

			if ((this.index >= this.json.Length) || (this.json[this.index] != '"')) {
				return false;
			}

			int start = ++this.index;

			while (this.index < this.json.Length) {
				byte current = this.json[this.index];

				if (current == '"') {
					value = this.json[start..this.index];
					this.index++;
					return true;
				}

				if ((current == '\\') || (current < 0x20)) {
					return false;
				}

				this.index++;
			}

			return false;
		}

		public bool TrySkipString() => this.TryReadString(out _);

		public bool TrySkipValue() {
			this.SkipWhitespace();

			if (this.TryReadStartObject()) {
				while (true) {
					if (this.TryReadEndObject()) {
						return true;
					}

					if (!this.TrySkipString() || !this.TryReadColon() || !this.TrySkipValue()) {
						return false;
					}

					if (this.TryReadComma()) {
						continue;
					}

					return this.TryReadEndObject();
				}
			}

			if (this.TryReadStartArray()) {
				while (true) {
					if (this.TryReadEndArray()) {
						return true;
					}

					if (!this.TrySkipValue()) {
						return false;
					}

					if (this.TryReadComma()) {
						continue;
					}

					return this.TryReadEndArray();
				}
			}

			if (this.TrySkipString() || this.TryReadNull() || this.TryReadBoolean(out _) || this.TryReadDouble(out _)) {
				return true;
			}

			return false;
		}

		private bool IsValueTerminator(int nextIndex) {
			if (nextIndex >= this.json.Length) {
				return true;
			}

			byte current = this.json[nextIndex];
			return (current == ',') || (current == '}') || (current == ']') || this.IsWhitespace(current);
		}

		private bool IsWhitespace(byte value) =>
			(value == (byte)' ') ||
			(value == (byte)'\n') ||
			(value == (byte)'\r') ||
			(value == (byte)'\t');

		private void SkipWhitespace() {
			while ((this.index < this.json.Length) && this.IsWhitespace(this.json[this.index])) {
				this.index++;
			}
		}

		private bool TryMatchLiteral(ReadOnlySpan<byte> literal) {
			if ((this.index + literal.Length) > this.json.Length || !this.json.Slice(this.index, literal.Length).SequenceEqual(literal)) {
				return false;
			}

			if (!this.IsValueTerminator(this.index + literal.Length)) {
				return false;
			}

			this.index += literal.Length;
			return true;
		}

		private bool TryReadToken(byte token) {
			this.SkipWhitespace();

			if ((this.index >= this.json.Length) || (this.json[this.index] != token)) {
				return false;
			}

			this.index++;
			return true;
		}
	}
}

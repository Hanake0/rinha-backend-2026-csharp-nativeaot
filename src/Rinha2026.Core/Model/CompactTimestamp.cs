namespace Rinha2026.Core.Model;

public readonly record struct CompactTimestamp(
	int Year,
	int Month,
	int Day,
	int Hour,
	int Minute,
	int Second) {
	public int GetMondayBasedDayOfWeek() {
		DayOfWeek dayOfWeek = new DateTime(this.Year, this.Month, this.Day, 0, 0, 0, DateTimeKind.Utc).DayOfWeek;
		return dayOfWeek == DayOfWeek.Sunday ? 6 : ((int)dayOfWeek - 1);
	}

	public double GetTotalMinutesSince(CompactTimestamp earlier) {
		DateTime current = this.ToDateTimeUtc();
		DateTime previous = earlier.ToDateTimeUtc();
		return (current - previous).TotalMinutes;
	}

	public DateTime ToDateTimeUtc() => new(this.Year, this.Month, this.Day, this.Hour, this.Minute, this.Second, DateTimeKind.Utc);

	public static bool TryParseIso8601Zulu(ReadOnlySpan<byte> utf8Value, out CompactTimestamp timestamp) {
		timestamp = default;

		if (utf8Value.Length != 20) {
			return false;
		}

		if ((utf8Value[4] != '-') ||
			(utf8Value[7] != '-') ||
			(utf8Value[10] != 'T') ||
			(utf8Value[13] != ':') ||
			(utf8Value[16] != ':') ||
			(utf8Value[19] != 'Z')) {
			return false;
		}

		if (!TryReadFourDigits(utf8Value[0..4], out int year) ||
			!TryReadTwoDigits(utf8Value[5..7], out int month) ||
			!TryReadTwoDigits(utf8Value[8..10], out int day) ||
			!TryReadTwoDigits(utf8Value[11..13], out int hour) ||
			!TryReadTwoDigits(utf8Value[14..16], out int minute) ||
			!TryReadTwoDigits(utf8Value[17..19], out int second)) {
			return false;
		}

		timestamp = new CompactTimestamp(year, month, day, hour, minute, second);
		return true;
	}

	private static bool TryReadFourDigits(ReadOnlySpan<byte> digits, out int value) {
		value = default;

		if (!TryReadDigit(digits[0], out int d0) ||
			!TryReadDigit(digits[1], out int d1) ||
			!TryReadDigit(digits[2], out int d2) ||
			!TryReadDigit(digits[3], out int d3)) {
			return false;
		}

		value = (d0 * 1000) + (d1 * 100) + (d2 * 10) + d3;
		return true;
	}

	private static bool TryReadTwoDigits(ReadOnlySpan<byte> digits, out int value) {
		value = default;

		if (!TryReadDigit(digits[0], out int d0) || !TryReadDigit(digits[1], out int d1)) {
			return false;
		}

		value = (d0 * 10) + d1;
		return true;
	}

	private static bool TryReadDigit(byte value, out int digit) {
		digit = value - '0';
		return (uint)digit <= 9u;
	}
}

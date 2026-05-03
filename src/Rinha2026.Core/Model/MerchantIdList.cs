using System.Runtime.CompilerServices;

namespace Rinha2026.Core.Model;

public struct MerchantIdList {
	public const int Capacity = 8;

	private MerchantIdBuffer codes;

	public int Count { get; private set; }

	public readonly int this[int index] {
		get {
			ArgumentOutOfRangeException.ThrowIfNegative(index);

			if (index >= this.Count) {
				throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be within the number of parsed merchant ids.");
			}

			return this.codes[index];
		}
	}

	public bool Add(int merchantId) {
		if (this.Count >= Capacity) {
			return false;
		}

		this.codes[this.Count] = merchantId;
		this.Count++;
		return true;
	}

	public readonly bool Contains(int merchantId) {
		for (int index = 0; index < this.Count; index++) {
			if (this.codes[index] == merchantId) {
				return true;
			}
		}

		return false;
	}
}

[InlineArray(MerchantIdList.Capacity)]
public struct MerchantIdBuffer {
	private int element0;
}

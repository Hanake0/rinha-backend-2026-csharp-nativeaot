namespace Rinha2026.Core.Detection;

public sealed class MccRiskTable {
	private readonly int[] mccCodes;
	private readonly float[] risks;

	public MccRiskTable(int[] mccCodes, float[] risks, float defaultRisk = 0.5f) {
		ArgumentNullException.ThrowIfNull(mccCodes);
		ArgumentNullException.ThrowIfNull(risks);

		if (mccCodes.Length != risks.Length) {
			throw new ArgumentException("MCC code and risk arrays must have the same length.");
		}

		this.mccCodes = mccCodes;
		this.risks = risks;
		this.DefaultRisk = defaultRisk;
	}

	public float DefaultRisk { get; }

	public float GetRisk(int mcc) {
		for (int index = 0; index < this.mccCodes.Length; index++) {
			if (this.mccCodes[index] == mcc) {
				return this.risks[index];
			}
		}

		return this.DefaultRisk;
	}

	public static MccRiskTable CreateDefault() => new(
		[5411, 5812, 5912, 5944, 7801, 7802, 7995, 4511, 5311, 5999],
		[0.15f, 0.30f, 0.20f, 0.45f, 0.80f, 0.75f, 0.85f, 0.35f, 0.25f, 0.50f]);
}

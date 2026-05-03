using System.Text.Json;

namespace Rinha2026.Core.Detection;

public static class ReferenceDataLoader {
	public static MccRiskTable LoadMccRiskTable(string path) {
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		Dictionary<string, float>? data = JsonSerializer.Deserialize(
			File.ReadAllText(path),
			ReferenceDataJsonContext.Default.DictionaryStringSingle);

		if ((data is null) || (data.Count == 0)) {
			throw new InvalidDataException("The MCC risk file is empty or invalid.");
		}

		int[] mccCodes = new int[data.Count];
		float[] risks = new float[data.Count];
		int index = 0;

		foreach ((string mccText, float risk) in data) {
			if (!int.TryParse(mccText, out int mccCode)) {
				throw new InvalidDataException($"Invalid MCC code '{mccText}' in MCC risk file.");
			}

			mccCodes[index] = mccCode;
			risks[index] = risk;
			index++;
		}

		return new MccRiskTable(mccCodes, risks);
	}

	public static NormalizationConstants LoadNormalizationConstants(string path) {
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		NormalizationFile? file = JsonSerializer.Deserialize(
			File.ReadAllText(path),
			ReferenceDataJsonContext.Default.NormalizationFile);

		if (file is null) {
			throw new InvalidDataException("The normalization file is empty or invalid.");
		}

		return new NormalizationConstants(
			file.AmountVsAverageRatio,
			file.MaxAmount,
			file.MaxInstallments,
			file.MaxKilometers,
			file.MaxMerchantAverageAmount,
			file.MaxMinutes,
			file.MaxTransactionCountLast24Hours);
	}
}

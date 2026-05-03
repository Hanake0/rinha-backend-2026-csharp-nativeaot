using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Rinha2026.Api.Services;
using Rinha2026.Core.Configuration;

namespace Rinha2026.Tests;

public sealed class FraudDetectionServiceParityTests {
	[Theory]
	[InlineData(ParserMode.Manual, 46)]
	[InlineData(ParserMode.Manual, 527)]
	[InlineData(ParserMode.Manual, 732)]
	[InlineData(ParserMode.ReferenceStj, 46)]
	[InlineData(ParserMode.ReferenceStj, 527)]
	[InlineData(ParserMode.ReferenceStj, 732)]
	public void FraudDetectionServiceMatchesOfficialExpectation(ParserMode parserMode, int requestIndex) {
		RuntimeConfig runtimeConfig = CreateRuntimeConfig(parserMode);
		OfficialRequestEntry entry = LoadOfficialEntry(requestIndex);

		using FraudDetectionService service = FraudDetectionService.Create(runtimeConfig);

		Assert.True(service.TryHandle(entry.Payload, out ReadOnlyMemory<byte> response));

		using JsonDocument document = JsonDocument.Parse(response);
		bool approved = document.RootElement.GetProperty("approved").GetBoolean();
		double fraudScore = document.RootElement.GetProperty("fraud_score").GetDouble();

		Assert.True(
			approved == entry.ExpectedApproved,
			$"Request {requestIndex} expected approved={entry.ExpectedApproved} but service returned approved={approved}, fraud_score={fraudScore.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
	}

	[Theory]
	[InlineData(ParserMode.Manual, 46)]
	[InlineData(ParserMode.Manual, 527)]
	[InlineData(ParserMode.Manual, 732)]
	[InlineData(ParserMode.ReferenceStj, 46)]
	[InlineData(ParserMode.ReferenceStj, 527)]
	[InlineData(ParserMode.ReferenceStj, 732)]
	public async Task FraudScoreEndpointMatchesOfficialExpectation(ParserMode parserMode, int requestIndex) {
		RuntimeConfig runtimeConfig = CreateRuntimeConfig(parserMode);
		OfficialRequestEntry entry = LoadOfficialEntry(requestIndex);

		using WebApplicationFactory<Program> factory = CreateFactory(runtimeConfig);
		using HttpClient client = factory.CreateClient();
		using ByteArrayContent content = new(entry.Payload);
		content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

		using HttpResponseMessage response = await client.PostAsync("/fraud-score", content);
		byte[] payload = await response.Content.ReadAsByteArrayAsync();

		Assert.True(response.IsSuccessStatusCode);

		using JsonDocument document = JsonDocument.Parse(payload);
		bool approved = document.RootElement.GetProperty("approved").GetBoolean();
		double fraudScore = document.RootElement.GetProperty("fraud_score").GetDouble();

		Assert.True(
			approved == entry.ExpectedApproved,
			$"Request {requestIndex} expected approved={entry.ExpectedApproved} but endpoint returned approved={approved}, fraud_score={fraudScore.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
	}

	private static RuntimeConfig CreateRuntimeConfig(ParserMode parserMode) {
		string repoRoot = GetRepoRoot();
		string runtimeDataRoot = Path.Combine(repoRoot, "runtime-data");

		return new RuntimeConfig(
			new RuntimeDetectionConfig(TopK: 5, ApprovalThreshold: 0.6d),
			new RuntimeDatasetConfig(
				Path.Combine(runtimeDataRoot, "index"),
				Path.Combine(runtimeDataRoot, "mcc_risk.json"),
				Path.Combine(runtimeDataRoot, "normalization.json")),
			new RuntimeSearchConfig(
				BeamLevel1: 10,
				BeamLevel2: 17,
				Dimension: 14,
				DistanceMetric: DistanceMetric.SquaredL2,
				IndexKind: IndexKind.HierarchicalBeamIvf,
				PaddedDimension: 16,
				RerankCount: 68,
				UseLastTransactionPartitionPruning: true),
			new RuntimeHttpConfig(
				ParserMode: parserMode,
				ResponseMode: ResponseMode.PrecomputedTable,
				TransportMode: TransportMode.Tcp));
	}

	private static OfficialRequestEntry LoadOfficialEntry(int targetIndex) {
		string repoRoot = GetRepoRoot();
		string testDataPath = Path.Combine(repoRoot, "..", "rinha-de-backend-2026", "test", "test-data.json");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(testDataPath));
		JsonElement.ArrayEnumerator entries = document.RootElement.GetProperty("entries").EnumerateArray();
		int index = 0;

		foreach (JsonElement entry in entries) {
			if (index == targetIndex) {
				return new OfficialRequestEntry(
					targetIndex,
					entry.GetProperty("expected_approved").GetBoolean(),
					Encoding.UTF8.GetBytes(entry.GetProperty("request").GetRawText()));
			}

			index++;
		}

		throw new ArgumentOutOfRangeException(nameof(targetIndex), targetIndex, "Official test-data entry was not found.");
	}

	private static string GetRepoRoot() {
		DirectoryInfo? current = new(AppContext.BaseDirectory);

		while (current is not null) {
			string solutionPath = Path.Combine(current.FullName, "Rinha2026.NativeAot.sln");

			if (File.Exists(solutionPath)) {
				return current.FullName;
			}

			current = current.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
	}

	private static WebApplicationFactory<Program> CreateFactory(RuntimeConfig runtimeConfig) {
		return new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
			builder.UseEnvironment("Testing");
			builder.ConfigureServices(services => {
				services.RemoveAll(typeof(RuntimeConfig));
				services.AddSingleton(typeof(RuntimeConfig), runtimeConfig);
			});
		});
	}

	private sealed record OfficialRequestEntry(int Index, bool ExpectedApproved, byte[] Payload);
}

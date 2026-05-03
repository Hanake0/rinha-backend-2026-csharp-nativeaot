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

	[Fact]
	public void AdaptiveBoundaryRerankFixesKnownOfficialBoundaryMismatch() {
		RuntimeConfig runtimeConfig = CreateRuntimeConfig(
			ParserMode.Manual,
			runtimeDataDirectoryName: "runtime-data-256x128-radii-f32-stable-s524k",
			beamLevel1: 8,
			beamLevel2: 128,
			rerankCount: 32,
			boundaryRerankCount: 48,
			useLeafRadiusPruning: true,
			useLastTransactionPartitionPruning: false);
		OfficialRequestEntry entry = LoadOfficialEntry(25640);

		using FraudDetectionService service = FraudDetectionService.Create(runtimeConfig);

		Assert.True(service.TryHandle(entry.Payload, out ReadOnlyMemory<byte> response));

		using JsonDocument document = JsonDocument.Parse(response);
		bool approved = document.RootElement.GetProperty("approved").GetBoolean();

		Assert.Equal(entry.ExpectedApproved, approved);
	}

	private static RuntimeConfig CreateRuntimeConfig(
		ParserMode parserMode,
		string runtimeDataDirectoryName = "runtime-data",
		int beamLevel1 = 10,
		int beamLevel2 = 17,
		int rerankCount = 68,
		int boundaryRerankCount = 68,
		bool useLeafRadiusPruning = false,
		bool useLastTransactionPartitionPruning = true) {
		string repoRoot = GetRepoRoot();
		string runtimeDataRoot = Path.Combine(repoRoot, runtimeDataDirectoryName);

		return new RuntimeConfig(
			new RuntimeDetectionConfig(TopK: 5, ApprovalThreshold: 0.6d),
			new RuntimeDatasetConfig(
				Path.Combine(runtimeDataRoot, "index"),
				Path.Combine(runtimeDataRoot, "mcc_risk.json"),
				Path.Combine(runtimeDataRoot, "normalization.json")),
			new RuntimeSearchConfig(
				BeamLevel1: beamLevel1,
				BeamLevel2: beamLevel2,
				Dimension: 14,
				DistanceMetric: DistanceMetric.SquaredL2,
				IndexKind: IndexKind.HierarchicalBeamIvf,
				PaddedDimension: 16,
				RerankCount: rerankCount,
				BoundaryRerankCount: boundaryRerankCount,
				UseLeafRadiusPruning: useLeafRadiusPruning,
				UseLastTransactionPartitionPruning: useLastTransactionPartitionPruning),
			new RuntimeHttpConfig(
				ParserMode: parserMode,
				ResponseMode: ResponseMode.PrecomputedTable,
				ServerMode: ServerMode.Kestrel,
				TransportMode: TransportMode.Tcp,
				UnixSocketPath: null));
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

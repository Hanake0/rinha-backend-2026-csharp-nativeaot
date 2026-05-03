using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

using Rinha2026.Api.Services;

namespace Rinha2026.Api.Endpoints;

public static class FraudScoreEndpoint {
	internal const string StartupWarmupHeaderName = "X-Startup-Warmup";
	internal const string StartupWarmupHeaderValue = "1";

	public static async Task HandleAsync(
		HttpContext context,
		StartupState startupState,
		FraudRuntimeState runtimeState,
		RequestProfileCollector requestProfileCollector,
		CancellationToken cancellationToken) {
		bool allowStartupWarmup = !startupState.IsReady &&
			context.Request.Headers.TryGetValue(StartupWarmupHeaderName, out Microsoft.Extensions.Primitives.StringValues warmupHeader) &&
			Microsoft.Extensions.Primitives.StringValues.Equals(warmupHeader, StartupWarmupHeaderValue);

		if ((!startupState.IsReady && !allowStartupWarmup) || !runtimeState.TryGet(out FraudDetectionService? detectionService) || (detectionService is null)) {
			context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
			return;
		}

		long requestStart = requestProfileCollector.IsEnabled ? Stopwatch.GetTimestamp() : 0L;
		(byte[]? rentedBuffer, int length) = await TryReadPayloadAsync(context.Request, cancellationToken);

		if (rentedBuffer is null) {
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		try {
			if (!requestProfileCollector.IsEnabled) {
				if (!detectionService.TryHandle(rentedBuffer.AsSpan(0, length), out ReadOnlyMemory<byte> responsePayload)) {
					context.Response.StatusCode = StatusCodes.Status400BadRequest;
					return;
				}

				context.Response.StatusCode = StatusCodes.Status200OK;
				context.Response.ContentType = "application/json";
				context.Response.ContentLength = responsePayload.Length;
				await context.Response.BodyWriter.WriteAsync(responsePayload, cancellationToken);
				return;
			}

			long afterRead = Stopwatch.GetTimestamp();

			if (!detectionService.TryHandle(
				rentedBuffer.AsSpan(0, length),
				out ReadOnlyMemory<byte> response,
				out FraudDetectionProfile detectionProfile)) {
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}

			context.Response.StatusCode = StatusCodes.Status200OK;
			context.Response.ContentType = "application/json";
			context.Response.ContentLength = response.Length;
			long beforeWrite = Stopwatch.GetTimestamp();
			await context.Response.BodyWriter.WriteAsync(response, cancellationToken);
			long afterWrite = Stopwatch.GetTimestamp();
			requestProfileCollector.Record(
				afterRead - requestStart,
				detectionProfile.ParseTicks,
				detectionProfile.VectorizeTicks,
				detectionProfile.SearchTicks,
				afterWrite - beforeWrite,
				afterWrite - requestStart);
		} finally {
			ArrayPool<byte>.Shared.Return(rentedBuffer);
		}
	}

	private static async Task<(byte[]? Buffer, int Length)> TryReadPayloadAsync(
		HttpRequest request,
		CancellationToken cancellationToken) {
		if (request.ContentLength is null || (request.ContentLength <= 0) || (request.ContentLength > 64 * 1024)) {
			return (null, 0);
		}

		int length = checked((int)request.ContentLength.Value);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
		int copied = 0;
		PipeReader bodyReader = request.BodyReader;

		while (copied < length) {
			ReadResult readResult = await bodyReader.ReadAsync(cancellationToken);
			ReadOnlySequence<byte> sequence = readResult.Buffer;
			int remaining = length - copied;
			int bytesToCopy = (int)Math.Min(sequence.Length, remaining);

			if (bytesToCopy > 0) {
				CopySequence(sequence.Slice(0, bytesToCopy), buffer.AsSpan(copied, bytesToCopy));
				copied += bytesToCopy;
			}

			SequencePosition consumed = sequence.GetPosition(bytesToCopy);
			bodyReader.AdvanceTo(consumed, consumed);

			if (readResult.IsCompleted && (copied < length)) {
				break;
			}
		}

		if (copied != length) {
			ArrayPool<byte>.Shared.Return(buffer);
			return (null, 0);
		}

		return (buffer, length);
	}

	private static void CopySequence(ReadOnlySequence<byte> source, Span<byte> destination) {
		int offset = 0;

		foreach (ReadOnlyMemory<byte> segment in source) {
			ReadOnlySpan<byte> segmentSpan = segment.Span;
			segmentSpan.CopyTo(destination[offset..]);
			offset += segmentSpan.Length;
		}
	}
}

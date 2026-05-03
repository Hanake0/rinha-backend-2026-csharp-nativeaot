using System.Buffers;

using Rinha2026.Api.Services;

namespace Rinha2026.Api.Endpoints;

public static class FraudScoreEndpoint {
	public static async Task HandleAsync(
		HttpContext context,
		StartupState startupState,
		FraudRuntimeState runtimeState,
		CancellationToken cancellationToken) {
		if (!startupState.IsReady || !runtimeState.TryGet(out FraudDetectionService? detectionService) || (detectionService is null)) {
			context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
			return;
		}

		(byte[]? rentedBuffer, int length) = await TryReadPayloadAsync(context.Request, cancellationToken);

		if (rentedBuffer is null) {
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		try {
			if (!detectionService.TryHandle(rentedBuffer.AsSpan(0, length), out ReadOnlyMemory<byte> response)) {
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}

			context.Response.StatusCode = StatusCodes.Status200OK;
			context.Response.ContentType = "application/json";
			context.Response.ContentLength = response.Length;
			await context.Response.BodyWriter.WriteAsync(response, cancellationToken);
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

		int read = 0;

		while (read < length) {
			int bytesRead = await request.Body.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);

			if (bytesRead == 0) {
				break;
			}

			read += bytesRead;
		}

		if (read != length) {
			ArrayPool<byte>.Shared.Return(buffer);
			return (null, 0);
		}

		return (buffer, length);
	}
}

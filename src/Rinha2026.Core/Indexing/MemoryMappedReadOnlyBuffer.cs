using System.IO.MemoryMappedFiles;

namespace Rinha2026.Core.Indexing;

public sealed unsafe class MemoryMappedReadOnlyBuffer : IDisposable {
	private readonly MemoryMappedFile mappedFile;
	private readonly MemoryMappedViewAccessor viewAccessor;
	private readonly byte* pointer;
	private bool disposed;

	private MemoryMappedReadOnlyBuffer(
		MemoryMappedFile mappedFile,
		MemoryMappedViewAccessor viewAccessor,
		byte* pointer,
		long length) {
		this.mappedFile = mappedFile;
		this.viewAccessor = viewAccessor;
		this.pointer = pointer;
		this.Length = length;
	}

	public long Length { get; }

	public static MemoryMappedReadOnlyBuffer OpenRead(string path) {
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		FileInfo fileInfo = new(path);

		if (!fileInfo.Exists) {
			throw new FileNotFoundException("Memory-mapped buffer file was not found.", path);
		}

		MemoryMappedFile mappedFile = MemoryMappedFile.CreateFromFile(
			path,
			FileMode.Open,
			mapName: null,
			capacity: 0,
			MemoryMappedFileAccess.Read);
		MemoryMappedViewAccessor viewAccessor = mappedFile.CreateViewAccessor(
			offset: 0,
			size: 0,
			MemoryMappedFileAccess.Read);

		byte* rawPointer = null;
		viewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref rawPointer);

		try {
			byte* pointer = rawPointer + viewAccessor.PointerOffset;
			return new MemoryMappedReadOnlyBuffer(mappedFile, viewAccessor, pointer, fileInfo.Length);
		} catch {
			viewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
			viewAccessor.Dispose();
			mappedFile.Dispose();
			throw;
		}
	}

	public ReadOnlySpan<byte> GetSpan() {
		ObjectDisposedException.ThrowIf(this.disposed, this);

		if (this.Length > int.MaxValue) {
			throw new InvalidOperationException("Mapped buffers larger than 2 GB are not supported by this span view.");
		}

		return new ReadOnlySpan<byte>(this.pointer, checked((int)this.Length));
	}

	public int TouchEveryPage(int pageSize = 4096) {
		ReadOnlySpan<byte> span = this.GetSpan();

		if (span.IsEmpty) {
			return 0;
		}

		int checksum = 0;
		int stride = Math.Max(pageSize, 1);

		for (int index = 0; index < span.Length; index += stride) {
			checksum ^= span[index];
		}

		checksum ^= span[^1];
		return checksum;
	}

	public void Dispose() {
		if (this.disposed) {
			return;
		}

		this.viewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
		this.viewAccessor.Dispose();
		this.mappedFile.Dispose();
		this.disposed = true;
	}
}

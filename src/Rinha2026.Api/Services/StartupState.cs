namespace Rinha2026.Api.Services;

public sealed class StartupState {
	private int isReady;

	public bool IsReady => Volatile.Read(ref this.isReady) == 1;

	public void MarkReady() => Volatile.Write(ref this.isReady, 1);
}

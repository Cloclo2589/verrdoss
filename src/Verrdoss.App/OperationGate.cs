namespace Verrdoss.App;

public sealed class OperationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool TryBegin() => _semaphore.Wait(0);

    public void End() => _semaphore.Release();
}

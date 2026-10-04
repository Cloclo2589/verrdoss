namespace Verrdoss.Core;

public sealed class KdfParameters
{
    public KdfParameters(int memoryKiB, int iterations, int parallelism)
    {
        if (memoryKiB < 8)
            throw new VaultException("Paramètres Argon2 trop faibles.");
        if (iterations < 1 || parallelism < 1)
            throw new VaultException("Paramètres Argon2 invalides.");
        MemoryKiB = memoryKiB;
        Iterations = iterations;
        Parallelism = parallelism;
    }

    public int MemoryKiB { get; }
    public int Iterations { get; }
    public int Parallelism { get; }

    public static KdfParameters Production { get; } = new(64 * 1024, 3, 1);

    public static KdfParameters Fast { get; } = new(32, 1, 1);
}

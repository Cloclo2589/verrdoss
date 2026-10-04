namespace Verrdoss.Core;

public sealed class VaultException : Exception
{
    public VaultException(string message) : base(message)
    {
    }

    public VaultException(string message, Exception inner) : base(message, inner)
    {
    }
}

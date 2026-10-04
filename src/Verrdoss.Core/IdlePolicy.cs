namespace Verrdoss.Core;

public static class IdlePolicy
{
    public const int DefaultMinutes = 15;
    public const int MinMinutes = 1;
    public const int MaxMinutes = 240;

    public static bool ShouldLock(TimeSpan idle, int idleMinutes, int unlockedWithKey)
    {
        if (unlockedWithKey <= 0)
            return false;
        if (idleMinutes < MinMinutes)
            idleMinutes = DefaultMinutes;
        return idle >= TimeSpan.FromMinutes(idleMinutes);
    }
}

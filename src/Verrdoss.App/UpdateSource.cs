namespace Verrdoss.App;

public static class UpdateSource
{
    public const string Owner = "Cloclo2589";
    public const string Repo = "verrdoss";
    public const string AssetName = "VerrdossSetup.exe";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repo);
}

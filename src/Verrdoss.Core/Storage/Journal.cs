namespace Verrdoss.Core;

public sealed class Journal
{
    private readonly string _path;
    private JournalFile _file;

    private Journal(string path, JournalFile file)
    {
        _path = path;
        _file = file;
    }

    public IReadOnlyList<JournalEntry> Entries => _file.Entries;

    public static Journal Open(string path)
    {
        JournalFile file;
        try
        {
            file = AtomicFile.ReadJson<JournalFile>(path);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new VaultException("Le journal VerrDoss est illisible.", ex);
        }
        file.Entries ??= new List<JournalEntry>();
        return new Journal(path, file);
    }

    public void Replace(JournalEntry entry)
    {
        _file.Entries.RemoveAll(e => e.VaultId == entry.VaultId && e.Op == entry.Op);
        _file.Entries.Add(entry);
        Save();
    }

    public void Remove(JournalEntry entry)
    {
        _file.Entries.RemoveAll(e =>
            e.VaultId == entry.VaultId &&
            e.Op == entry.Op &&
            e.OriginalPath == entry.OriginalPath);
        Save();
    }

    public void Clear()
    {
        _file = new JournalFile();
        Save();
    }

    private void Save() => AtomicFile.WriteJson(_path, _file);
}

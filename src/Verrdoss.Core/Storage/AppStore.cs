using System.Security.Cryptography;
using System.Text.Json;

namespace Verrdoss.Core;

public sealed class AppStore
{
    private AppStore(string dataDirectory, StoreData data)
    {
        DataDirectory = dataDirectory;
        Data = data;
    }

    public string DataDirectory { get; }
    public StoreData Data { get; }
    public string StorePath => Path.Combine(DataDirectory, "store.json");
    public string JournalPath => Path.Combine(DataDirectory, "journal.json");
    public bool HasMaster => Data.Master != null;

    public static AppStore Open(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "store.json");
        StoreData data;
        try
        {
            data = AtomicFile.ReadJson<StoreData>(path);
        }
        catch (JsonException ex)
        {
            throw new VaultException("Le fichier de configuration VerrDoss est illisible.", ex);
        }
        if (data.IdleMinutes < IdlePolicy.MinMinutes || data.IdleMinutes > IdlePolicy.MaxMinutes)
            data.IdleMinutes = IdlePolicy.DefaultMinutes;
        data.Vaults ??= new List<VaultRecord>();
        return new AppStore(dataDirectory, data);
    }

    public void Save() => AtomicFile.WriteJson(StorePath, Data);

    public void SetMaster(byte[] password, KdfParameters kdf)
    {
        if (password.Length < 8)
            throw new VaultException("Le mot de passe maître doit contenir au moins 8 caractères.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Argon2Kdf.Derive(password, salt, kdf);
        try
        {
            Data.Master = new MasterRecord
            {
                MemoryKiB = kdf.MemoryKiB,
                Iterations = kdf.Iterations,
                Parallelism = kdf.Parallelism,
                Salt = Convert.ToBase64String(salt),
                Hash = Convert.ToBase64String(hash)
            };
        }
        finally
        {
            Argon2Kdf.Clear(hash);
        }
        Save();
    }

    public bool CheckMaster(byte[] password)
    {
        if (Data.Master == null)
            return false;
        var salt = Convert.FromBase64String(Data.Master.Salt);
        var expected = Convert.FromBase64String(Data.Master.Hash);
        var kdf = new KdfParameters(Data.Master.MemoryKiB, Data.Master.Iterations, Data.Master.Parallelism);
        var hash = Argon2Kdf.Derive(password, salt, kdf);
        try
        {
            return CryptographicOperations.FixedTimeEquals(hash, expected);
        }
        finally
        {
            Argon2Kdf.Clear(hash);
        }
    }

    public void SetIdleMinutes(int minutes)
    {
        if (minutes < IdlePolicy.MinMinutes || minutes > IdlePolicy.MaxMinutes)
            throw new VaultException("Le délai d'inactivité doit être compris entre 1 et 240 minutes.");
        Data.IdleMinutes = minutes;
        Save();
    }

    public VaultRecord? FindById(string id) => Data.Vaults.FirstOrDefault(v => v.Id == id);

    public VaultRecord? FindByOriginalPath(string path)
    {
        var full = Path.GetFullPath(path);
        return Data.Vaults.FirstOrDefault(v => PathsEqual(v.OriginalPath, full));
    }

    public void Upsert(VaultRecord record)
    {
        var index = Data.Vaults.FindIndex(v => v.Id == record.Id);
        if (index >= 0)
            Data.Vaults[index] = record;
        else
            Data.Vaults.Add(record);
        Save();
    }

    public void Remove(string id)
    {
        Data.Vaults.RemoveAll(v => v.Id == id);
        Save();
    }

    public static bool PathsEqual(string left, string right)
    {
        var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Mountly.Core.Storage;

/// <summary>
/// Small JSON files shared by the app and the CLI. Writes go to a temporary file that replaces the old
/// one, and a named mutex serialises read-modify-write cycles across processes.
/// </summary>
public static class JsonFile
{
    public static T Load<T>(string path, JsonTypeInfo<T> type, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize(stream, type) ?? fallback();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or locked file must never stop the app from starting; start from defaults.
            return fallback();
        }
    }

    public static void Save<T>(string path, T value, JsonTypeInfo<T> type)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Environment.ProcessId + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, value, type);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(30); // another process is reading it right now
            }
        }
    }

    /// <summary>Loads, changes and saves a file while holding a machine-wide lock for it.</summary>
    public static T Update<T>(string path, JsonTypeInfo<T> type, Func<T> fallback, Action<T> change)
    {
        using var mutex = new Mutex(false, "Mountly-" + Path.GetFileName(path));
        var owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { owned = true; }
            var value = Load(path, type, fallback);
            change(value);
            Save(path, value, type);
            return value;
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }
}

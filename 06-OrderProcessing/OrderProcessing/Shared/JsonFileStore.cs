using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderProcessing.Shared;

public sealed class JsonFileStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public async Task<List<T>> ReadAsync<T>(string fileName, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(DataFiles.PathFor(fileName), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await ReadUnlockedAsync<T>(fileName, ct); }
        finally { gate.Release(); }
    }

    public async Task WriteAsync<T>(string fileName, List<T> values, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(DataFiles.PathFor(fileName), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { await WriteUnlockedAsync(fileName, values, ct); }
        finally { gate.Release(); }
    }

    public async Task<TResult> UpdateAsync<T, TResult>(string fileName, Func<List<T>, TResult> update, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(DataFiles.PathFor(fileName), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            List<T> values = await ReadUnlockedAsync<T>(fileName, ct);
            TResult result = update(values);
            await WriteUnlockedAsync(fileName, values, ct);
            return result;
        }
        finally { gate.Release(); }
    }


    public List<T> Read<T>(string fileName)
    {
        var gate = Gates.GetOrAdd(DataFiles.PathFor(fileName), _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            string path = DataFiles.PathFor(fileName);
            if (!File.Exists(path)) return [];
            try { return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOptions) ?? []; }
            catch (JsonException ex) { throw new DataFileCorruptedException(fileName, ex); }
        }
        finally { gate.Release(); }
    }

    public void Write<T>(string fileName, List<T> values)
    {
        var gate = Gates.GetOrAdd(DataFiles.PathFor(fileName), _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            Directory.CreateDirectory(DataFiles.Root);
            string path = DataFiles.PathFor(fileName);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, JsonSerializer.Serialize(values, JsonOptions), new System.Text.UTF8Encoding(false)); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.Release(); }
    }

    private static async Task<List<T>> ReadUnlockedAsync<T>(string fileName, CancellationToken ct)
    {
        string path = DataFiles.PathFor(fileName);
        if (!File.Exists(path)) return [];
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, ct) ?? [];
        }
        catch (JsonException ex) { throw new DataFileCorruptedException(fileName, ex); }
    }

    private static async Task WriteUnlockedAsync<T>(string fileName, List<T> values, CancellationToken ct)
    {
        Directory.CreateDirectory(DataFiles.Root);
        string path = DataFiles.PathFor(fileName);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, values, JsonOptions, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

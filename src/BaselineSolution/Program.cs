using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;

string inputFile = args.Length > 0 ? args[0] : "measurements.txt";

if (!File.Exists(inputFile))
{
    Console.Error.WriteLine($"File not found: {inputFile}");
    return 1;
}

var sw = Stopwatch.StartNew();

const int threadCount = 4;

var fileInfo = new FileInfo(inputFile);
long fileSize = fileInfo.Length;

using var mmf = MemoryMappedFile.CreateFromFile(inputFile, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
using var accessor = mmf.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);

// Each thread gets its own local ByteKeyDictionary — no locking, no string allocations
var threadResults = new ByteKeyDictionary[threadCount];

unsafe
{
    byte* pointer = null;
    accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);

    try
    {
        // Split file into chunks, aligning to line boundaries
        var chunkBounds = new long[threadCount + 1];
        chunkBounds[0] = 0;
        chunkBounds[threadCount] = fileSize;

        for (int i = 1; i < threadCount; i++)
        {
            long approx = fileSize * i / threadCount;
            while (approx < fileSize && pointer[approx] != (byte)'\n')
                approx++;
            if (approx < fileSize) approx++;
            chunkBounds[i] = approx;
        }

        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadIndex = t;
            long start = chunkBounds[threadIndex];
            long end = chunkBounds[threadIndex + 1];

            tasks[t] = Task.Run(() =>
            {
                var localStats = new ByteKeyDictionary();
                long pos = start;

                while (pos < end)
                {
                    long lineStart = pos;
                    while (pos < end && pointer[pos] != (byte)'\n')
                        pos++;

                    long lineEnd = pos;
                    if (pos < end) pos++;

                    if (lineEnd > lineStart && pointer[lineEnd - 1] == (byte)'\r')
                        lineEnd--;

                    if (lineEnd <= lineStart) continue;

                    long sepPos = lineStart;
                    while (sepPos < lineEnd && pointer[sepPos] != (byte)';')
                        sepPos++;

                    if (sepPos >= lineEnd) continue;

                    int nameLen = (int)(sepPos - lineStart);
                    double value = ParseDouble(pointer, sepPos + 1, lineEnd);

                    localStats.AddOrUpdate(pointer + lineStart, nameLen, value);
                }

                threadResults[threadIndex] = localStats;
            });
        }

        Task.WaitAll(tasks);
    }
    finally
    {
        accessor.SafeMemoryMappedViewHandle.ReleasePointer();
    }
}

// Merge thread-local results, converting byte keys to strings only once
var merged = new SortedDictionary<string, StationStats>(StringComparer.Ordinal);
foreach (var localStats in threadResults)
{
    foreach (var entry in localStats.GetEntries())
    {
        string name = Encoding.UTF8.GetString(entry.Key);
        if (merged.TryGetValue(name, out var existing))
        {
            existing.Min = Math.Min(existing.Min, entry.Min);
            existing.Max = Math.Max(existing.Max, entry.Max);
            existing.Sum += entry.Sum;
            existing.Count += entry.Count;
        }
        else
        {
            merged[name] = new StationStats
            {
                Min = entry.Min,
                Max = entry.Max,
                Sum = entry.Sum,
                Count = entry.Count
            };
        }
    }
}

// Format output
var results = merged.Select(kv =>
{
    var s = kv.Value;
    var mean = s.Sum / s.Count;
    return $"{kv.Key}={s.Min.ToString("F2", CultureInfo.InvariantCulture)}/{mean.ToString("F2", CultureInfo.InvariantCulture)}/{s.Max.ToString("F2", CultureInfo.InvariantCulture)}";
});

Console.Write("{");
Console.Write(string.Join(", ", results));
Console.WriteLine("}");

Console.Error.WriteLine($"Processed in {sw.Elapsed.TotalSeconds:F2}s");
return 0;

static unsafe double ParseDouble(byte* data, long start, long end)
{
    double result = 0;
    double fraction = 0;
    double divisor = 1;
    bool hasDot = false;

    for (long i = start; i < end; i++)
    {
        byte b = data[i];
        if (b == (byte)'.')
        {
            hasDot = true;
            continue;
        }
        if (b < (byte)'0' || b > (byte)'9') continue;

        int digit = b - '0';
        if (hasDot)
        {
            divisor *= 10;
            fraction += digit / divisor;
        }
        else
        {
            result = result * 10 + digit;
        }
    }

    return result + fraction;
}

class StationStats
{
    public double Min;
    public double Max;
    public double Sum;
    public long Count;
}

/// <summary>
/// Open-addressing hash map keyed on raw byte spans.
/// Avoids all string allocation during the hot loop.
/// Keys are copied once on first insert; lookups compare bytes directly.
/// </summary>
unsafe class ByteKeyDictionary
{
    private const int InitialCapacity = 256; // plenty for ~55 regions
    private const double LoadFactor = 0.7;

    private Entry[] _entries;
    private int _count;

    public ByteKeyDictionary()
    {
        _entries = new Entry[InitialCapacity];
        _count = 0;
    }

    public void AddOrUpdate(byte* keyPtr, int keyLen, double value)
    {
        if (_count >= (int)(_entries.Length * LoadFactor))
            Resize();

        uint hash = FnvHash(keyPtr, keyLen);
        int mask = _entries.Length - 1;
        int idx = (int)(hash & (uint)mask);

        while (true)
        {
            ref var entry = ref _entries[idx];

            if (entry.Key == null)
            {
                // Empty slot — insert
                entry.Key = new byte[keyLen];
                new ReadOnlySpan<byte>(keyPtr, keyLen).CopyTo(entry.Key);
                entry.Hash = hash;
                entry.Min = value;
                entry.Max = value;
                entry.Sum = value;
                entry.Count = 1;
                _count++;
                return;
            }

            if (entry.Hash == hash && entry.Key.Length == keyLen &&
                new ReadOnlySpan<byte>(keyPtr, keyLen).SequenceEqual(entry.Key))
            {
                // Found — update
                if (value < entry.Min) entry.Min = value;
                if (value > entry.Max) entry.Max = value;
                entry.Sum += value;
                entry.Count++;
                return;
            }

            idx = (idx + 1) & mask;
        }
    }

    public IEnumerable<Entry> GetEntries()
    {
        for (int i = 0; i < _entries.Length; i++)
            if (_entries[i].Key != null)
                yield return _entries[i];
    }

    private void Resize()
    {
        var old = _entries;
        _entries = new Entry[old.Length * 2];
        _count = 0;
        int mask = _entries.Length - 1;

        for (int i = 0; i < old.Length; i++)
        {
            if (old[i].Key == null) continue;
            ref var src = ref old[i];

            int idx = (int)(src.Hash & (uint)mask);
            while (_entries[idx].Key != null)
                idx = (idx + 1) & mask;

            _entries[idx] = src;
            _count++;
        }
    }

    private static uint FnvHash(byte* data, int len)
    {
        uint hash = 2166136261;
        for (int i = 0; i < len; i++)
        {
            hash ^= data[i];
            hash *= 16777619;
        }
        return hash;
    }

    public struct Entry
    {
        public byte[]? Key;
        public uint Hash;
        public double Min;
        public double Max;
        public double Sum;
        public long Count;
    }
}

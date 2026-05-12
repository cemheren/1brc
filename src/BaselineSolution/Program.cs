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

// Each thread gets its own local dictionary — no locking needed
var threadResults = new Dictionary<string, StationStats>[threadCount];

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
            // Walk forward to the next newline so we don't split a line
            while (approx < fileSize && pointer[approx] != (byte)'\n')
                approx++;
            if (approx < fileSize) approx++; // skip past the \n
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
                var localStats = new Dictionary<string, StationStats>();
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

                    var nameSpan = new ReadOnlySpan<byte>(pointer + lineStart, nameLen);
                    string name = Encoding.UTF8.GetString(nameSpan);

                    if (localStats.TryGetValue(name, out var existing))
                    {
                        existing.Min = Math.Min(existing.Min, value);
                        existing.Max = Math.Max(existing.Max, value);
                        existing.Sum += value;
                        existing.Count++;
                    }
                    else
                    {
                        localStats[name] = new StationStats
                        {
                            Min = value,
                            Max = value,
                            Sum = value,
                            Count = 1
                        };
                    }
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

// Merge thread-local results
var merged = new Dictionary<string, StationStats>();
foreach (var localStats in threadResults)
{
    foreach (var (name, s) in localStats)
    {
        if (merged.TryGetValue(name, out var existing))
        {
            existing.Min = Math.Min(existing.Min, s.Min);
            existing.Max = Math.Max(existing.Max, s.Max);
            existing.Sum += s.Sum;
            existing.Count += s.Count;
        }
        else
        {
            merged[name] = new StationStats
            {
                Min = s.Min,
                Max = s.Max,
                Sum = s.Sum,
                Count = s.Count
            };
        }
    }
}

// Sort alphabetically and format output
var sorted = new SortedDictionary<string, StationStats>(merged);

var results = sorted.Select(kv =>
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

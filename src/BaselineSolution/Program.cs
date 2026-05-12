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

var fileInfo = new FileInfo(inputFile);
long fileSize = fileInfo.Length;

var stats = new Dictionary<string, StationStats>();

using var mmf = MemoryMappedFile.CreateFromFile(inputFile, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
using var accessor = mmf.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);

unsafe
{
    byte* pointer = null;
    accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);

    try
    {
        long pos = 0;

        while (pos < fileSize)
        {
            // Find end of line
            long lineStart = pos;
            while (pos < fileSize && pointer[pos] != (byte)'\n')
                pos++;

            long lineEnd = pos;
            if (pos < fileSize) pos++;

            // Handle \r\n
            if (lineEnd > lineStart && pointer[lineEnd - 1] == (byte)'\r')
                lineEnd--;

            if (lineEnd <= lineStart) continue;

            // Find semicolon
            long sepPos = lineStart;
            while (sepPos < lineEnd && pointer[sepPos] != (byte)';')
                sepPos++;

            if (sepPos >= lineEnd) continue;

            int nameLen = (int)(sepPos - lineStart);

            // Parse the numeric value from raw bytes
            double value = ParseDouble(pointer, sepPos + 1, lineEnd);

            // Get or create the region name string (interned on first encounter)
            var nameSpan = new ReadOnlySpan<byte>(pointer + lineStart, nameLen);
            string name = Encoding.UTF8.GetString(nameSpan);

            if (stats.TryGetValue(name, out var existing))
            {
                existing.Min = Math.Min(existing.Min, value);
                existing.Max = Math.Max(existing.Max, value);
                existing.Sum += value;
                existing.Count++;
            }
            else
            {
                stats[name] = new StationStats
                {
                    Min = value,
                    Max = value,
                    Sum = value,
                    Count = 1
                };
            }
        }
    }
    finally
    {
        accessor.SafeMemoryMappedViewHandle.ReleasePointer();
    }
}

// Sort alphabetically and format output
var sorted = stats.OrderBy(kv => kv.Key, StringComparer.Ordinal);

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

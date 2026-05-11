using System.Diagnostics;
using System.Globalization;

string inputFile = args.Length > 0 ? args[0] : "measurements.txt";

if (!File.Exists(inputFile))
{
    Console.Error.WriteLine($"File not found: {inputFile}");
    return 1;
}

var sw = Stopwatch.StartNew();

// Track stats per region
var stats = new Dictionary<string, StationStats>();

using (var reader = new StreamReader(inputFile))
{
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        int semicolonIndex = line.IndexOf(';');
        if (semicolonIndex < 0) continue;

        // Note(akhe): This is the first optimization we do in the default solution. Run the benchmarkdotnet solution to see the difference in these parsing methods.
        //var parts = line.Split(';');
        //result = (parts[0], double.Parse(parts[1], CultureInfo.InvariantCulture));

        var name = line[..semicolonIndex];
        var value = double.Parse(line.AsSpan(semicolonIndex + 1), CultureInfo.InvariantCulture);

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

class StationStats
{
    public double Min;
    public double Max;
    public double Sum;
    public long Count;
}

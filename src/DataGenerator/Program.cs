using System.Diagnostics;
using System.Globalization;
using System.Text;

// Parse arguments
long rowCount = 1_000_000_000;
string outputFile = "measurements.txt";

if (args.Length >= 1 && long.TryParse(args[0], out var parsed))
    rowCount = parsed;
if (args.Length >= 2)
    outputFile = args[1];

Console.WriteLine($"Generating {rowCount:N0} rows to {outputFile}...");
var sw = Stopwatch.StartNew();

// Azure regions with latency profiles from Seattle (mean, stddev for log-normal)
// The log-normal parameters (mu, sigma) are derived so that the generated latency
// clusters around the "mean" with realistic right-tail spikes.
var regions = new (string Name, double MeanMs, double StdDevMs)[]
{
    // Local / West US
    ("westus", 8.0, 3.0),
    ("westus2", 5.0, 2.0),
    ("westus3", 6.0, 2.5),

    // Nearby US
    ("centralus", 35.0, 8.0),
    ("westcentralus", 32.0, 7.0),
    ("northcentralus", 42.0, 10.0),

    // Far US / Canada
    ("southcentralus", 55.0, 12.0),
    ("eastus", 65.0, 14.0),
    ("eastus2", 67.0, 14.0),
    ("canadacentral", 60.0, 13.0),
    ("canadaeast", 70.0, 15.0),

    // Latin America
    ("brazilsouth", 170.0, 30.0),
    ("mexicocentral", 130.0, 25.0),

    // Europe — West
    ("northeurope", 140.0, 25.0),
    ("westeurope", 145.0, 25.0),
    ("uksouth", 138.0, 24.0),
    ("ukwest", 142.0, 25.0),
    ("francecentral", 148.0, 26.0),
    ("francesouth", 155.0, 27.0),
    ("belgiumcentral", 144.0, 25.0),

    // Europe — Central/North
    ("germanywestcentral", 150.0, 26.0),
    ("germanynorth", 155.0, 27.0),
    ("switzerlandnorth", 152.0, 26.0),
    ("switzerlandwest", 154.0, 27.0),
    ("norwayeast", 160.0, 28.0),
    ("norwaywest", 162.0, 28.0),
    ("swedencentral", 158.0, 27.0),
    ("finlandcentral", 165.0, 29.0),
    ("denmarkeast", 155.0, 27.0),
    ("polandcentral", 160.0, 28.0),
    ("italynorth", 155.0, 27.0),
    ("spaincentral", 160.0, 28.0),
    ("austriaeast", 153.0, 27.0),
    ("greece", 168.0, 30.0),

    // Asia — Near (Pacific)
    ("eastasia", 135.0, 25.0),
    ("japaneast", 95.0, 18.0),
    ("japanwest", 105.0, 20.0),
    ("koreacentral", 120.0, 22.0),
    ("koreasouth", 125.0, 23.0),

    // Asia — Far
    ("southeastasia", 170.0, 30.0),
    ("centralindia", 210.0, 35.0),
    ("southindia", 220.0, 36.0),
    ("westindia", 215.0, 35.0),
    ("indonesiacentral", 190.0, 32.0),
    ("malaysiawest", 180.0, 30.0),

    // Oceania
    ("australiaeast", 160.0, 28.0),
    ("australiasoutheast", 165.0, 29.0),
    ("australiacentral", 162.0, 28.0),
    ("newzealandnorth", 175.0, 30.0),

    // Middle East / Africa
    ("uaenorth", 250.0, 40.0),
    ("uaecentral", 255.0, 40.0),
    ("qatarcentral", 260.0, 42.0),
    ("israelcentral", 220.0, 36.0),
    ("southafricanorth", 300.0, 50.0),
    ("saudiarabiaeast", 265.0, 42.0),
};

// Compute log-normal parameters from desired mean and stddev
static (double mu, double sigma) ToLogNormalParams(double mean, double stddev)
{
    double variance = stddev * stddev;
    double sigma2 = Math.Log(1.0 + variance / (mean * mean));
    double mu = Math.Log(mean) - sigma2 / 2.0;
    return (mu, Math.Sqrt(sigma2));
}

var regionParams = regions
    .Select(r => (r.Name, LogNormal: ToLogNormalParams(r.MeanMs, r.StdDevMs)))
    .ToArray();

int threadCount = Environment.ProcessorCount;
long rowsPerThread = rowCount / threadCount;
long remainder = rowCount % threadCount;

using var fileStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);

// Generate in parallel, write sequentially in chunks
var tasks = new Task<byte[]>[threadCount];
long rowsWritten = 0;

// Process in batches to avoid holding all data in memory
const int batchSize = 10_000_000; // 10M rows per batch

while (rowsWritten < rowCount)
{
    long batchRows = Math.Min(batchSize, rowCount - rowsWritten);
    long batchPerThread = batchRows / threadCount;
    long batchRemainder = batchRows % threadCount;

    for (int t = 0; t < threadCount; t++)
    {
        int threadIndex = t;
        long threadRows = batchPerThread + (threadIndex < batchRemainder ? 1 : 0);
        long seed = rowsWritten + threadIndex * batchPerThread;

        tasks[t] = Task.Run(() =>
        {
            var rng = new Random(unchecked((int)seed));
            var sb = new StringBuilder((int)(threadRows * 30));

            for (long i = 0; i < threadRows; i++)
            {
                var (name, (mu, sigma)) = regionParams[rng.Next(regionParams.Length)];

                // Generate log-normal sample: exp(mu + sigma * Z)
                double u1 = 1.0 - rng.NextDouble();
                double u2 = rng.NextDouble();
                double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                double latency = Math.Exp(mu + sigma * z);

                // Clamp to reasonable range
                latency = Math.Max(0.01, Math.Min(latency, 9999.99));

                sb.Append(name);
                sb.Append(';');
                sb.Append(latency.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        });
    }

    await Task.WhenAll(tasks);

    for (int t = 0; t < threadCount; t++)
    {
        var bytes = tasks[t].Result;
        await fileStream.WriteAsync(bytes);
    }

    rowsWritten += batchRows;

    if (rowsWritten % 100_000_000 == 0 || rowsWritten == rowCount)
        Console.Write($"\r  {rowsWritten:N0} / {rowCount:N0} rows ({100.0 * rowsWritten / rowCount:F1}%)");
}

Console.WriteLine();
Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F1}s — {new FileInfo(outputFile).Length / (1024.0 * 1024 * 1024):F2} GB");

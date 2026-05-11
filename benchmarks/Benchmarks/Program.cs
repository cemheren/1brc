using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Globalization;

BenchmarkRunner.Run<ParsingBenchmarks>();

/// <summary>
/// Example micro-benchmarks comparing different parsing strategies.
/// Run with: dotnet run -c Release --project benchmarks/Benchmarks
/// 
/// Add your own benchmarks here to compare approaches before committing
/// to a full solution. For example:
///   - Line splitting strategies
///   - Number parsing approaches  
///   - Hash map implementations
///   - Buffer sizes for file I/O
/// </summary>
[MemoryDiagnoser]
public class ParsingBenchmarks
{
    private string[] _lines = null!;
    private byte[][] _lineBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Simulate typical input lines
        _lines =
        [
            "westus2;4.73",
            "southafricanorth;312.88",
            "japaneast;91.05",
            "germanywestcentral;150.42",
            "centralus;38.22",
            "australiasoutheast;165.91",
            "swedencentral;158.33",
            "brazilsouth;170.67",
        ];

        _lineBytes = _lines.Select(System.Text.Encoding.UTF8.GetBytes).ToArray();
    }

    [Benchmark(Baseline = true)]
    public (string name, double value) StringSplit()
    {
        // Naive approach: string.Split + double.Parse
        (string, double) result = default;
        foreach (var line in _lines)
        {
            var parts = line.Split(';');
            result = (parts[0], double.Parse(parts[1], CultureInfo.InvariantCulture));
        }
        return result;
    }

    [Benchmark]
    public (string name, double value) IndexOfSubstring()
    {
        // Better: IndexOf + Substring, avoids array allocation from Split
        (string, double) result = default;
        foreach (var line in _lines)
        {
            int sep = line.IndexOf(';');
            result = (line[..sep], double.Parse(line.AsSpan(sep + 1), provider: CultureInfo.InvariantCulture));
        }
        return result;
    }

    [Benchmark]
    public (int nameLen, double value) SpanByteParsing()
    {
        // Advanced: work entirely with byte spans, no string allocations
        (int, double) result = default;
        foreach (var bytes in _lineBytes)
        {
            var span = bytes.AsSpan();
            int sep = span.IndexOf((byte)';');
            var nameSpan = span[..sep];
            var valueSpan = span[(sep + 1)..];

            // Hand-rolled decimal parse from ASCII bytes
            double value = 0;
            int decimalPos = -1;
            for (int i = 0; i < valueSpan.Length; i++)
            {
                byte b = valueSpan[i];
                if (b == '.')
                {
                    decimalPos = i;
                    continue;
                }
                value = value * 10 + (b - '0');
            }
            if (decimalPos >= 0)
            {
                int decimals = valueSpan.Length - decimalPos - 1;
                for (int i = 0; i < decimals; i++)
                    value /= 10.0;
            }

            result = (nameSpan.Length, value);
        }
        return result;
    }
}

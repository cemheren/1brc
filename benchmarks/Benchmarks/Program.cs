using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

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

////You should see something like below: 
////| Method | Mean | Error | StdDev | Median | Ratio | RatioSD | Gen0 | Gen1 | Allocated | Alloc Ratio |
////| ----------------- | ---------:| ---------:| ---------:| ---------:| ------:| --------:| -------:| -------:| ----------:| ------------:|
////| StringSplit | 387.6 ns | 1.89 ns | 1.68 ns | 387.3 ns | 1.00 | 0.01 | 0.1616 | 0.0005 | 1016 B | 1.00 |
////| IndexOfSubstring | 323.1 ns | 24.73 ns | 72.90 ns | 377.4 ns | 0.83 | 0.19 | 0.0637 | - | 400 B | 0.39 |
[MemoryDiagnoser]
public class ParsingBenchmarks
{
    private string[] _lines = null!;

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
}

using System.Diagnostics;
using System.Globalization;

namespace BaselineSolution.Tests;

/// <summary>
/// Integration tests that validate any 1BRC solution by running it as a process.
/// Participants can add a project reference or change the executable path to test their solution.
/// </summary>
public class SolutionTests : IDisposable
{
    private readonly string _testDir;

    public SolutionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"1brc_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private string CreateTestFile(string content)
    {
        var path = Path.Combine(_testDir, $"test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    private static string RunSolution(string inputFile)
    {
        var testAssemblyDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(repoRoot, "src", "BaselineSolution", "BaselineSolution.csproj");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" -- \"{inputFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(60_000);

        Assert.Equal(0, proc.ExitCode);
        return stdout.Trim();
    }

    [Fact]
    public void SingleRegion_SingleRow()
    {
        var file = CreateTestFile("westus2;5.25\n");
        var result = RunSolution(file);
        Assert.Equal("{westus2=5.25/5.25/5.25}", result);
    }

    [Fact]
    public void SingleRegion_MultipleRows()
    {
        var file = CreateTestFile(
            "eastus;10.00\n" +
            "eastus;20.00\n" +
            "eastus;30.00\n");
        var result = RunSolution(file);
        Assert.Equal("{eastus=10.00/20.00/30.00}", result);
    }

    [Fact]
    public void MultipleRegions_AlphabeticalOrder()
    {
        var file = CreateTestFile(
            "westus;8.00\n" +
            "eastus;65.00\n" +
            "centralus;35.00\n");
        var result = RunSolution(file);
        Assert.Equal("{centralus=35.00/35.00/35.00, eastus=65.00/65.00/65.00, westus=8.00/8.00/8.00}", result);
    }

    [Fact]
    public void MeanCalculation_RoundsCorrectly()
    {
        // 10.10 + 20.30 + 30.17 = 60.57 / 3 = 20.19
        var file = CreateTestFile(
            "canadacentral;10.10\n" +
            "canadacentral;20.30\n" +
            "canadacentral;30.17\n");
        var result = RunSolution(file);
        Assert.Equal("{canadacentral=10.10/20.19/30.17}", result);
    }

    [Fact]
    public void SmallValues_NearZero()
    {
        var file = CreateTestFile(
            "westus3;0.50\n" +
            "westus3;0.10\n" +
            "westus3;1.20\n");
        var result = RunSolution(file);
        Assert.Equal("{westus3=0.10/0.60/1.20}", result);
    }

    [Fact]
    public void LargeValues_HighLatency()
    {
        var file = CreateTestFile(
            "southafricanorth;450.99\n" +
            "southafricanorth;300.00\n" +
            "southafricanorth;150.01\n");
        var result = RunSolution(file);
        Assert.Equal("{southafricanorth=150.01/300.33/450.99}", result);
    }

    [Fact]
    public void MixedRegions_FullScenario()
    {
        var file = CreateTestFile(
            "westus2;3.50\n" +
            "japaneast;95.00\n" +
            "westus2;6.50\n" +
            "southafricanorth;300.00\n" +
            "japaneast;105.00\n" +
            "westus2;5.00\n" +
            "southafricanorth;250.00\n" +
            "japaneast;85.00\n");
        var result = RunSolution(file);

        // westus2: min=3.50, mean=5.00, max=6.50
        // japaneast: min=85.00, mean=95.00, max=105.00
        // southafricanorth: min=250.00, mean=275.00, max=300.00
        Assert.Equal(
            "{japaneast=85.00/95.00/105.00, southafricanorth=250.00/275.00/300.00, westus2=3.50/5.00/6.50}",
            result);
    }

    [Fact]
    public void IdenticalValues_AllSame()
    {
        var file = CreateTestFile(
            "norwayeast;42.42\n" +
            "norwayeast;42.42\n" +
            "norwayeast;42.42\n");
        var result = RunSolution(file);
        Assert.Equal("{norwayeast=42.42/42.42/42.42}", result);
    }

    [Fact]
    public void ManyRegions_VerifySortOrder()
    {
        var file = CreateTestFile(
            "westus;5.00\n" +
            "australiaeast;160.00\n" +
            "brazilsouth;170.00\n" +
            "qatarcentral;260.00\n" +
            "eastasia;135.00\n" +
            "northeurope;140.00\n" +
            "finlandcentral;165.00\n" +
            "germanywestcentral;150.00\n");
        var result = RunSolution(file);

        Assert.StartsWith("{australiaeast=", result);
        Assert.EndsWith("westus=5.00/5.00/5.00}", result);
    }
}

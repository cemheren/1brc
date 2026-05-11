# 1️⃣🐝🏎️ The One Billion Row Challenge — C# Edition

A fun exploration of how far modern C#/.NET can be pushed for aggregating one billion rows from a text file.
Grab your `Span<T>`s, reach for `Memory<byte>`, optimize your parsing, go parallel — and create the fastest implementation!

Inspired by [Gunnar Morling's original 1BRC](https://github.com/gunnarmorling/1brc) (Java), reimagined for .NET 9.

## The Challenge

You are given a text file containing **one billion** latency measurements from Azure data centers, as observed from Seattle.
Each row is one measurement in the format:

```
<string: region name>;<double: latency in ms>
```

The latency has exactly **two fractional digits**. Example rows:

```
westus2;4.73
southafricanorth;312.88
japaneast;91.05
centralus;38.22
westus2;5.91
northeurope;142.67
```

There are **55 unique region names** in the file.

### Your Task

Write a C# program that:

1. Reads the file (`measurements.txt`)
2. Calculates the **min**, **mean**, and **max** latency per region
3. Emits the results on stdout, sorted alphabetically by region name

### Expected Output Format

```
{australiacentral=120.45/162.30/210.88, australiaeast=115.22/158.47/205.31, ..., westus3=1.05/5.82/12.44}
```

- Wrapped in `{` and `}`
- Entries separated by `, `
- Each entry: `region=min/mean/max`
- Values rounded to **2 decimal places**
- Sorted alphabetically (ordinal) by region name

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Generate the Data

```bash
# Generate the full 1 billion row file (~14 GB)
dotnet run --project src/DataGenerator -- 1000000000

# Or generate a smaller file for testing
dotnet run --project src/DataGenerator -- 20000000 small_measurements.txt
```

### Run the Baseline Solution

```bash
dotnet run --project src/BaselineSolution -c Release -- measurements.txt
```

### Run the Tests

Use the included tests to validate your solution produces correct output:

```bash
dotnet test tests/BaselineSolution.Tests
```

> **Tip:** To test your own solution, modify `SolutionTests.cs` to point to your project instead of `BaselineSolution`.

## Rules

1. **Language:** C# on .NET 9
2. **No external NuGet packages** — standard library only
3. **Input:** Read from a file path passed as the first command-line argument (default: `measurements.txt`)
4. **Output:** Results must be printed to `stdout` in the exact format specified above
5. **Correctness:** Your solution must pass all provided unit tests
6. **Timing:** Wall-clock time from process start to exit, measured externally

## How to Submit

1. Create your solution as a new project under `src/` (e.g., `src/MySolution/`)
2. Add it to the solution: `dotnet sln add src/MySolution/MySolution.csproj`
3. Verify it passes: `dotnet test`
4. Submit a PR

## Evaluation

Solutions will be evaluated on a standard machine. The metric is **wall-clock time** to process the full 1 billion row file.

The baseline solution is intentionally naive — single-threaded `StreamReader` with `string.Split`. There is a **lot** of room for improvement. Some areas to explore:

- Memory-mapped files (`MemoryMappedFile`)
- `Span<byte>` / `ReadOnlySpan<byte>` for zero-allocation parsing
- Parallel chunk processing
- Custom hash maps tuned for the key set
- SIMD for delimiter scanning
- Avoiding `string` allocations entirely
- `stackalloc` and buffer pooling

## Project Structure

```
├── 1brc.slnx                          # Solution file
├── README.md
├── benchmarks/
│   └── Benchmarks/                     # BenchmarkDotNet micro-benchmarks
│       └── Program.cs
├── src/
│   ├── DataGenerator/                  # Generates measurements.txt
│   │   └── Program.cs
│   └── BaselineSolution/               # Naive reference implementation
│       └── Program.cs
└── tests/
    └── BaselineSolution.Tests/         # Correctness validation tests
        └── SolutionTests.cs
```

Good luck, and may the fastest `Span<T>` win! 🏎️

## Profiling Guide

Before optimizing, **measure**. Here are the built-in tools available without any NuGet packages.

### 1. Measure Wall-Clock Time

```powershell
# PowerShell
Measure-Command { dotnet run --project src/BaselineSolution -c Release -- measurements.txt } | Select-Object TotalSeconds

# Or use the Stopwatch already built into the baseline (printed to stderr)
dotnet run --project src/BaselineSolution -c Release -- measurements.txt > output.txt
```

### 2. dotnet-counters (Live Runtime Metrics)

See GC pressure, thread pool usage, and allocations in real time:

```bash
# Install once
dotnet tool install --global dotnet-counters

# In one terminal, start your solution
dotnet run --project src/BaselineSolution -c Release -- measurements.txt

# In another terminal, attach
dotnet-counters monitor --process-id <PID> --counters System.Runtime
```

Key metrics to watch:
- `GC Heap Size` — are you allocating too much?
- `Gen 0/1/2 GC Count` — frequent GCs kill throughput
- `ThreadPool Thread Count` — are you saturating cores?
- `Allocation Rate` — target: as close to zero as possible

### 3. dotnet-trace (CPU Profiling)

Capture a trace and view it in Visual Studio, PerfView, or Speedscope:

```bash
# Install once
dotnet tool install --global dotnet-trace

# Build first, then trace the compiled binary directly
dotnet build src/BaselineSolution -c Release
dotnet-trace collect --duration 00:00:10 -- src/BaselineSolution/bin/Release/net9.0/BaselineSolution measurements.txt
```

Open the `.nettrace` file in:
- **Visual Studio** → Diagnostics Tools (built-in)
- **[Speedscope](https://www.speedscope.app/)** → convert first: `dotnet-trace convert trace.nettrace --format speedscope`
- **PerfView** → Windows only, very powerful

### 4. dotnet-gcdump (GC/Memory Analysis)

Find what objects are eating your heap:

```bash
# Install once
dotnet tool install --global dotnet-gcdump

# Capture a GC dump
dotnet-gcdump collect --process-id <PID>
```

Open the `.gcdump` file in Visual Studio to see object counts and retained sizes.

### 5. Environment Variables for Quick Diagnostics

```bash
# Show GC stats at exit
set DOTNET_gcServer=1
set DOTNET_GCHeapCount=8

# Enable Event Pipe for allocation tracking
set DOTNET_EnableEventPipe=1
```

### 6. BenchmarkDotNet (for Micro-Benchmarks)

A scaffold project is included at `benchmarks/Benchmarks/` with example benchmarks comparing parsing strategies:

```bash
dotnet run -c Release --project benchmarks/Benchmarks
```

This compares three approaches out of the box:
- `string.Split()` — naive baseline
- `IndexOf` + `Substring` — avoids Split's array allocation
- `Span<byte>` hand-rolled parsing — zero allocation

Add your own benchmarks to compare e.g.:
- `Dictionary<string, T>` vs custom hash map
- `FileStream` buffer sizes
- `MemoryMappedFile` vs sequential reads
- SIMD delimiter scanning

### Profiling Tips

| Symptom | Likely Cause | Fix Direction |
|---|---|---|
| High Gen 0/1 GC count | `string` allocations per line | Parse with `Span<byte>`, avoid `SubString` |
| One core at 100%, rest idle | Single-threaded I/O | Partition file, process chunks in parallel |
| Slow `double.Parse` in trace | Culture-aware parsing overhead | Hand-roll decimal parsing from bytes |
| Large GC heap | Storing strings for every row | Intern region names or use byte keys |
| Flat CPU but slow | I/O bound, small reads | Use `MemoryMappedFile` or large `FileStream` buffers |

```

BenchmarkDotNet v0.15.6, macOS Sequoia 15.7.2 (24G325) [Darwin 24.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  Job-YFEFPZ : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

IterationCount=10  WarmupCount=3  

```
| Method                                 | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| &#39;SELECT all fortunes (pool)&#39;           | 300.4 μs |  7.52 μs |  4.97 μs | 0.9766 |   8.09 KB |
| &#39;SELECT all fortunes (pool, prepared)&#39; | 286.5 μs | 17.37 μs | 10.33 μs | 0.9766 |   8.15 KB |

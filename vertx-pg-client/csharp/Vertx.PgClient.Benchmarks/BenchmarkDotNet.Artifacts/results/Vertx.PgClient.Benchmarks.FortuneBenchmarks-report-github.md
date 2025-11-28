```

BenchmarkDotNet v0.15.6, macOS Sequoia 15.7.2 (24G325) [Darwin 24.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  Job-YFEFPZ : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

IterationCount=10  WarmupCount=3  

```
| Method                                 | Mean     | Error    | StdDev  | Gen0   | Allocated |
|--------------------------------------- |---------:|---------:|--------:|-------:|----------:|
| &#39;SELECT all fortunes (pool)&#39;           | 302.7 μs | 10.81 μs | 7.15 μs | 0.9766 |   8.09 KB |
| &#39;SELECT all fortunes (pool, prepared)&#39; | 579.2 μs | 15.31 μs | 8.01 μs | 0.9766 |   8.62 KB |

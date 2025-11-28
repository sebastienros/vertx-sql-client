```

BenchmarkDotNet v0.15.6, macOS Sequoia 15.7.2 (24G325) [Darwin 24.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  Job-YFEFPZ : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a

IterationCount=10  WarmupCount=3  

```
| Method                                       | Mean     | Error    | StdDev  | Allocated |
|--------------------------------------------- |---------:|---------:|--------:|----------:|
| &#39;Npgsql: SELECT all fortunes (simple query)&#39; | 301.4 μs | 10.19 μs | 6.06 μs |   3.01 KB |

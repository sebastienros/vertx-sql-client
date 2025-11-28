```

BenchmarkDotNet v0.15.6, Linux Ubuntu 24.04.3 LTS (Noble Numbat)
AMD EPYC 7763 3.21GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YFEFPZ : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

WarmupCount=3  

```
| Method                                         | Job        | IterationCount | LaunchCount | Mean     | Error    | StdDev  | Allocated |
|----------------------------------------------- |----------- |--------------- |------------ |---------:|---------:|--------:|----------:|
| &#39;Npgsql: SELECT all fortunes (prepared query)&#39; | Job-YFEFPZ | 10             | Default     | 228.3 μs |  3.69 μs | 2.20 μs |   2.92 KB |
| &#39;Npgsql: SELECT all fortunes (prepared query)&#39; | ShortRun   | 3              | 1           | 229.4 μs | 33.82 μs | 1.85 μs |   2.91 KB |

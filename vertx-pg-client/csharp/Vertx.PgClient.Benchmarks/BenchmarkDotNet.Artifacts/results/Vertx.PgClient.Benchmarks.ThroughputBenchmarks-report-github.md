```

BenchmarkDotNet v0.14.0, macOS Sequoia 15.7.2 (24G325) [Darwin 24.6.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD
  Job-SLVJPQ : .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD

IterationCount=1  RunStrategy=Monitoring  WarmupCount=1  

```
| Method                               | Mean    | Error |
|------------------------------------- |--------:|------:|
| &#39;Pipelined fortunes throughput (5s)&#39; | 10.01 s |    NA |

# Rushframe Benchmarks

Pure hot-path microbenchmarks for animation lookup, cached sequence duration, and real-time render-plan interval queries.

```powershell
dotnet run --project .\benchmarks\Rushframe.Benchmarks\Rushframe.Benchmarks.csproj -c Release -- --filter '*'
```

BenchmarkDotNet output is written to `BenchmarkDotNet.Artifacts`. Compare mean, P95-equivalent distribution diagnostics where available, and allocated bytes/op before accepting changes to these paths.

This project is intentionally outside `Rushframe.slnx` so normal application restore/build does not require BenchmarkDotNet.

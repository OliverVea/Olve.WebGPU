using BenchmarkDotNet.Running;

// dotnet run -c Release --project benchmarks/Olve.WebGPU.Benchmarks -- --filter '*'
// CI: -- --filter '*' --job Dry --inProcess
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

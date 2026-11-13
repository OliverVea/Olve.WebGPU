using Olve.WebGPU.Tests;

// The same checks as Olve.WebGPU.Tests, in the browser; the exit code becomes the page title (main.js)
var failures = await Checks.RunAsync(Console.WriteLine, dawnLayout: null);
Console.WriteLine(failures == 0 ? "PASS" : $"FAIL ({failures})");
return failures;

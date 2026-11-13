using Android.App;
using Android.OS;
using Android.Util;

namespace Olve.WebGPU.Tests;

/// <summary>Runs the shared checks on start, off the UI thread, and logs PASS or FAIL for CI to read from logcat.</summary>
[Activity(Label = "Olve.WebGPU tests", MainLauncher = true)]
public class MainActivity : Activity
{
    private const string Tag = "OlveWebGPU";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _ = Task.Run(async () =>
        {
            var failures = 0;
            try
            {
                failures = await Checks.RunAsync(message => Log.Info(Tag, message), DawnLayout.Check);
            }
            catch (Exception e)
            {
                Log.Error(Tag, e.ToString());
                failures++;
            }

            Log.Info(Tag, failures == 0 ? "PASS" : $"FAIL ({failures})");
        });
    }
}

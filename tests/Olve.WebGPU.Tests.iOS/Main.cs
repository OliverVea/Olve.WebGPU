using System.Runtime.InteropServices;
using Foundation;
using Olve.WebGPU.Tests;
using UIKit;

UIApplication.Main(args, null, typeof(AppDelegate));

/// <summary>Runs the shared checks on launch, off the main thread, prints PASS or FAIL, and exits with the failure count.</summary>
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds) { RootViewController = new UIViewController() };
        Window.MakeKeyAndVisible();

        // The layout checker is a static library linked into the app, like Dawn: its symbols are in the app's executable
        NativeLibrary.SetDllImportResolver(typeof(AppDelegate).Assembly,
            static (name, _, _) => name == "webgpu_layout_check" ? NativeLibrary.GetMainProgramHandle() : 0);

        _ = Task.Run(async () =>
        {
            var failures = 0;
            try
            {
                failures = await Checks.RunAsync(Console.WriteLine, DawnLayout.Check);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                failures++;
            }

            Console.WriteLine(failures == 0 ? "PASS" : $"FAIL ({failures})");
            Environment.Exit(failures == 0 ? 0 : 1);
        });
        return true;
    }
}

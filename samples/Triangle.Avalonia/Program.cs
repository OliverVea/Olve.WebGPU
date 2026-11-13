using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Triangle.Avalonia;

// The triangle in an Avalonia window, next to ordinary Avalonia controls.
//   --frames N    closes the app after the viewport has drawn N frames; the exit code is 1 if it couldn't draw
var frameLimit = Array.IndexOf(args, "--frames") is var i and >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : int.MaxValue;
return AppBuilder.Configure(() => new App(frameLimit)).UsePlatformDetect().StartWithClassicDesktopLifetime(args);

namespace Triangle.Avalonia
{
    public sealed class App(int frameLimit) : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var viewport = new WebGpuView();
                viewport.FrameDrawn += frame =>
                {
                    if (frame >= frameLimit)
                    {
                        desktop.Shutdown();
                    }
                };
                viewport.Failed += () => desktop.Shutdown(1);

                // Ordinary Avalonia controls beside the viewport: the slider changes what the renderer draws
                var blue = new Slider { Minimum = 0, Maximum = 1, Value = 0.15, Width = 180 };
                blue.ValueChanged += (_, e) => viewport.BackgroundBlue = e.NewValue;
                var panel = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Olve.WebGPU", FontSize = 20, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = "Background blue" },
                        blue,
                    },
                };

                desktop.MainWindow = new Window
                {
                    Title = "Olve.WebGPU in Avalonia",
                    Width = 900,
                    Height = 560,
                    Content = new DockPanel
                    {
                        Children = { panel, viewport },
                        LastChildFill = true,
                    },
                };
                DockPanel.SetDock(panel, Dock.Left);
                panel.VerticalAlignment = VerticalAlignment.Stretch;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}

using System.Runtime.InteropServices;

namespace Triangle.Desktop;

/// <summary>
/// The CAMetalLayer a macOS window needs for SurfaceSourceMetalLayer, made through the Objective-C runtime:
/// the window's content view gets a layer of its own, which Dawn presents to.
/// </summary>
internal static class MetalLayer
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    public static nint For(nint nsWindow)
    {
        var contentView = Send(nsWindow, Selector("contentView"));
        var layer = Send(objc_getClass("CAMetalLayer"), Selector("layer"));

        // Match the screen's pixels on Retina displays: the layer's drawable is the surface Dawn configures
        var scale = SendDouble(nsWindow, Selector("backingScaleFactor"));
        SendDouble(layer, Selector("setContentsScale:"), scale);
        SendBool(contentView, Selector("setWantsLayer:"), 1);
        Send(contentView, Selector("setLayer:"), layer);
        return layer;
    }

    private static nint Selector(string name) => sel_registerName(name);

    [DllImport(ObjC)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern nint sel_registerName(string name);

    // objc_msgSend isn't variadic on arm64 or x64: each signature it's called with is declared as its own import

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Send(nint receiver, nint selector, nint argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendBool(nint receiver, nint selector, byte argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendDouble(nint receiver, nint selector, double argument);
}

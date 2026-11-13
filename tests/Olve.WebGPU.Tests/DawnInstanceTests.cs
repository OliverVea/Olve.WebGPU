using System.Runtime.InteropServices;
using Olve.WebGPU.Native;

namespace Olve.WebGPU.Tests;

public class DawnInstanceTests
{
    // native/dawn_layout_check.cpp, compiled by each platform's C++ compiler against Dawn's DawnNative.h
    [DllImport("webgpu_layout_check")]
    private static extern int dawn_layout_values(long[]? values);

    [Test]
    public async Task DawnInstanceDescriptorMatchesTheCppLayout()
    {
        var native = new long[dawn_layout_values(null)];
        _ = dawn_layout_values(native);
        var pointer = IntPtr.Size;

        (string Name, long Value)[] managed =
        [
            ("sizeof(DawnInstanceDescriptor)", Marshal.SizeOf<WGPUDawnInstanceDescriptor>()),
            ("additionalRuntimeSearchPaths", Offset("AdditionalRuntimeSearchPaths")),
            ("platform", Offset("Platform")),
            ("backendValidationLevel", Offset("BackendValidationLevel")),
            ("beginCaptureOnStartup", Offset("BeginCaptureOnStartup")),
            ("loggingCallbackInfo", Offset("LoggingCallbackInfo")),
            ("sizeof(span<string_view>)", 2 * pointer),
            ("span data offset", 0),
            ("sizeof(string_view)", 2 * pointer),
            ("string_view data offset", DawnMarshalling.StringViewLengthFirst ? pointer : 0),
            ("sizeof(BackendValidationLevel)", sizeof(BackendValidationLevel)),
            ("sizeof(bool)", sizeof(byte)),
            ("BackendValidationLevel::Disabled", (long)BackendValidationLevel.Disabled),
        ];

        await Assert.That(native.Length).IsEqualTo(managed.Length);
        var mismatches = managed.Zip(native).Where(p => p.First.Value != p.Second).Select(p => $"{p.First.Name}: C# {p.First.Value}, C++ {p.Second}");
        await Assert.That(mismatches).IsEmpty();
    }

    [Test]
    public async Task DawnsInstanceLogGoesToTheLoggingCallback()
    {
        var messages = new List<(LoggingType Type, string Message)>();
        using var instance = Wgpu.CreateInstance(new InstanceDescriptor(), new DawnInstanceDescriptor
        {
            LoggingCallback = (type, message) =>
            {
                lock (messages)
                {
                    messages.Add((type, message));
                }
            },
        });

        // Without the TimedWaitAny feature, Dawn logs a timed wait as an error on the instance's log
        var status = instance.WaitAny([], 1);

        await Assert.That(status).IsEqualTo(WaitStatus.Error);
        await Assert.That(messages).IsEquivalentTo([(LoggingType.Error, "Timeout waits are either not enabled or not supported.")]);
    }

    private static long Offset(string field) => Marshal.OffsetOf<WGPUDawnInstanceDescriptor>(field).ToInt64();
}

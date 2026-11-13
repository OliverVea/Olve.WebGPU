// The C++ half of the layout check, desktop only: dawn::native::DawnInstanceDescriptor is C++ (dawn/native/DawnNative.h), so its
// layout depends on the C++ ABI and the standard library, which C can't show. Built into webgpu_layout_check by build-natives.
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <span>
#include <string_view>

#include <dawn/native/DawnNative.h>

#if defined(_WIN32)
#define LAYOUT_EXPORT extern "C" __declspec(dllexport)
#else
#define LAYOUT_EXPORT extern "C" __attribute__((visibility("default")))
#endif

using dawn::native::DawnInstanceDescriptor;

// The byte offset of a pointer inside an object's representation, or -1
template <typename T>
static int64_t PointerOffset(const T& value, const void* pointer) {
    unsigned char bytes[sizeof(T)];
    std::memcpy(bytes, &value, sizeof(T));
    for (size_t i = 0; i + sizeof(pointer) <= sizeof(T); i++) {
        if (std::memcmp(bytes + i, &pointer, sizeof(pointer)) == 0) {
            return static_cast<int64_t>(i);
        }
    }
    return -1;
}

// Fills out (when not null) in DawnLayout's order and returns how many values there are
LAYOUT_EXPORT int dawn_layout_values(int64_t* out) {
    static const char text[] = "abc";
    std::string_view view(text, 3);
    std::span<std::string_view> span(&view, 1);
    const int64_t values[] = {
        sizeof(DawnInstanceDescriptor),
        offsetof(DawnInstanceDescriptor, additionalRuntimeSearchPaths),
        offsetof(DawnInstanceDescriptor, platform),
        offsetof(DawnInstanceDescriptor, backendValidationLevel),
        offsetof(DawnInstanceDescriptor, beginCaptureOnStartup),
        offsetof(DawnInstanceDescriptor, loggingCallbackInfo),
        sizeof(std::span<std::string_view>),
        PointerOffset(span, &view),
        sizeof(std::string_view),
        PointerOffset(view, text),
        sizeof(dawn::native::BackendValidationLevel),
        sizeof(bool),
        static_cast<int64_t>(dawn::native::BackendValidationLevel::Disabled),
    };
    const int count = static_cast<int>(sizeof(values) / sizeof(values[0]));
    if (out) {
        std::memcpy(out, values, sizeof(values));
    }
    return count;
}

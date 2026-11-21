using System.Runtime.InteropServices;

namespace XIVLauncher.Common.Mac;

public static partial class MacGameWindow
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint OnScreenWindows = 1 << 0;
    private const uint ExcludeDesktopElements = 1 << 4;
    private const uint Utf8 = 0x08000100;
    private const int SignedInt32 = 3;

    private static readonly Lazy<nint> OwnerPidKey = new(() => MakeKey("kCGWindowOwnerPID"));
    private static readonly Lazy<nint> LayerKey = new(() => MakeKey("kCGWindowLayer"));
    private static readonly Lazy<nint> BoundsKey = new(() => MakeKey("kCGWindowBounds"));
    private static readonly Lazy<nint> WidthKey = new(() => MakeKey("Width"));
    private static readonly Lazy<nint> HeightKey = new(() => MakeKey("Height"));

    public static bool IsVisible(int processId)
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        var windows = CGWindowListCopyWindowInfo(OnScreenWindows | ExcludeDesktopElements, 0);
        if (windows == 0)
            return false;

        try
        {
            var count = CFArrayGetCount(windows);
            for (nint index = 0; index < count; index++)
            {
                var window = CFArrayGetValueAtIndex(windows, index);
                if (ReadNumber(window, OwnerPidKey.Value) != processId || ReadNumber(window, LayerKey.Value) != 0)
                    continue;

                var bounds = CFDictionaryGetValue(window, BoundsKey.Value);
                // Wine uses small helper windows as well as the actual game window.
                if (bounds != 0 && ReadNumber(bounds, WidthKey.Value) >= 320 && ReadNumber(bounds, HeightKey.Value) >= 200)
                    return true;
            }

            return false;
        }
        finally
        {
            CFRelease(windows);
        }
    }

    private static int? ReadNumber(nint dictionary, nint key)
    {
        var number = CFDictionaryGetValue(dictionary, key);
        return number != 0 && CFNumberGetValue(number, SignedInt32, out var value) ? value : null;
    }

    private static nint MakeKey(string key) => CFStringCreateWithCString(0, key, Utf8);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGWindowListCopyWindowInfo(uint options, uint relativeToWindow);

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string value, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(nint array);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool CFNumberGetValue(nint number, int type, out int value);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
}

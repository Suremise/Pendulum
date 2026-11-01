using System.Runtime.InteropServices;

namespace Pendulum.Core.Speech;

/// Whether Whisper's native libraries (whisper.cpp) can start on this PC. They're built against
/// Microsoft's Visual C++ runtime, which Windows doesn't include: on a PC where no app has
/// installed it, loading them fails. Checked before listening, so the user is told why, not
/// told to check their microphone after speaking.
public static class WhisperRuntime
{
    /// The Visual C++ runtime libraries whisper.cpp's DLLs import (vcomp140 is OpenMP).
    public static readonly IReadOnlyList<string> NativeDependencies =
        new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll" };

    /// The ones that can't be loaded here: shipped beside whisper.cpp's DLLs (runtimes\win-x64\)
    /// or in Windows' own folder, the two places Windows looks for them when Whisper.net loads
    /// those DLLs. Empty when Whisper can start.
    public static IReadOnlyList<string> MissingNativeDependencies(Func<string, bool>? canLoad = null)
    {
        canLoad ??= CanLoad;
        return NativeDependencies.Where(name => !canLoad(name)).ToList();
    }

    /// Where whisper.cpp's DLLs, and the runtime shipped with them, are.
    public static string NativeDirectory => Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64");

    private static bool CanLoad(string name)
    {
        if (File.Exists(Path.Combine(NativeDirectory, name)))
            return true;

        if (!NativeLibrary.TryLoad(name, typeof(WhisperRuntime).Assembly, DllImportSearchPath.System32, out var handle))
            return false;

        NativeLibrary.Free(handle);
        return true;
    }
}

using Pendulum.Core.Models;

namespace Pendulum.Core.Persistence;

/// Names of sounds, voices and speech models, as saved in reminders and settings. They may come
/// from a file imported or restored from anywhere, so each is used only when it is a plain file
/// name inside its own folder (Sounds\, Data\PiperModels\, Data\WhisperModels\): never a full
/// path, a network path (\\host\share\a.mp3) or one that climbs out with "..".
public static class DataFileName
{
    /// True for a plain file name: no folder, drive, network share or alternate stream.
    public static bool IsPlain(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && name is not "." and not ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.Contains(':')
        && !name.Contains('/')
        && !name.Contains('\\');

    /// The full path of <paramref name="name"/> inside <paramref name="folder"/>, or null when it
    /// isn't a plain file name or would land anywhere else.
    public static string? PathInside(string folder, string? name)
    {
        if (!IsPlain(name))
            return null;
        var root = Path.GetFullPath(folder);
        var full = Path.GetFullPath(Path.Combine(root, name!));
        return string.Equals(Path.GetDirectoryName(full), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }

    /// Blanks each reminder's sound that isn't a plain file name: that reminder plays no sound.
    public static void Clean(IEnumerable<TriggerTimer> reminders)
    {
        foreach (var t in reminders)
        {
            if (!string.IsNullOrEmpty(t.SoundFileName) && !IsPlain(t.SoundFileName))
                t.SoundFileName = null;
        }
    }

    /// Blanks the default sound, Whisper model and Piper voice when they aren't plain file names.
    public static void Clean(AppSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.DefaultSoundFileName) && !IsPlain(settings.DefaultSoundFileName))
            settings.DefaultSoundFileName = string.Empty;
        if (settings.WhisperModelFileName is not null && !IsPlain(settings.WhisperModelFileName))
            settings.WhisperModelFileName = null;
        if (settings.PiperVoiceModelFileName is not null && !IsPlain(settings.PiperVoiceModelFileName))
            settings.PiperVoiceModelFileName = null;
    }
}

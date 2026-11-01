using System.IO;
using Pendulum.App.Services;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Pendulum.Core.Speech;

namespace Pendulum.App.Tests;

/// At the moment of use, a sound or voice name from a reminder or settings is used only as a plain
/// file name in Pendulum's own folder, whatever the file it came from. No window is shown; files
/// stay in the test's temp folder, and nothing is read over a network.
[Collection(nameof(SettingsScreen))]
public sealed class DataFileNameUseTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-app-tests-" + Guid.NewGuid().ToString("N"));

    public DataFileNameUseTests()
    {
        AppPaths.BaseDirectoryOverride = _base;
        AppPaths.EnsureDirectories();
    }

    public void Dispose()
    {
        AppPaths.BaseDirectoryOverride = null;
        try { Directory.Delete(_base, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData(@"\\host\share\a.mp3")]
    [InlineData(@"C:\Windows\Media\chimes.wav")]
    [InlineData(@"..\Data\settings.json")]
    [InlineData("../a.wav")]
    public void A_sound_outside_Sounds_is_never_played(string name) =>
        Assert.Equal(string.Empty, AppServices.Instance.ResolveSoundPath(name));

    [Fact]
    public void A_plain_sound_name_plays_from_Sounds() =>
        Assert.Equal(Path.Combine(AppPaths.SoundsDirectory, "chime.wav"), AppServices.Instance.ResolveSoundPath("chime.wav"));

    [Theory]
    [InlineData(@"\\host\share\voice.onnx")]
    [InlineData(@"..\..\voice.onnx")]
    [InlineData(@"C:\elsewhere\voice.onnx")]
    public void A_Piper_voice_outside_PiperModels_is_never_given_to_piper(string name)
    {
        File.WriteAllText(Path.Combine(AppPaths.PiperModelsDirectory, "piper.exe"), "");  // never run
        AppServices.Instance.UseSettingsForTests(new AppSettings
        {
            TextToSpeechEngine = TextToSpeechEngine.Piper,
            PiperVoiceModelFileName = name,
        });

        using var engine = AppServices.Instance.CreateSpeechEngine();
        Assert.IsNotType<PiperSpeechService>(engine);
    }

    [Fact]
    public void A_plain_Piper_voice_in_PiperModels_is_used()
    {
        File.WriteAllText(Path.Combine(AppPaths.PiperModelsDirectory, "piper.exe"), "");  // never run
        File.WriteAllText(Path.Combine(AppPaths.PiperModelsDirectory, "voice.onnx"), "");
        AppServices.Instance.UseSettingsForTests(new AppSettings
        {
            TextToSpeechEngine = TextToSpeechEngine.Piper,
            PiperVoiceModelFileName = "voice.onnx",
        });

        using var engine = AppServices.Instance.CreateSpeechEngine();
        Assert.IsType<PiperSpeechService>(engine);
    }
}

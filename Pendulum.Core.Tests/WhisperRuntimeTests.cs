using Pendulum.Core.Speech;

namespace Pendulum.Core.Tests;

/// Whether Whisper's native libraries can start: the Visual C++ runtime they import. The decision
/// is tested with a fake loader; the real one is only checked not to fail.
public class WhisperRuntimeTests
{
    [Fact]
    public void Whisper_needs_the_four_visual_cpp_runtime_libraries_its_dlls_import()
    {
        Assert.Equal(new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll" }, WhisperRuntime.NativeDependencies);
    }

    [Fact]
    public void With_all_of_them_nothing_is_missing()
    {
        Assert.Empty(WhisperRuntime.MissingNativeDependencies(_ => true));
    }

    [Fact]
    public void On_a_pc_without_the_runtime_all_four_are_named()
    {
        Assert.Equal(WhisperRuntime.NativeDependencies, WhisperRuntime.MissingNativeDependencies(_ => false));
    }

    [Fact]
    public void A_pc_with_an_older_runtime_lacking_openmp_names_just_that()
    {
        Assert.Equal(new[] { "vcomp140.dll" }, WhisperRuntime.MissingNativeDependencies(name => name != "vcomp140.dll"));
    }

    // ---- Quick Reminder's choice: said before listening ----

    private static (string Dir, string Model) WhisperModelOnDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pendulum-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "ggml-base.en.bin"), new byte[] { 1 });
        return (dir, "ggml-base.en.bin");
    }

    [Fact]
    public void Whisper_without_its_runtime_is_a_problem_reported_before_listening()
    {
        var (dir, model) = WhisperModelOnDisk();
        try
        {
            var choice = DictationChoice.For(Models.SpeechToTextEngine.Whisper, model, dir, () => new[] { "vcomp140.dll" });

            Assert.Equal(DictationProblem.WhisperRuntimeMissing, choice.Problem);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Whisper_with_its_runtime_is_ready()
    {
        var (dir, model) = WhisperModelOnDisk();
        try
        {
            var choice = DictationChoice.For(Models.SpeechToTextEngine.Whisper, model, dir, () => Array.Empty<string>());

            Assert.Null(choice.Problem);
            Assert.True(choice.UseWhisper);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_windows_recognizer_doesnt_need_whispers_runtime()
    {
        var choice = DictationChoice.For(Models.SpeechToTextEngine.WindowsSpeechRecognition, null, Path.GetTempPath(), () => WhisperRuntime.NativeDependencies);

        Assert.Null(choice.Problem);
        Assert.False(choice.UseWhisper);
    }

    [Fact]
    public void A_missing_model_is_said_first()
    {
        var choice = DictationChoice.For(Models.SpeechToTextEngine.Whisper, "gone.bin", Path.GetTempPath(), () => WhisperRuntime.NativeDependencies);

        Assert.Equal(DictationProblem.WhisperModelMissing, choice.Problem);
    }

    [Fact]
    public void A_library_that_fails_to_load_while_listening_is_the_runtime_problem()
    {
        Assert.Equal(DictationProblem.WhisperRuntimeMissing, DictationChoice.Classify(new DllNotFoundException("vcomp140.dll")));
    }

    [Fact]
    public void The_real_check_runs_and_names_only_libraries_it_knows()
    {
        // This PC may or may not have the runtime: the check must just answer, from the known names.
        var missing = WhisperRuntime.MissingNativeDependencies();

        Assert.All(missing, name => Assert.Contains(name, WhisperRuntime.NativeDependencies));
    }
}

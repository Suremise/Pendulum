using System.IO;
using System.Xml.Linq;
using Pendulum.App.ViewModels;

namespace Pendulum.App.Tests;

/// Settings offers only the Piper voices whose terms let anyone use them, each with its licence
/// and the credit its licence asks for.
public sealed class DownloadOfferTests
{
    [Fact]
    public void Only_the_allowed_voices_are_offered_each_with_its_licence_and_credit()
    {
        var voices = SettingsViewModel.OfferedVoices;
        Assert.Equal(
            ["/en_GB/alba/medium", "/en_GB/jenny_dioco/medium", "/en_US/libritts_r/medium", "/en_GB/northern_english_male/medium"],
            voices.Select(v => v.Url[v.Url.IndexOf("/en_", StringComparison.Ordinal)..]));
        Assert.All(voices, v =>
        {
            Assert.StartsWith("https://huggingface.co/rhasspy/piper-voices/", v.Url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(v.Name));
            Assert.False(string.IsNullOrWhiteSpace(v.Licence));
            Assert.False(string.IsNullOrWhiteSpace(v.Credit));
        });
        // Jenny's licence asks for her to be named "Jenny (Dioco)".
        Assert.Contains(voices, v => v.Name.Contains("Jenny (Dioco)", StringComparison.Ordinal) && v.Credit.Contains("Jenny (Dioco)", StringComparison.Ordinal));
    }

    [Fact]
    public void Voices_whose_terms_dont_allow_use_are_never_named()
    {
        // Lessac's licence is for research only, and Amy's terms are unclear.
        var resx = Path.Combine(RepoRoot(), "Pendulum.App", "Resources", "Strings.resx");
        var text = string.Concat(XDocument.Load(resx).Root!.Elements("data").Select(d => d.Element("value")!.Value));
        Assert.DoesNotContain("lessac", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("en_US-amy", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Pendulum.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Pendulum.sln not found above the test's folder.");
    }
}

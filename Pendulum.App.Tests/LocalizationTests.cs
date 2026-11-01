using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Pendulum.App.Converters;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

/// Every user-visible string goes through Resources\Strings.resx, so Pendulum can be translated.
/// These tests read the App's source files and fail on a new literal in its UI code.
public sealed class LocalizationTests
{
    private static readonly string AppSource = Path.Combine(RepoRoot(), "Pendulum.App");

    // The XAML attributes that show text to the user, or name an element for a screen reader.
    private static readonly Regex XamlTextAttribute = new(
        @"(?<![\w.:])(Text|Content|Header|ToolTip|Title|PlaceholderText|Description|AutomationProperties\.Name)=""([^""]*)""");

    private static readonly Regex XamlSetterValue = new(
        @"Property=""(?:Text|Content|Header|ToolTip|Title)""\s+Value=""([^""]*)""");

    private static readonly Regex XamlInnerText = new(@">([^<>]*[A-Za-z][^<>]*)<");

    private static readonly Regex CSharpLiteral = new(@"\$?@?""((?:[^""\\]|\\.)*)""");

    [Fact]
    public void No_text_is_written_into_the_XAML()
    {
        var found = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
        {
            var text = WithoutXmlComments(File.ReadAllText(file));
            foreach (Match m in XamlTextAttribute.Matches(text))
                if (IsText(m.Groups[2].Value))
                    found.Add($"{Name(file)}: {m.Groups[1].Value}=\"{m.Groups[2].Value}\"");
            foreach (Match m in XamlSetterValue.Matches(text))
                if (IsText(m.Groups[1].Value))
                    found.Add($"{Name(file)}: Setter Value=\"{m.Groups[1].Value}\"");
            foreach (Match m in XamlInnerText.Matches(text))
                found.Add($"{Name(file)}: text \"{m.Groups[1].Value.Trim()}\"");
            foreach (Match m in Regex.Matches(text, @"StringFormat='?([^'},]*[A-Za-z][^'},]*)"))
                if (!m.Groups[1].Value.StartsWith("{x:Static", StringComparison.Ordinal) && !Regex.IsMatch(m.Groups[1].Value, @"^\{\}\{0:[^}]*\}$"))
                    found.Add($"{Name(file)}: StringFormat \"{m.Groups[1].Value}\"");
        }

        Assert.True(found.Count == 0,
            "Put these in Resources\\Strings.resx and use {x:Static res:Strings.Key}:\n" + string.Join("\n", found));
    }

    /// A literal that reads as words ("Add sound", "Copied") in the App's C#. Code that has to keep a
    /// literal (a file name, a registry path, a log tag) marks its line `// not user-visible`.
    [Fact]
    public void No_text_is_written_into_the_UI_code()
    {
        var found = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('[')
                    || line.Contains("not user-visible", StringComparison.Ordinal)
                    || line.Contains("CrashLog.Write(", StringComparison.Ordinal)
                    || line.Contains("nameof(", StringComparison.Ordinal))
                    continue;
                foreach (Match m in CSharpLiteral.Matches(StripComment(line)))
                    if (ReadsAsWords(m.Groups[1].Value, verbatim: m.Value.Contains("@\"", StringComparison.Ordinal)))
                        found.Add($"{Name(file)}:{i + 1}: \"{m.Groups[1].Value}\"");
            }
        }

        Assert.True(found.Count == 0,
            "Put these in Resources\\Strings.resx and use Strings.Key (or mark a line that isn't shown " +
            "to users `// not user-visible`):\n" + string.Join("\n", found));
    }

    [Fact]
    public void Every_string_the_code_uses_is_in_the_resx_and_every_one_in_the_resx_is_used()
    {
        var keys = ResxKeys(Path.Combine(AppSource, "Resources", "Strings.resx"));
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles("*.xaml").Concat(SourceFiles("*.cs")))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Strings\.([A-Za-z0-9_]+)"))
                used.Add(m.Groups[1].Value);
        used.Remove("ResourceManager");
        used.Remove("Culture");
        used.Remove("resx");  // "Strings.resx" in comments

        // Enum names are looked up by EnumSpacedDisplayConverter, not named in the code.
        var unused = keys.Where(k => !used.Contains(k) && !k.StartsWith("Enum_", StringComparison.Ordinal)).ToList();
        Assert.True(unused.Count == 0, "Keys nothing uses (remove them):\n" + string.Join("\n", unused));
        var missing = used.Where(u => !keys.Contains(u)).ToList();
        Assert.True(missing.Count == 0, "Used but not in the resx:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_Core_string_is_used_and_every_one_used_is_in_its_resx()
    {
        var core = Path.Combine(RepoRoot(), "Pendulum.Core");
        var keys = ResxKeys(Path.Combine(core, "Resources", "CoreStrings.resx"));
        var used = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"CoreStrings\.([A-Za-z0-9_]+)").Select(m => m.Groups[1].Value))
            .Where(k => k != "ResourceManager" && k != "Culture" && k != "resx")
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(keys.Order(), used.Order());
    }

    [Fact]
    public void The_enums_shown_on_screen_have_names_in_the_resx()
    {
        var converter = new EnumSpacedDisplayConverter();
        foreach (var value in Enum.GetValues<ThemeMode>().Cast<Enum>().Concat(Enum.GetValues<AlertMode>().Cast<Enum>()))
        {
            var key = $"Enum_{value.GetType().Name}_{value}";
            Assert.NotNull(Strings.ResourceManager.GetString(key, CultureInfo.InvariantCulture));
            Assert.Equal(Strings.ResourceManager.GetString(key, CultureInfo.InvariantCulture),
                converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Placeholders_in_a_string_are_numbered_from_zero_without_gaps()
    {
        // string.Format throws on a "{1}" with no "{0}" argument: catch a broken placeholder here.
        foreach (var path in new[] { Path.Combine(AppSource, "Resources", "Strings.resx"), Path.Combine(RepoRoot(), "Pendulum.Core", "Resources", "CoreStrings.resx") })
            foreach (var data in XDocument.Load(path).Root!.Elements("data"))
            {
                var value = data.Element("value")!.Value;
                var numbers = Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}").Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order().ToList();
                Assert.True(numbers.SequenceEqual(Enumerable.Range(0, numbers.Count)), $"{data.Attribute("name")!.Value}: {value}");
            }
    }

    // --- Dates and times ----------------------------------------------------------------------

    [Fact]
    public void Times_follow_the_12_or_24_hour_setting_and_the_users_culture()
    {
        var at = new DateTime(2026, 8, 25, 14, 30, 0);
        InCulture("en-GB", () =>
        {
            Assert.Equal("14:30", DisplayFormat.Time(at, use24Hour: true));
            Assert.Equal("2:30 pm", DisplayFormat.Time(at, use24Hour: false));
            Assert.Equal("Tue, 25 Aug 2026  14:30", DisplayFormat.DateAndTime(at, use24Hour: true));
            Assert.Equal("Tue, 25 Aug 2026  2:30 pm", DisplayFormat.DateAndTime(at, use24Hour: false));
            Assert.Equal("25 Aug", DisplayFormat.ShortDate(at));
            Assert.Equal("August 2026", DisplayFormat.MonthAndYear(at));
            Assert.Equal("25 August 2026, 14:30", DisplayFormat.LongDateAndTime(at, use24Hour: true));
        });
        InCulture("en-US", () => Assert.Equal("2:30 PM", DisplayFormat.Time(at, use24Hour: false)));
        InCulture("fr-FR", () => Assert.Equal("mar., 25 août 2026  14:30", DisplayFormat.DateAndTime(at, use24Hour: true)));
    }

    [Fact]
    public void The_date_converter_follows_the_setting_it_is_bound_to()
    {
        var converter = new DateTimeDisplayConverter();
        var at = new DateTime(2026, 8, 25, 9, 5, 0);
        InCulture("en-GB", () =>
        {
            Assert.Equal("Tue, 25 Aug 2026  09:05", converter.Convert([at, true], typeof(string), null!, CultureInfo.CurrentCulture));
            Assert.Equal("Tue, 25 Aug 2026  9:05 am", converter.Convert([at, false], typeof(string), null!, CultureInfo.CurrentCulture));
            Assert.Equal("Tue, 25 Aug 2026", converter.Convert(at, typeof(string), "Date", CultureInfo.CurrentCulture));
        });
    }

    // --- Helpers ------------------------------------------------------------------------------

    private static bool IsText(string value) =>
        !value.StartsWith('{') && !value.StartsWith("&#x", StringComparison.Ordinal) && Regex.IsMatch(value, "[A-Za-z]");

    /// Words: a space between letters, or a capitalised word. Not a URL, a path, a file pattern or
    /// a file name. A path is a verbatim string with a backslash, or an escaped backslash in an
    /// ordinary one; escapes such as \n, \t and \" don't make a literal a path. The literal is as the
    /// source spells it, escapes included.
    internal static bool ReadsAsWords(string literal, bool verbatim = false)
    {
        var text = Regex.Replace(literal, @"\{[^}]*\}", "");
        var unescaped = verbatim ? text : Regex.Replace(text, @"\\[nrt""'0abfv]|\\u[0-9A-Fa-f]{4}", " ");
        var isPath = verbatim ? literal.Contains('\\') : literal.Contains(@"\\", StringComparison.Ordinal);
        if (!Regex.IsMatch(unescaped, "[A-Za-z]") || literal.Contains("://", StringComparison.Ordinal) || isPath || literal.StartsWith("*.", StringComparison.Ordinal))
            return false;
        if (Regex.IsMatch(literal, @"^[\w.\-]+\.(wav|mp3|bin|onnx|exe|json|log|md)$"))
            return false;
        return Regex.IsMatch(unescaped, @"[A-Za-z][\s,.:;!?'-]*\s+[A-Za-z]") || Regex.IsMatch(unescaped, "^[A-Z][a-z]+");
    }

    // The literal check's own cases, as the source code spells them (escapes included).
    [Theory]
    [InlineData("Copied to clipboard", false, true)]
    [InlineData(@"Could not read the file.\nPlease try again.", false, true)]
    [InlineData(@"Unexpected error:\n{0}", false, true)]
    [InlineData(@"Delete \""{0}\""?", false, true)]
    [InlineData(@"\tIndented text", false, true)]
    [InlineData("Copied", false, true)]
    [InlineData(@"Software\\Microsoft\\Windows", false, false)]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Run", true, false)]
    [InlineData(@"Data\\crash.log", false, false)]
    [InlineData("https://github.com/Suremise/Pendulum", false, false)]
    [InlineData("*.wav", false, false)]
    [InlineData("chime.wav", false, false)]
    [InlineData(@"\r\n", false, false)]
    [InlineData("HH:mm", false, false)]
    public void The_literal_check_tells_words_from_paths_and_patterns(string literal, bool verbatim, bool words) =>
        Assert.Equal(words, ReadsAsWords(literal, verbatim));

    [Fact]
    public void A_new_reminder_is_named_from_the_resources()
    {
        var draft = Pendulum.App.ViewModels.TriggersViewModel.NewDraft(new AppSettings { DefaultSoundFileName = "bell.wav" });
        Assert.Equal(Strings.Reminders_NewReminderName, draft.Name);
        Assert.Equal("bell.wav", draft.SoundFileName);
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
                inString = !inString;
            if (!inString && line[i] == '/' && line[i + 1] == '/')
                return line[..i];
        }
        return line;
    }

    private static string WithoutXmlComments(string xaml) => Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);

    private static HashSet<string> ResxKeys(string path) =>
        XDocument.Load(path).Root!.Elements("data").Select(d => d.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(AppSource, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Name(string file) => Path.GetRelativePath(AppSource, file);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Pendulum.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Pendulum.sln not found above the test's folder.");
    }

    private static void InCulture(string name, Action work)
    {
        var (culture, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            work();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }
}

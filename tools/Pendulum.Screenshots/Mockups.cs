using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Pendulum.App;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Wpf.Ui.Controls;
using static Pendulum.Screenshots.Program;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using ListView = System.Windows.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using GridView = System.Windows.Controls.GridView;
using Image = System.Windows.Controls.Image;
using Path = System.IO.Path;
using ThemeMode = Pendulum.Core.Models.ThemeMode;

namespace Pendulum.Screenshots;

/// The redesign's mock-ups, for the maintainer's approval before it is built: the app's own views
/// and styles, changed in memory to show the design (standard window controls, a menu bar, a
/// Reminders list that fills the window, row actions on a right-click menu, less rose, Settings in
/// a window of its own, a colour for each reminder), in light and dark side by side. Drawn off
/// screen like the README's pictures; nothing in the app changes.
///
/// Words that only the mock-ups show (the menus, the colour row) are literals here: the build
/// that follows puts each in Strings.resx.
internal static class Mockups
{
    private static bool _dark;

    /// Each reminder's colour, as the design assigns them: in the order the reminders were made,
    /// each the least used so far (ties in <see cref="Palette.AssignOrder"/>).
    private static readonly Dictionary<Guid, Palette.Swatch> ColorOf = [];

    private static readonly (string File, string Title, Func<FrameworkElement> Build)[] Shots =
    [
        ("01-main-narrow.png", "Main window, narrow (820 × 600): title bar with minimise, maximise and close; menu bar; two tabs. When Name would be cramped, Sound and then Mode step aside", () => MainWindowMock(820, 600, tab: 0, MainExtra.None)),
        ("02-main-medium.png", "Main window, medium (1100 × 720): Name stretches, the other columns fit their content, the on/off toggle at the right edge", () => MainWindowMock(1100, 720, tab: 0, MainExtra.None)),
        ("03-main-wide.png", "Main window, wide or maximised (1480 × 880)", () => MainWindowMock(1480, 880, tab: 0, MainExtra.None)),
        ("04-menu-file.png", "The menu bar, File open (View: Reminders, Calendar · Tools: Settings… · Help: Check for updates…, About Pendulum)", () => MainWindowMock(1100, 720, tab: 0, MainExtra.FileMenu)),
        ("05-row-menu.png", "A row: double-click (or Enter) edits; right-click opens this menu; Delete deletes. No Edit or Delete buttons on each row", () => MainWindowMock(1100, 720, tab: 0, MainExtra.RowMenu)),
        ("06-calendar.png", "Calendar: each reminder in its own colour; \"+N more\" as plain text with room below it (a chip fewer when needed); today stays rose", () => MainWindowMock(1100, 760, tab: 1, MainExtra.None)),
        ("07-editor.png", "The reminder's editor: its colour, changeable (a new row under the name)", EditorMock),
        ("08-settings.png", "Tools > Settings…: Settings in a window of its own", SettingsMock),
        ("09-palette.png", "The palette: eight muted colours round the wheel from rose, contrast in each theme, and as colour-blind eyes see them", PaletteSheet),
    ];

    /// Draws each theme in a process of its own (this tool again), then puts each picture's
    /// light and dark side by side.
    public static void Render(string output)
    {
        var parts = Path.Combine(Path.GetTempPath(), "pendulum-mockups-" + Guid.NewGuid().ToString("N")); // not user-visible
        try
        {
            foreach (var theme in new[] { "light", "dark" }) // not user-visible: command-line values
            {
                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                foreach (var arg in new[] { "--mockups-theme", theme, Path.Combine(parts, theme) }) // not user-visible
                    start.ArgumentList.Add(arg);
                using var child = System.Diagnostics.Process.Start(start)!;
                child.WaitForExit();
                if (child.ExitCode != 0)
                    throw new InvalidOperationException($"Drawing the {theme} theme failed."); // not user-visible
            }

            foreach (var shot in Shots)
                Save(SideBySide(shot.Title, Load(Path.Combine(parts, "light", shot.File)), Load(Path.Combine(parts, "dark", shot.File))),
                    Path.Combine(output, shot.File));

            var tokens = Path.Combine(output, "palette-tokens.md");
            File.WriteAllText(tokens, TokensTable());
            Console.WriteLine($"  {Path.GetFileName(tokens)}"); // not user-visible
        }
        finally
        {
            try { Directory.Delete(parts, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static BitmapSource Load(string path)
    {
        using var file = File.OpenRead(path);
        var frame = BitmapFrame.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    /// One theme's pictures, each on its own, into a folder.
    public static void RenderTheme(bool dark, string output)
    {
        // A busy day, so the calendar shows "+N more".
        var busy = DateTime.Today.AddDays(3);
        foreach (var (name, hour, minute) in new[] { ("School run", 8, 15), ("Parcel collection", 13, 0), ("Pick up prescription", 16, 30) })
        {
            var at = busy.AddHours(hour).AddMinutes(minute);
            AppServices.Instance.Triggers.Add(new TriggerTimer
            {
                Name = name,
                TriggerAt = at,
                RecurrenceAnchor = at,
                Mode = AlertMode.SoundOnly,
                SoundFileName = "chime.wav",
            });
        }
        foreach (var reminder in AppServices.Instance.Triggers)
        {
            var used = Palette.AssignOrder.ToDictionary(t => t, t => ColorOf.Values.Count(c => c.Token == t));
            ColorOf[reminder.Id] = Palette.ByToken(Palette.AssignOrder.OrderBy(t => used[t]).ThenBy(t => Array.IndexOf(Palette.AssignOrder, t)).First());
        }

        ApplyTheme(dark);
        foreach (var shot in Shots)
            Save(Draw(shot.Build()), Path.Combine(output, shot.File));
    }

    // --- The theme, with the design's colours ------------------------------------------------------

    private static void ApplyTheme(bool dark)
    {
        _dark = dark;
        AppServices.Instance.Settings.Theme = dark ? ThemeMode.Dark : ThemeMode.Light;
        AppThemeManager.Apply(dark ? ThemeMode.Dark : ThemeMode.Light);

        // Less rose: the list's badges take the palette (rose is kept for the primary button,
        // selection, today and focus). Upcoming teal; the alert types blue, ochre and plum.
        SetTint(AppThemeManager.ChipBrushKey, AppThemeManager.ChipStrokeBrushKey, "teal");
        SetTint(AppThemeManager.BadgeSoundOnlyBrushKey, null, "blue");
        SetTint(AppThemeManager.BadgeSoundAndSpeechBrushKey, null, "ochre");
        SetTint(AppThemeManager.BadgeSpeechOnlyBrushKey, null, "plum");
    }

    private static void SetTint(string key, string? strokeKey, string token)
    {
        var shade = Palette.ByToken(token).Shade(_dark);
        Application.Current.Resources[key] = Frozen(Palette.WithAlpha(shade, Palette.TintAlpha(_dark)));
        if (strokeKey is not null)
            Application.Current.Resources[strokeKey] = Frozen(shade);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// A reminder's colour in the current theme: its shade (a dot, a chip's stripe).
    public static Brush ShadeOf(TriggerTimer reminder) =>
        Frozen(ColorOf.TryGetValue(reminder.Id, out var swatch) ? swatch.Shade(_dark) : Colors.Gray);

    // --- The main window ---------------------------------------------------------------------------

    private enum MainExtra { None, FileMenu, RowMenu }

    private static FrameworkElement MainWindowMock(double width, double height, int tab, MainExtra extra)
    {
        var window = new MainWindow();
        var tabs = (TabControl)window.FindName("MainTabControl");
        var root = (Grid)window.Content;
        window.Content = null;

        // Standard window controls: minimise, maximise, close.
        root.Children.OfType<TitleBar>().First().ShowMaximize = true;

        // A menu bar under the title bar: the window's own, now that it has one.
        MenuItem file;
        if (window.FindName("MainMenu") is Menu real)
            file = real.Items.OfType<MenuItem>().First();
        else
        {
            root.RowDefinitions.Insert(1, new RowDefinition { Height = GridLength.Auto });
            foreach (UIElement child in root.Children)
                if (Grid.GetRow(child) >= 1)
                    Grid.SetRow(child, Grid.GetRow(child) + 1);
            (var menu, file) = MenuBar();
            Grid.SetRow(menu, 1);
            root.Children.Add(menu);
        }

        // Two tabs: Settings moves to Tools > Settings… (the window has only two now).
        if (tabs.Items.Count > 2)
            tabs.Items.RemoveAt(2);
        tabs.SelectedIndex = tab;
        tabs.Margin = new Thickness(28, 6, 28, 20);

        // About moves to Help > About Pendulum.
        var status = root.Children.OfType<Border>().Last();
        foreach (var button in ((Grid)status.Child).Children.OfType<Button>())
            button.Visibility = Visibility.Collapsed;

        var overlay = new Canvas();
        var stage = new Grid();
        stage.Children.Add(root);
        stage.Children.Add(overlay);
        window.Content = stage;
        var host = Detach(window, width, height);
        Inherit(host);

        Layout(host);
        foreach (var calendar in Descendants(host).OfType<CalendarPanel>())
            (calendar.DataContext as CalendarViewModel)?.RefreshForDisplay();
        Layout(host);

        // The panel's rose frame goes: the theme's own quiet edge instead.
        foreach (var frame in Descendants(tabs).OfType<Border>().Where(b => b.BorderThickness == new Thickness(1.5)))
        {
            frame.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush"); // not user-visible: resource keys
            frame.BorderThickness = new Thickness(1);
        }

        var panel = Descendants(host).OfType<TriggersPanel>().FirstOrDefault();
        if (panel is not null)
            ReminderList(panel, host);
        foreach (var calendar in Descendants(host).OfType<CalendarPanel>())
            Calendar(calendar);
        Layout(host);

        if (extra == MainExtra.FileMenu)
        {
            Highlight(file);
            var drop = DropDown(
                ("New reminder", "Ctrl+N", SymbolRegular.Add24),
                null,
                ("Import…", null, SymbolRegular.ArrowDownload24),
                ("Export…", null, SymbolRegular.ArrowUpload24),
                null,
                ("Exit", null, null));
            Place(overlay, drop, file.TranslatePoint(new Point(-4, file.ActualHeight - 2), overlay));
        }
        else if (extra == MainExtra.RowMenu && panel is not null)
        {
            var list = (ListView)panel.FindName("RemindersList");
            var reminder = AppServices.Instance.Triggers.First(t => t.Name == "Dentist");
            list.SelectedItem = reminder;
            Layout(host);
            var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(reminder);
            var drop = DropDown(
                ("Edit…", "Enter", SymbolRegular.Edit24),
                ("Turn off", null, SymbolRegular.Alert24),
                null,
                ("Delete", "Del", SymbolRegular.Delete24));
            Place(overlay, drop, row.TranslatePoint(new Point(150, row.ActualHeight * 0.65), overlay));
        }

        Layout(host);
        return Framed(host);
    }

    private static (Menu Menu, MenuItem File) MenuBar()
    {
        var menu = new Menu { Margin = new Thickness(10, 0, 10, 0), Background = Brushes.Transparent };
        MenuItem Top(string header)
        {
            var item = new MenuItem { Header = header };
            menu.Items.Add(item);
            return item;
        }
        var file = Top("_File");
        Top("_View");
        Top("_Tools");
        Top("_Help");
        return (menu, file);
    }

    /// A menu as it shows when open: a context menu's look, drawn in place (an open menu is a
    /// popup window, which would be shown).
    private static ContextMenu DropDown(params (string Header, string? Keys, SymbolRegular? Icon)?[] items)
    {
        var menu = new ContextMenu();
        foreach (var item in items)
        {
            if (item is not { } i)
            {
                menu.Items.Add(new Separator());
                continue;
            }
            menu.Items.Add(new MenuItem
            {
                Header = i.Header,
                InputGestureText = i.Keys ?? "",
                Icon = i.Icon is { } symbol ? new SymbolIcon { Symbol = symbol } : null,
            });
        }
        return menu;
    }

    /// A context menu can't sit in another element (WPF refuses), so it's laid out on its own,
    /// drawn, and its picture placed. Its first item is drawn as if under the pointer.
    private static void Place(Canvas overlay, ContextMenu menu, Point at)
    {
        TextOptions.SetTextFormattingMode(menu, TextFormattingMode.Ideal);
        for (var pass = 0; pass < 3; pass++)
        {
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (pass == 0)
                Highlight((MenuItem)menu.Items[0]);
        }
        var picture = new Image { Source = Draw(menu), Stretch = Stretch.None };
        Canvas.SetLeft(picture, at.X);
        Canvas.SetTop(picture, at.Y);
        overlay.Children.Add(picture);
    }

    /// The look of an item under the pointer, without a pointer.
    private static void Highlight(MenuItem item)
    {
        var key = (DependencyPropertyKey)typeof(MenuItem)
            .GetField("IsHighlightedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!; // not user-visible
        item.SetValue(key, true);
    }

    // --- The Reminders list ------------------------------------------------------------------------

    private static void ReminderList(TriggersPanel panel, FrameworkElement host)
    {
        var list = (ListView)panel.FindName("RemindersList");
        var columns = ((GridView)list.View).Columns;

        // The name with the reminder's colour beside it, as on the calendar.
        columns[1].CellTemplate = Template("""
            <StackPanel Orientation="Horizontal" Margin="10,0,0,0">
                <Ellipse Width="10" Height="10" Margin="0,0,9,0" VerticalAlignment="Center"
                         Fill="{Binding Converter={x:Static mock:ShadeConverter.Instance}}"/>
                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" VerticalAlignment="Center"
                           TextTrimming="CharacterEllipsis" Style="{DynamicResource NameText}"/>
            </StackPanel>
            """);

        // The row actions: only the on/off toggle, at the right edge.
        columns[^1].CellTemplate = Template("""
            <Button Style="{DynamicResource EnableToggleButton}" HorizontalAlignment="Right" Margin="0,0,4,0"/>
            """);

        // Every column but the name fits its widest content (its header or any cell); the name
        // takes the rest.
        Layout(host);
        var rows = Descendants(list).OfType<System.Windows.Controls.GridViewRowPresenter>().ToList();
        var headers = Descendants(list).OfType<GridViewColumnHeader>().Where(h => h.Column is not null).ToList();
        for (var i = 2; i < columns.Count; i++)
        {
            var widest = 0.0;
            foreach (var cell in rows.Select(r => VisualTreeHelper.GetChild(r, i)).OfType<UIElement>()
                         .Concat(headers.Where(h => h.Column == columns[i])))
            {
                cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                widest = Math.Max(widest, cell.DesiredSize.Width);
            }
            columns[i].Width = Math.Ceiling(widest) + 14;
        }
        Layout(host);
        // Too narrow for the name to keep 220: Sound steps aside, then Mode.
        var room = list.ActualWidth - 26;
        double Others() => columns.Where((_, i) => i != 1).Sum(c => double.IsNaN(c.Width) ? c.ActualWidth : c.Width);
        foreach (var index in new[] { 5, 4 })
            if (room - Others() < 220)
                columns[index].Width = 0;
        columns[1].Width = Math.Max(140, room - Others());

        // Less rose: the sort arrows, the Scheduled outline and the filters' dot in the theme's
        // own greys. (The primary button keeps it.)
        Layout(host);
        var accent = Application.Current.Resources[AppThemeManager.AccentBrushKey];
        foreach (var element in Descendants(panel))
        {
            if (element is TextBlock text && ReferenceEquals(text.Foreground, accent))
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush"); // not user-visible
            else if (element is Border border && ReferenceEquals(border.BorderBrush, accent))
                border.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush"); // not user-visible
            else if (element is Border dot && ReferenceEquals(dot.Background, accent))
                dot.SetResourceReference(Border.BackgroundProperty, "TextFillColorSecondaryBrush"); // not user-visible
        }
    }

    private static DataTemplate Template(string body) => (DataTemplate)XamlReader.Parse($"""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      xmlns:mock="clr-namespace:Pendulum.Screenshots;assembly=Pendulum.Screenshots">
            {body}
        </DataTemplate>
        """);

    // --- The calendar ------------------------------------------------------------------------------

    private static void Calendar(CalendarPanel calendar)
    {
        var chip = calendar.Resources["ReminderPillBadge"];
        var more = calendar.Resources["OverflowChip"];
        var plain = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate TargetType="ToggleButton" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ContentPresenter Margin="{TemplateBinding Padding}"/>
            </ControlTemplate>
            """);
        foreach (var element in Descendants(calendar))
        {
            // A chip in the reminder's colour: a tint of it, with a bar of it at the start.
            if (element is Border border && ReferenceEquals(border.Style, chip) && border.DataContext is TriggerTimer { HasFired: false } reminder)
            {
                var swatch = ColorOf[reminder.Id];
                border.Background = Frozen(Palette.WithAlpha(swatch.Shade(_dark), Palette.TintAlpha(_dark)));
                border.BorderBrush = Frozen(swatch.Shade(_dark));
                border.BorderThickness = new Thickness(3, 0, 0, 0);
                border.CornerRadius = new CornerRadius(3);
                border.Padding = new Thickness(5, 1, 5, 2);
            }
            // "+N more" as plain text, with room below it.
            else if (element is ToggleButton toggle && ReferenceEquals(toggle.Style, more))
            {
                toggle.Template = plain;
                toggle.Padding = new Thickness(6, 1, 0, 0);
                toggle.Margin = new Thickness(0, 0, 0, 6);
                toggle.SetResourceReference(Control.ForegroundProperty, "TextFillColorSecondaryBrush"); // not user-visible

                // A day with more than fits keeps a line for "+N more": one chip fewer, so it's
                // never cut off at the cell's foot.
                if (toggle.Visibility == Visibility.Visible && toggle.DataContext is CalendarDayCell day
                    && VisualTreeHelper.GetParent(toggle) is Panel stack && stack.Children[0] is ItemsControl chips)
                {
                    var shown = chips.Items.Count - 1;
                    if (chips.ItemContainerGenerator.ContainerFromIndex(shown) is UIElement last)
                        last.Visibility = Visibility.Collapsed;
                    ((TextBlock)toggle.Content).Text = $"+{day.Reminders.Count - shown} more";
                }
            }
        }
    }

    // --- The reminder's editor ---------------------------------------------------------------------

    private static FrameworkElement EditorMock()
    {
        var reminder = AppServices.Instance.Triggers.First(t => t.Name == "Dentist");
        var window = new TriggerEditWindow(reminder, isNew: false);
        var root = (Grid)window.Content;
        var form = root.Children.OfType<StackPanel>().First();

        var row = new StackPanel { Margin = new Thickness(0, -6, 0, 18) };
        row.Children.Add(new TextBlock { Text = "Colour", FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
        row.Children[0].SetValue(TextBlock.ForegroundProperty, Application.Current.Resources["TextFillColorSecondaryBrush"]);
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        var mine = ColorOf[reminder.Id];
        foreach (var swatch in Palette.Colors)
        {
            var dot = new Ellipse { Width = 22, Height = 22, Fill = Frozen(swatch.Shade(_dark)) };
            var ring = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = swatch == mine ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] : Brushes.Transparent,
                Child = dot,
            };
            swatches.Children.Add(ring);
        }
        var name = new TextBlock { Text = mine.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 13 };
        swatches.Children.Add(name);
        row.Children.Add(swatches);
        form.Children.Insert(1, row);

        var host = Detach(window, window.Width, double.NaN);
        Inherit(host);
        Layout(host);
        return Framed(host);
    }

    // --- The Settings window -----------------------------------------------------------------------

    private static FrameworkElement SettingsMock()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(new TitleBar { Title = "Settings", ShowMinimize = false });
        var settings = new SettingsPanel { Margin = new Thickness(28, 4, 28, 20) };
        Grid.SetRow(settings, 1);
        grid.Children.Add(settings);

        var window = new Window { Content = grid, Width = 980, Height = 700 };
        var host = Detach(window, window.Width, window.Height);
        Inherit(host);
        Layout(host);
        return Framed(host);
    }

    // --- The palette sheet -------------------------------------------------------------------------

    private static FrameworkElement PaletteSheet()
    {
        var surface = Palette.Surface(_dark);
        var text = Palette.Text(_dark);
        var secondary = Frozen(Palette.Over(text, 0xA0, surface));
        var sheet = new StackPanel { Margin = new Thickness(28, 24, 28, 28) };
        TextBlock Line(string s, double size = 12, FontWeight? weight = null, Brush? brush = null, double below = 6) => new()
        {
            Text = s,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush ?? Frozen(text),
            Margin = new Thickness(0, 0, 0, below),
            TextWrapping = TextWrapping.Wrap,
        };

        sheet.Children.Add(Line(_dark ? "Dark theme" : "Light theme", 18, FontWeights.SemiBold, below: 2));
        sheet.Children.Add(Line($"On the panels' surface {Palette.Hex(surface)}. Shade: the solid colour (a dot, a chip's bar), 3:1 or more on the surface. " +
            $"Tint: the shade at {Palette.TintAlpha(_dark) * 100 / 255}% (a chip, a badge), with the theme's text on it, 4.5:1 or more.", 12, brush: secondary, below: 16));

        var table = new Grid();
        double[] widths = [36, 112, 84, 92, 180, 96];
        foreach (var w in widths)
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        void Cell(UIElement e, int row, int col)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            table.Children.Add(e);
        }
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] heads = ["", "Colour", "Shade", "on surface", "Chip", "text on tint"];
        for (var c = 0; c < heads.Length; c++)
            Cell(Line(heads[c], 11, FontWeights.SemiBold, secondary), 0, c);
        table.Margin = new Thickness(0, 0, 0, 22);
        for (var i = 0; i < Palette.Colors.Length; i++)
        {
            var swatch = Palette.Colors[i];
            var shade = swatch.Shade(_dark);
            var tint = Palette.Tint(swatch, _dark);
            var r = i + 1;
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            Cell(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Frozen(shade), HorizontalAlignment = HorizontalAlignment.Left }, r, 0);
            Cell(Center(Line($"{swatch.Name}", 13, FontWeights.SemiBold, below: 0)), r, 1);
            Cell(Center(Line(Palette.Hex(shade), 12, below: 0)), r, 2);
            Cell(Center(Line($"{Palette.Contrast(shade, surface):0.00}:1", 12, below: 0)), r, 3);
            Cell(Chip("Dentist 10:15", shade, tint, text), r, 4);
            Cell(Center(Line($"{Palette.Contrast(text, tint):0.00}:1", 12, below: 0)), r, 5);
        }
        sheet.Children.Add(table);

        sheet.Children.Add(Line("As colour-blind eyes see them", 15, FontWeights.SemiBold, below: 2));
        sheet.Children.Add(Line("Simulated (Machado et al. 2009, full strength). The closest pair's CIEDE2000 difference: about 10 or more reads as clearly different side by side.",
            12, brush: secondary, below: 10));
        var shades = Palette.Colors.Select(c => c.Shade(_dark)).ToArray();
        foreach (var vision in Enum.GetValues<Palette.Vision>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(Center(Line(vision.ToString(), 12, FontWeights.SemiBold, below: 0), 104));
            foreach (var swatch in Palette.Colors)
            {
                var shade = Palette.Simulate(swatch.Shade(_dark), vision);
                var tint = Palette.Simulate(Palette.Tint(swatch, _dark), vision);
                var chip = Chip(swatch.Name, shade, tint, text);
                chip.Width = 84;
                chip.Margin = new Thickness(0, 0, 6, 0);
                row.Children.Add(chip);
            }
            var (delta, a, b) = Palette.Closest(shades, vision);
            row.Children.Add(Center(Line($"closest: {Palette.Colors[a].Name} and {Palette.Colors[b].Name}, ΔE {delta:0.0}", 12, below: 0), 290, left: 10));
            sheet.Children.Add(row);
        }

        sheet.Children.Add(Line("Categories in the Reminders list", 15, FontWeights.SemiBold, below: 8));
        ((FrameworkElement)sheet.Children[^1]).Margin = new Thickness(0, 14, 0, 8);
        var badges = new WrapPanel();
        foreach (var (label, token) in new[] { ("Upcoming", "teal"), ("Sound only", "blue"), ("Sound and speech", "ochre"), ("Speech only", "plum") })
        {
            var swatch = Palette.ByToken(token);
            badges.Children.Add(new Border
            {
                Background = Frozen(Palette.Tint(swatch, _dark)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(0, 0, 8, 0),
                Child = Line($"{label} ({swatch.Name.ToLowerInvariant()})", 11, FontWeights.SemiBold, below: 0),
            });
        }
        sheet.Children.Add(badges);
        sheet.Children.Add(Line("Fixed, Scheduled, Disabled and Spent stay neutral greys; rose is kept for the primary button, selection, today and focus.",
            12, brush: secondary, below: 0));

        var page = new Border { Background = Frozen(surface), Width = 1160, Child = sheet, CornerRadius = new CornerRadius(8) };
        TextOptions.SetTextFormattingMode(page, TextFormattingMode.Ideal);
        Layout(page);
        return page;
    }

    private static FrameworkElement Center(TextBlock text, double width = double.NaN, double left = 0)
    {
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Width = width;
        text.Margin = new Thickness(left, 0, 0, 0);
        return text;
    }

    private static Border Chip(string label, Color shade, Color tint, Color text) => new()
    {
        Background = Frozen(tint),
        BorderBrush = Frozen(shade),
        BorderThickness = new Thickness(3, 0, 0, 0),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(6, 2, 6, 3),
        Width = 164,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Frozen(text), TextTrimming = TextTrimming.CharacterEllipsis },
    };

    /// The text colour a window gives what's in it (its theme's), which content moved out of the
    /// window would otherwise lose: the menu's words and the title bar's buttons inherit it.
    private static void Inherit(FrameworkElement host)
    {
        host.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush"); // not user-visible
        foreach (var bar in Descendants(host).OfType<TitleBar>().Concat(LogicalTitleBars(host)))
            bar.SetResourceReference(TitleBar.ButtonsForegroundProperty, "TextFillColorPrimaryBrush"); // not user-visible
    }

    private static IEnumerable<TitleBar> LogicalTitleBars(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()
            .SelectMany(c => (c is TitleBar t ? [t] : Enumerable.Empty<TitleBar>()).Concat(LogicalTitleBars(c)));

    // --- Framing and sheets ------------------------------------------------------------------------

    /// A window as it sits on the desktop: rounded corners, a hairline edge and a soft shadow.
    private static FrameworkElement Framed(Border host)
    {
        // The title bar's buttons take their glyphs' colour as they load, before the content is
        // in a theme: given the theme's text colour here.
        foreach (var button in Descendants(host).OfType<TitleBarButton>())
            button.SetValue(TitleBarButton.RenderButtonsForegroundProperty, Application.Current.Resources["TextFillColorPrimaryBrush"]); // not user-visible
        Layout(host);
        var (width, height) = (host.ActualWidth, host.ActualHeight);
        host.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 8, 8);
        var shadow = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["ApplicationBackgroundBrush"], // not user-visible
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = _dark ? 0.6 : 0.25 },
        };
        var edge = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Frozen(_dark ? Palette.Parse("#FF454545") : Palette.Parse("#33000000")),
        };
        var stack = new Grid { Margin = new Thickness(28, 20, 28, 36) };
        stack.Children.Add(shadow);
        stack.Children.Add(host);
        stack.Children.Add(edge);
        var frame = new Border { Child = stack, Background = Brushes.Transparent, UseLayoutRounding = true, Width = width + 56 };
        TextOptions.SetTextFormattingMode(frame, TextFormattingMode.Ideal);
        Layout(frame);
        return frame;
    }

    /// The light and dark renderings side by side, each on a desktop of its theme, under a title.
    private static BitmapSource SideBySide(string title, BitmapSource light, BitmapSource dark)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(28, 16, 28, 4),
            TextWrapping = TextWrapping.Wrap,
        };
        var head = new Border { Background = Frozen(Palette.Parse("#F3F3F3")), Child = caption };
        Grid.SetColumnSpan(head, 2);
        grid.Children.Add(head);
        foreach (var (bitmap, col, back, label) in new[] { (light, 0, "#E4E6EA", "Light"), (dark, 1, "#141417", "Dark") })
        {
            var side = new StackPanel { Background = Frozen(Palette.Parse(back)) };
            side.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(28, 12, 0, 0),
                Foreground = col == 0 ? Brushes.Black : Brushes.White,
            });
            side.Children.Add(new Image { Source = bitmap, Stretch = Stretch.None, Margin = new Thickness(0, 4, 0, 0) });
            if (bitmap.PixelWidth < 900)
                ((Image)side.Children[1]).Margin = new Thickness(28, 12, 28, 28);
            Grid.SetRow(side, 1);
            Grid.SetColumn(side, col);
            grid.Children.Add(side);
        }
        var page = new Border { Child = grid, UseLayoutRounding = true };
        TextOptions.SetTextFormattingMode(page, TextFormattingMode.Ideal);
        page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        page.Width = page.DesiredSize.Width;
        Layout(page);
        return Draw(page);
    }

    // --- The tokens, for the docs ------------------------------------------------------------------

    private static string TokensTable()
    {
        var md = new StringBuilder();
        md.AppendLine("| Token | Light | Dark | Light: on surface / text on tint | Dark: on surface / text on tint |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var swatch in Palette.Colors)
        {
            string Figures(bool dark) => string.Create(CultureInfo.InvariantCulture,
                $"{Palette.Contrast(swatch.Shade(dark), Palette.Surface(dark)):0.00} / {Palette.Contrast(Palette.Text(dark), Palette.Tint(swatch, dark)):0.00}");
            md.AppendLine($"| `--palette-{swatch.Token}` | `{swatch.Light}` | `{swatch.Dark}` | {Figures(false)} | {Figures(true)} |");
        }
        md.AppendLine();
        md.AppendLine("| Vision | Light: closest pair, ΔE2000 | Dark: closest pair, ΔE2000 |");
        md.AppendLine("|---|---|---|");
        foreach (var vision in Enum.GetValues<Palette.Vision>())
        {
            string Pair(bool dark)
            {
                var (d, a, b) = Palette.Closest(Palette.Colors.Select(c => c.Shade(dark)).ToArray(), vision);
                return string.Create(CultureInfo.InvariantCulture, $"{Palette.Colors[a].Name} and {Palette.Colors[b].Name}, {d:0.0}");
            }
            md.AppendLine($"| {vision} | {Pair(false)} | {Pair(true)} |");
        }
        return md.ToString();
    }
}

/// A reminder's colour for a binding in the mock-ups' templates.
public sealed class ShadeConverter : IValueConverter
{
    public static ShadeConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TriggerTimer reminder ? Mockups.ShadeOf(reminder) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

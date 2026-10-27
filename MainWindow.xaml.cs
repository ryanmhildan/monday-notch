using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using MondayNotch.Services;

namespace MondayNotch;

public partial class MainWindow : Window
{
    private const double CollapsedWidth = 230, CollapsedHeight = 34;
    private const double CompactWidth = 120, CompactHeight = 6;
    private const double GreetingHeight = 44, GreetingLogoOnlyWidth = 110;
    private const double CollapsedLyricMaxWidth = 560;
    private const double CollapsedLyricChrome = 24 + 22 + 10 + 8; // side margins, artwork, gap, breathing room
    private const double ExpandedWidth = 424;  // 340 of cards plus the page arrows
    private const double LyricsCardWidth = 228; // card plus its left gap
    private const double MusicCardShrinkWithLyrics = 90; // music card goes from 340 to 250 next to lyrics
    private const double ExpandedSidePadding = 28;
    private const double ExpandedVerticalPadding = 24; // 10 above the header, 14 below the page dots
    private static readonly TimeSpan HoverCloseDelay = TimeSpan.FromMilliseconds(350);
    private static readonly CultureInfo English = new("en-US");

    // Light a line up slightly early; it reads better than lighting it up late.
    private static readonly TimeSpan LyricLead = TimeSpan.FromMilliseconds(200);
    private const double LyricLineGap = 6;
    private const double InactiveLyricOpacity = 0.38;

    private static readonly Brush FocusBrush = Frozen(Color.FromRgb(0xFF, 0x9F, 0x45));
    private static readonly Brush BreakBrush = Frozen(Color.FromRgb(0x4A, 0xDE, 0x80));

    // Process names (without .exe) that count as "a browser" for auto-compact.
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc", "zen", "librewolf", "waterfox", "chromium",
    };

    private readonly MediaService _media = new();
    private readonly PomodoroTimer _pomodoro = new();
    private readonly LyricsService _lyricsService = new();
    private readonly TodoStore _todos = new();
    private readonly IdleInfo _idle = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = HoverCloseDelay };
    private readonly DispatcherTimer _lyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Dictionary<UIElement, double> _fadeTargets = new();
    private readonly bool _showGreeting;
    private readonly bool _followMedia;

    private enum NotchPage { ControlCenter, Music, Focus, Todo, Note, Settings }

    private bool _expanded;
    private bool _compact;
    private bool _greeting;
    private double _greetingWidth = GreetingLogoOnlyWidth;
    private NotchPage _page = NotchPage.ControlCenter;
    private double _shapeWidth = CollapsedWidth, _shapeHeight = CollapsedHeight;
    private bool _shapeExpanded;
    private NotchShape _shape = AppSettings.Shape;
    private bool _autoCompact = AppSettings.AutoCompact;
    private int _foregroundPid;
    private bool _foregroundIsBrowser;

    private byte[]? _shownThumbnail;
    private (Color Start, Color End)? _artColors;
    private (Color Start, Color End)? _notchColors;

    private bool _hasBattery, _charging;
    private double _batteryLevel;

    private bool _lyricsEnabled = AppSettings.LyricsEnabled;
    private string _lyricsKey = "";
    private bool _lyricsLoading;
    private Lyrics? _lyrics;
    private CancellationTokenSource? _lyricsCancellation;
    private int _currentLine = -1;
    private double _collapsedLyricWidth;

    /// <param name="followMedia">False leaves the music card empty (used for screenshots without anyone's music in them).</param>
    public MainWindow(bool showGreeting, bool followMedia = true)
    {
        _showGreeting = showGreeting && AppSettings.GreetingEnabled;
        _followMedia = followMedia;
        InitializeComponent();

        _clock.Tick += (_, _) => Tick();
        _lyricsTimer.Tick += (_, _) => UpdateLyricsPosition();
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            // Stay open while something is being typed, even if the pointer wanders off.
            if (!Notch.IsMouseOver && !IsTyping)
                Collapse();
        };
        Deactivated += (_, _) => OnDeactivated();

        _todos.Changed += UpdateTodoSummary;
        _todos.Load();
        TodoList.ItemsSource = _todos.Items;

        ControlCenterPage.CollapseRequested += () => Collapse();
        SettingsPage.Changed += ApplySettings;

        // Media and idle-info events arrive on background threads.
        _media.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateMedia));
        _idle.Changed += () => Dispatcher.BeginInvoke(new Action(RenderIdleSlots));
        _pomodoro.Changed += UpdatePomodoro;
        _pomodoro.Finished += OnPomodoroFinished;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        BuildPageDots();

        Loaded += async (_, _) =>
        {
            ApplySettings();
            ApplyLyricsMode();
            Tick();
            UpdateMedia();
            UpdatePomodoro();
            _clock.Start();
            if (_showGreeting)
                _ = RunGreetingAsync();
            if (_followMedia)
                await _media.InitializeAsync();
        };
    }

    public void DisposeServices()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _clock.Stop();
        _lyricsTimer.Stop();
        _lyricsCancellation?.Cancel();
        _pomodoro.Pause();
        _media.Dispose();
        NotePage.Flush();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Keep the notch out of Alt+Tab and never steal focus from the app being used.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    /// <summary>Opens the notch on the Settings page for a while (tray menu).</summary>
    public void OpenSettings()
    {
        Peek(TimeSpan.FromSeconds(8), NotchPage.Settings);
    }

    // ---- Settings ----------------------------------------------------------------

    /// <summary>Re-reads every preference and applies it. Runs on start and whenever Settings changes something.</summary>
    private void ApplySettings()
    {
        ThemeManager.Apply(AppSettings.Theme);
        _shape = AppSettings.Shape;
        _autoCompact = AppSettings.AutoCompact;
        if (!_autoCompact)
            _compact = false;

        // Album-art colours depend on the theme (dark tones under white text, light tones under dark text).
        _artColors = ArtworkPalette.FromImage(_shownThumbnail, ThemeManager.IsLight);
        _notchColors = null; // force the notch colour to be re-applied
        _shapeWidth = -1;    // and the shape (corner radii differ per shape)

        PositionWindow();
        RenderIdleSlots();
        UpdateShape();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Windows switched between light and dark app mode.
        if (e.Category == UserPreferenceCategory.General && AppSettings.Theme == ThemeMode.Auto)
            Dispatcher.BeginInvoke(new Action(ApplySettings));
    }

    // ---- Window and keyboard -------------------------------------------------------

    /// <summary>
    /// The notch normally never takes focus. Typing (a task, the note, a name) needs the keyboard, so it is
    /// allowed to activate for that and goes back to never-activate once the user moves on.
    /// </summary>
    private void SetNoActivate(bool noActivate)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, noActivate ? exStyle | WS_EX_NOACTIVATE : exStyle & ~WS_EX_NOACTIVATE);
    }

    private static bool IsTyping => Keyboard.FocusedElement is TextBox;

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TextBox? box = FindAncestor<TextBox>(e.OriginalSource as DependencyObject);
        if (box is null || box.IsKeyboardFocused)
            return;
        SetNoActivate(false);
        Activate();
        box.Focus();
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    private void OnDeactivated()
    {
        SetNoActivate(true);
        if (IsTyping)
            Keyboard.ClearFocus();
        if (!Notch.IsMouseOver)
        {
            _collapseTimer.Interval = HoverCloseDelay;
            _collapseTimer.Start();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(PositionWindow));

    private void PositionWindow()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = _shape == NotchShape.Floating ? AppSettings.FloatingDistance : 0;
    }

    // ---- Open / close ------------------------------------------------------------

    private void Notch_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        Expand();
    }

    private void Notch_MouseLeave(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        _collapseTimer.Interval = HoverCloseDelay;
        _collapseTimer.Start();
    }

    /// <summary>Opens the notch for a moment, e.g. when a timer finishes.</summary>
    private void Peek(TimeSpan duration, NotchPage page)
    {
        Expand(page);
        _collapseTimer.Stop();
        _collapseTimer.Interval = duration;
        _collapseTimer.Start();
    }

    /// <param name="page">Page to open on; by default Music while something plays and Control Center otherwise.</param>
    private void Expand(NotchPage? page = null)
    {
        if (_greeting)
            EndGreeting();
        NotchPage target = page ?? (_media.IsPlaying ? NotchPage.Music : NotchPage.ControlCenter);
        if (_expanded)
        {
            if (page is not null)
                ShowPage(target, direction: 1);
            return;
        }
        _expanded = true;

        NotchPage before = _page;
        ShowPage(target, direction: 0);
        // ShowPage only refreshes a page it switches to; reopening on the same page needs fresh switch states too.
        if (before == target && target == NotchPage.ControlCenter)
            ControlCenterPage.Refresh();
        UpdateMedia();
        UpdatePomodoro();
        UpdateShape();
        ExpandedView.IsHitTestVisible = true;
    }

    private void Collapse()
    {
        if (!_expanded)
            return;
        _expanded = false;
        if (IsTyping)
            Keyboard.ClearFocus();

        UpdateShape();
        ExpandedView.IsHitTestVisible = false;
    }

    private bool ShowCollapsedLyric => _lyricsEnabled && _lyrics is { IsSynced: true } && _media.IsPlaying && !_compact;

    /// <summary>The lyrics card only widens the notch on the music page, and the music card narrows beside it.</summary>
    private double ExpandedContentWidth =>
        ExpandedWidth - ExpandedSidePadding +
        (_page == NotchPage.Music && _lyricsEnabled ? LyricsCardWidth - MusicCardShrinkWithLyrics : 0);

    private static double ExpandedHeightFor(NotchPage page) => page switch
    {
        NotchPage.ControlCenter or NotchPage.Note => 240,
        NotchPage.Settings => 312,
        _ => 196,
    };

    // ---- Pages -------------------------------------------------------------------

    private void PrevPageBtn_Click(object sender, RoutedEventArgs e) => ShowPage(StepPage(-1), direction: -1);
    private void NextPageBtn_Click(object sender, RoutedEventArgs e) => ShowPage(StepPage(1), direction: 1);

    /// <summary>The arrows wrap around: right from the last page goes back to the first.</summary>
    private NotchPage StepPage(int step)
    {
        NotchPage[] pages = Enum.GetValues<NotchPage>();
        return pages[(Array.IndexOf(pages, _page) + step + pages.Length) % pages.Length];
    }

    private FrameworkElement PageElement(NotchPage page) => page switch
    {
        NotchPage.ControlCenter => ControlCenterPage,
        NotchPage.Music => MusicPage,
        NotchPage.Focus => FocusPage,
        NotchPage.Todo => TodoPage,
        NotchPage.Note => NotePage,
        _ => SettingsPage,
    };

    private static string PageName(NotchPage page) => page switch
    {
        NotchPage.ControlCenter => "Control Center",
        NotchPage.Todo => "To-do",
        NotchPage.Note => "Quick note",
        _ => page.ToString(),
    };

    private void BuildPageDots()
    {
        foreach (NotchPage page in Enum.GetValues<NotchPage>())
        {
            var dot = new Ellipse { Width = 5, Height = 5, Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand, ToolTip = PageName(page) };
            // A bigger transparent target around the 5px dot.
            var target = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 4, 0, 4), Child = dot, Tag = page };
            target.MouseLeftButtonUp += (_, _) => ShowPage(page, direction: page > _page ? 1 : -1);
            PageDots.Children.Add(target);
        }
        UpdatePageDots();
    }

    private void UpdatePageDots()
    {
        foreach (Border target in PageDots.Children)
            ((Ellipse)target.Child).SetResourceReference(Shape.FillProperty, (NotchPage)target.Tag == _page ? "TextBrush" : "TrackBrush");
    }

    /// <param name="direction">1 slides the new page in from the right, -1 from the left, 0 shows it without sliding.</param>
    private void ShowPage(NotchPage page, int direction)
    {
        if (page == _page && PageElement(page).IsVisible)
            return;
        _page = page;

        foreach (NotchPage each in Enum.GetValues<NotchPage>())
            PageElement(each).Visibility = each == page ? Visibility.Visible : Visibility.Collapsed;
        FrameworkElement incoming = PageElement(page);
        LyricsBtn.Visibility = page == NotchPage.Music ? Visibility.Visible : Visibility.Collapsed;
        TodoCount.Visibility = page == NotchPage.Todo ? Visibility.Visible : Visibility.Collapsed;
        UpdatePageDots();

        ExpandedView.Width = ExpandedContentWidth;
        ExpandedView.Height = ExpandedHeightFor(page) - ExpandedVerticalPadding;
        UpdateLayout();

        // Progress bars and the lyric scroll position depend on layout that only exists while a page is visible.
        UpdateMediaProgress();
        UpdatePomodoro();
        UpdateLyricsPosition(force: true);
        if (page == NotchPage.ControlCenter)
            ControlCenterPage.Refresh();
        if (page == NotchPage.Settings)
            SettingsPage.UpdatePreview(BuildIdleSnapshot());
        UpdateShape();

        if (direction == 0)
            return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        incoming.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(24 * direction, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    // ---- Shape -------------------------------------------------------------------

    private (double Width, double Height) TargetSize()
    {
        if (_expanded)
            return (ExpandedContentWidth + ExpandedSidePadding, ExpandedHeightFor(_page));
        if (_greeting)
            return (_greetingWidth, GreetingHeight);
        if (_compact)
            return (CompactWidth, CompactHeight);
        if (ShowCollapsedLyric)
            return (Math.Clamp(_collapsedLyricWidth + CollapsedLyricChrome, CollapsedWidth, CollapsedLyricMaxWidth), CollapsedHeight);
        return (CollapsedWidth, CollapsedHeight);
    }

    private CornerRadius TargetRadius(double height)
    {
        double bottom = _expanded ? 26 : _compact ? 3 : _greeting ? 20 : 14;
        // A floating notch is rounded all round; an anchored one only at the bottom, the top meets the screen edge.
        return _shape == NotchShape.Floating
            ? new CornerRadius(Math.Min(bottom, height / 2))
            : new CornerRadius(0, 0, bottom, bottom);
    }

    /// <summary>Animates the notch to the size and look its current state needs, if that changed.</summary>
    private void UpdateShape()
    {
        bool showLyric = ShowCollapsedLyric;
        LyricRow.Visibility = showLyric ? Visibility.Visible : Visibility.Collapsed;
        ClockRow.Visibility = showLyric ? Visibility.Collapsed : Visibility.Visible;
        UpdateNotchColors();
        UpdateViews();

        Visibility ears = _shape == NotchShape.Anchored && !(_compact && !_expanded) ? Visibility.Visible : Visibility.Collapsed;
        LeftEar.Visibility = RightEar.Visibility = ears;

        (double width, double height) = TargetSize();
        if (width == _shapeWidth && height == _shapeHeight && _expanded == _shapeExpanded)
            return;

        bool opening = _expanded && !_shapeExpanded;
        _shapeWidth = width;
        _shapeHeight = height;
        _shapeExpanded = _expanded;

        Notch.CornerRadius = TargetRadius(height);
        IEasingFunction ease = opening
            ? new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        AnimateSize(width, height, ease, opening ? 320 : 260);
    }

    /// <summary>Fades between the greeting, the closed rows and the open pages.</summary>
    private void UpdateViews()
    {
        FadeTo(GreetingView, _greeting && !_expanded ? 1 : 0, 120);
        FadeTo(CollapsedView, !_expanded && !_greeting && !_compact ? 1 : 0, 120);
        FadeTo(ExpandedView, _expanded ? 1 : 0, 90);
    }

    private void FadeTo(UIElement element, double to, int delayWhenShowing)
    {
        if (_fadeTargets.TryGetValue(element, out double current) && current == to)
            return;
        _fadeTargets[element] = to;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 200 : 110))
        {
            BeginTime = TimeSpan.FromMilliseconds(to > 0 ? delayWhenShowing : 0),
        });
    }

    /// <summary>
    /// While lyrics are shown (the lyric line when closed, the music page when open) the notch takes a
    /// gradient from the album art; otherwise it has the theme's colour. Colours fade rather than jump.
    /// </summary>
    private void UpdateNotchColors()
    {
        bool tinted = _lyricsEnabled && _artColors is not null &&
                      (_expanded ? _page == NotchPage.Music : ShowCollapsedLyric);
        Color plain = ThemeManager.NotchColor;
        (Color Start, Color End) target = tinted ? _artColors!.Value : (plain, plain);
        if (target == _notchColors)
            return;
        _notchColors = target;

        var duration = TimeSpan.FromMilliseconds(700);
        NotchStart.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(target.Start, duration));
        NotchEnd.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(target.End, duration));
        LeftEarFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target.Start, duration));
        RightEarFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target.End, duration));
    }

    private void AnimateSize(double width, double height, IEasingFunction ease, int milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        Notch.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
        Notch.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
    }

    // ---- Startup greeting --------------------------------------------------------

    private async Task RunGreetingAsync()
    {
        _greeting = true;
        GreetText.Visibility = Visibility.Collapsed;
        _greetingWidth = GreetingLogoOnlyWidth;
        UpdateShape();

        // The logo pops in and blinks.
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
        };
        GreetLogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        GreetLogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        await Task.Delay(800);
        Blink();
        await Task.Delay(600);
        if (!_greeting)
            return;

        // Then the greeting slides out next to it.
        GreetText.Text = GreetingText();
        GreetText.Visibility = Visibility.Visible;
        _greetingWidth = 38 + 12 + MeasureText(GreetText.Text, GreetText) + 56;
        GreetText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(120),
        });
        UpdateShape();

        await Task.Delay(2400);
        if (_greeting)
            EndGreeting();
    }

    private void EndGreeting()
    {
        _greeting = false;
        UpdateShape();
    }

    private void Blink()
    {
        var blink = new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
        LeftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        RightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
    }

    private static string GreetingText()
    {
        int hour = DateTime.Now.Hour;
        string part = hour switch
        {
            >= 5 and < 12 => "Good morning",
            >= 12 and < 17 => "Good afternoon",
            >= 17 and < 22 => "Good evening",
            _ => "Hello",
        };
        string name = AppSettings.GreetingNameAuto || AppSettings.GreetingName.Length == 0
            ? Maintenance.WindowsFirstName()
            : AppSettings.GreetingName;
        return $"{part}, {name}";
    }

    // ---- Clock and idle widgets --------------------------------------------------

    private void Tick()
    {
        DateTime now = DateTime.Now;
        // Escaped colon: some cultures print the time separator as "." (e.g. "20.35").
        BigClock.Text = now.ToString(@"HH\:mm", English);
        DateText.Text = now.ToString("dddd, d MMMM", English);

        // Battery level has no change event of its own; reading it is a cheap system call.
        UpdateBattery();
        UpdateCompact();

        IdleWidget[] shown = ShownIdleWidgets();
        _idle.Tick(
            weather: Array.IndexOf(shown, IdleWidget.Weather) >= 0,
            ping: Array.IndexOf(shown, IdleWidget.Ping) >= 0,
            fps: Array.IndexOf(shown, IdleWidget.Fps) >= 0);
        RenderIdleSlots();

        if (_expanded)
        {
            UpdateMediaProgress();
            if (_page == NotchPage.ControlCenter)
                ControlCenterPage.RefreshVolume();
            if (_page == NotchPage.Settings)
                SettingsPage.UpdatePreview(BuildIdleSnapshot());
        }
    }

    private static IdleWidget[] ShownIdleWidgets() => new[] { AppSettings.IdleLeft, AppSettings.IdleCenter, AppSettings.IdleRight };

    private IdleSnapshot BuildIdleSnapshot() => new()
    {
        Now = DateTime.Now,
        HasBattery = _hasBattery,
        BatteryLevel = _batteryLevel,
        Charging = _charging,
        OpenTodos = _todos.Items.Count - _todos.DoneCount,
        Temperature = _idle.Temperature,
        WeatherSymbol = _idle.WeatherSymbol,
        PingMs = _idle.PingMs,
        Fps = _idle.Fps,
    };

    private void RenderIdleSlots()
    {
        IdleSnapshot snapshot = BuildIdleSnapshot();
        IdleWidgetView.Render(SlotLeft, AppSettings.IdleLeft, snapshot);
        IdleWidgetView.Render(SlotCenter, AppSettings.IdleCenter, snapshot);
        IdleWidgetView.Render(SlotRight, AppSettings.IdleRight, snapshot);

        // Album art and a running timer take over the side slots.
        SlotLeft.Visibility = _media.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
        SlotRight.Visibility = _pomodoro.IsRunning ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Auto-compact: shrink to a thin line while a web browser is the active window.</summary>
    private void UpdateCompact()
    {
        bool compact = false;
        if (_autoCompact)
        {
            IntPtr window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out int pid);
            if (pid != Environment.ProcessId)
            {
                if (pid != _foregroundPid)
                {
                    _foregroundPid = pid;
                    try
                    {
                        using Process process = Process.GetProcessById(pid);
                        _foregroundIsBrowser = Browsers.Contains(process.ProcessName);
                    }
                    catch (Exception)
                    {
                        _foregroundIsBrowser = false;
                    }
                }
                compact = _foregroundIsBrowser;
            }
            else
            {
                compact = _compact; // our own window (typing): keep whatever it was
            }
        }

        if (compact == _compact)
            return;
        _compact = compact;
        UpdateShape();
    }

    // ---- Battery -----------------------------------------------------------------

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(UpdateBattery));

    private void UpdateBattery()
    {
        System.Windows.Forms.PowerStatus status = System.Windows.Forms.SystemInformation.PowerStatus;
        _hasBattery =
            !status.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) &&
            status.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown &&
            status.BatteryLifePercent <= 1; // 255% means "unknown"
        _charging = status.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        _batteryLevel = Math.Clamp(status.BatteryLifePercent, 0, 1);

        HeaderBattery.Visibility = _hasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (!_hasBattery)
            return;

        HeaderBatteryIcon.Update(_batteryLevel, _charging);
        BatteryText.Text = $"{Math.Round(_batteryLevel * 100)}%";
        HeaderBattery.ToolTip = _charging ? "Charging" : "On battery";
    }

    // ---- Music -------------------------------------------------------------------

    private void UpdateMedia()
    {
        bool hasSession = _media.HasSession;
        bool hasTrack = hasSession && _media.Title.Length > 0;

        TrackTitle.Text = hasTrack ? _media.Title : "Nothing playing";
        TrackArtist.Text = hasTrack ? _media.Artist : "Play something on Spotify, YouTube…";

        PlayPauseBtn.Content = _media.IsPlaying ? "" : "";
        PlayPauseBtn.IsEnabled = hasSession && _media.CanTogglePlay;
        PrevBtn.IsEnabled = hasSession && _media.CanPrevious;
        NextBtn.IsEnabled = hasSession && _media.CanNext;

        MiniArt.Visibility = _media.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        if (!ReferenceEquals(_shownThumbnail, _media.Thumbnail))
        {
            _shownThumbnail = _media.Thumbnail;
            _artColors = ArtworkPalette.FromImage(_shownThumbnail, ThemeManager.IsLight);
            ImageSource? image = LoadImage(_shownThumbnail);
            ShowArt(Artwork, ArtworkGlyph, image);
            ShowArt(MiniArt, MiniArtGlyph, image);
            ShowArt(LyricArt, LyricArtGlyph, image);
        }

        UpdateMediaProgress();
        RenderIdleSlots();

        if (_lyricsEnabled)
            EnsureLyricsForCurrentTrack();
        UpdateLyricsTimer();
        UpdateLyricsPosition();
        UpdateShape();
    }

    /// <summary>Album art as the tile's background, or the theme placeholder with a music glyph.</summary>
    private static void ShowArt(Border tile, UIElement glyph, ImageSource? image)
    {
        if (image is null)
            tile.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        else
            tile.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        glyph.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMediaProgress()
    {
        TimeSpan duration = _media.Duration;
        bool hasTimeline = _media.HasSession && duration > TimeSpan.Zero;

        SeekArea.Visibility = hasTimeline ? Visibility.Visible : Visibility.Hidden;
        if (!hasTimeline)
        {
            ElapsedText.Text = "";
            RemainingText.Text = "";
            return;
        }

        TimeSpan position = _media.Position;
        ProgressFill.Width = ProgressTrack.ActualWidth * Math.Clamp(position / duration, 0, 1);
        ElapsedText.Text = FormatTrackTime(position);
        RemainingText.Text = "-" + FormatTrackTime(duration - position);
    }

    private void SeekArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_media.CanSeek || ProgressTrack.ActualWidth <= 0)
            return;

        double fraction = Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);
        _media.Seek(_media.Duration * fraction);
        UpdateMediaProgress();
        UpdateLyricsPosition();
    }

    private void PrevBtn_Click(object sender, RoutedEventArgs e) => _media.Previous();
    private void PlayPauseBtn_Click(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void NextBtn_Click(object sender, RoutedEventArgs e) => _media.Next();

    private static string FormatTrackTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static ImageSource? LoadImage(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 128;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // Some players hand out thumbnails in formats WPF cannot decode.
            return null;
        }
    }

    // ---- Lyrics ------------------------------------------------------------------

    /// <summary>Show or hide lyrics. Also driven from the tray menu.</summary>
    public bool LyricsVisible
    {
        get => _lyricsEnabled;
        set
        {
            if (value == _lyricsEnabled)
                return;
            _lyricsEnabled = value;
            AppSettings.LyricsEnabled = value;
            ApplyLyricsMode();
        }
    }

    private void LyricsBtn_Click(object sender, RoutedEventArgs e) => LyricsVisible = !LyricsVisible;

    private void ApplyLyricsMode()
    {
        LyricsBtn.Content = _lyricsEnabled ? "Hide lyrics" : "Show lyrics";
        LyricsBtn.SetResourceReference(BackgroundProperty, _lyricsEnabled ? "PillActiveBrush" : "PillBrush");
        LyricsCard.Visibility = _lyricsEnabled ? Visibility.Visible : Visibility.Collapsed;
        ExpandedView.Width = ExpandedContentWidth;

        if (_lyricsEnabled)
            EnsureLyricsForCurrentTrack();
        UpdateLyricsTimer();
        UpdateLyricsPosition(force: true);
        UpdateShape();
    }

    private void EnsureLyricsForCurrentTrack()
    {
        string key = _media.HasSession && _media.Title.Length > 0 ? $"{_media.Artist}\n{_media.Title}" : "";
        if (key == _lyricsKey)
            return;

        _lyricsKey = key;
        _lyricsCancellation?.Cancel();
        _lyrics = null;
        _lyricsLoading = key.Length > 0;
        RebuildLyricsPanel();

        if (key.Length > 0)
            _ = LoadLyricsAsync(_media.Title, _media.Artist, _media.Album, _media.Duration);
    }

    private async Task LoadLyricsAsync(string title, string artist, string album, TimeSpan duration)
    {
        var cancellation = new CancellationTokenSource();
        _lyricsCancellation = cancellation;

        Lyrics? found = null;
        try
        {
            found = await _lyricsService.FindAsync(title, artist, album, duration, cancellation.Token);
        }
        catch (Exception)
        {
            // Offline, LRCLIB down or a newer track took over: show "not found" (or nothing, if cancelled).
        }

        if (cancellation.IsCancellationRequested)
            return;

        _lyricsLoading = false;
        _lyrics = found;
        RebuildLyricsPanel();
        UpdateLyricsTimer();
        UpdateLyricsPosition(force: true);
        UpdateShape();
    }

    private void RebuildLyricsPanel()
    {
        LyricsPanel.Children.Clear();
        LyricsScroll.BeginAnimation(TranslateTransform.YProperty, null);
        LyricsScroll.Y = 0;
        _currentLine = -1;

        string? status =
            _lyricsKey.Length == 0 ? "Nothing playing" :
            _lyricsLoading ? "Finding lyrics…" :
            _lyrics is null ? "No lyrics found" :
            _lyrics.IsInstrumental ? "Instrumental ♪" :
            null;
        LyricsStatus.Text = status ?? "";
        LyricsStatus.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        if (status is not null || _lyrics is null)
            return;

        bool synced = _lyrics.IsSynced;
        foreach (LyricLine line in _lyrics.Lines)
        {
            var text = new TextBlock
            {
                Text = line.Text.Length > 0 ? line.Text : "♪",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, LyricLineGap),
                Opacity = synced ? InactiveLyricOpacity : 0.85,
            };
            if (synced)
            {
                text.Cursor = Cursors.Hand;
                text.MouseLeftButtonDown += (_, _) => SeekToLyric(line);
            }
            LyricsPanel.Children.Add(text);
        }
        LyricsPanel.UpdateLayout();
    }

    private void UpdateLyricsTimer()
    {
        bool run = _lyricsEnabled && _lyrics is { IsSynced: true } && _media.IsPlaying;
        if (run && !_lyricsTimer.IsEnabled)
            _lyricsTimer.Start();
        else if (!run && _lyricsTimer.IsEnabled)
            _lyricsTimer.Stop();
    }

    private void UpdateLyricsPosition(bool force = false)
    {
        if (!_lyricsEnabled || _lyrics is not { IsSynced: true } lyrics)
            return;

        int index = lyrics.LineAt(_media.Position + LyricLead);
        if (index == _currentLine && !force)
            return;
        _currentLine = index;

        string text = index >= 0 && lyrics.Lines[index].Text.Length > 0 ? lyrics.Lines[index].Text : "♪";
        CollapsedLyric.Text = text;
        _collapsedLyricWidth = MeasureText(text, CollapsedLyric);
        CollapsedLyric.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));

        HighlightLyric(index);
        UpdateShape();
    }

    private void HighlightLyric(int index)
    {
        for (int i = 0; i < LyricsPanel.Children.Count; i++)
            LyricsPanel.Children[i].Opacity = i == index ? 1 : InactiveLyricOpacity;

        // Keep one earlier line visible above the current one.
        double target = 0;
        if (index > 0 && LyricsPanel.Children[index - 1] is FrameworkElement previous)
            target = -previous.TranslatePoint(new Point(), LyricsPanel).Y;

        LyricsScroll.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void SeekToLyric(LyricLine line)
    {
        if (!_media.CanSeek)
            return;
        _media.Seek(line.Time);
        UpdateMediaProgress();
        UpdateLyricsPosition(force: true);
    }

    /// <summary>Plain (unsynced) lyrics don't scroll by themselves, so the wheel scrolls them.</summary>
    private void LyricsCard_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_lyrics is null || _lyrics.IsSynced)
            return;

        double lowest = Math.Min(0, LyricsViewport.ActualHeight - LyricsPanel.ActualHeight);
        LyricsScroll.BeginAnimation(TranslateTransform.YProperty, null);
        LyricsScroll.Y = Math.Clamp(LyricsScroll.Y + e.Delta / 4.0, lowest, 0);
        e.Handled = true;
    }

    private double MeasureText(string text, TextBlock style)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(style.FontFamily, style.FontStyle, style.FontWeight, style.FontStretch),
            style.FontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    // ---- To-do -------------------------------------------------------------------

    private void UpdateTodoSummary()
    {
        int total = _todos.Items.Count;
        TodoCount.Text = total == 0 ? "" : $"{_todos.DoneCount}/{total} done";
        TodoEmpty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TodoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTodo();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TodoInput.Clear();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void TodoInput_TextChanged(object sender, TextChangedEventArgs e) =>
        TodoPlaceholder.Visibility = TodoInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void TodoAdd_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void AddTodo()
    {
        _todos.Add(TodoInput.Text);
        TodoInput.Clear();
    }

    private void TodoCheck_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TodoItem item)
            _todos.Toggled(item);
    }

    private void TodoDelete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TodoItem item)
            _todos.Remove(item);
    }

    // ---- Pomodoro ----------------------------------------------------------------

    private void UpdatePomodoro()
    {
        string remaining = PomodoroTimer.Format(_pomodoro.Remaining);
        Brush accent = _pomodoro.Mode == PomodoroMode.Focus ? FocusBrush : BreakBrush;

        PomoMode.Text = _pomodoro.Mode switch
        {
            PomodoroMode.Focus => "Focus",
            PomodoroMode.ShortBreak => "Short break",
            _ => "Long break",
        };
        PomoSessions.Text = $"· {_pomodoro.CompletedFocusSessions % PomodoroTimer.SessionsPerCycle}/{PomodoroTimer.SessionsPerCycle} sessions";
        PomoTime.Text = remaining;
        PomoFill.Background = accent;
        PomoFill.Width = PomoTrack.ActualWidth * _pomodoro.Progress;
        PomoStartBtn.Content = _pomodoro.IsRunning ? "" : "";

        MiniPomo.Text = remaining;
        MiniPomo.Foreground = accent;
        MiniPomo.Visibility = _pomodoro.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        SlotRight.Visibility = _pomodoro.IsRunning ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPomodoroFinished(PomodoroMode finished)
    {
        SystemSounds.Asterisk.Play();
        Peek(TimeSpan.FromSeconds(5), NotchPage.Focus);
    }

    private void PomoStartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pomodoro.IsRunning)
            _pomodoro.Pause();
        else
            _pomodoro.Start();
    }

    private void PomoResetBtn_Click(object sender, RoutedEventArgs e) => _pomodoro.Reset();
    private void PomoSkipBtn_Click(object sender, RoutedEventArgs e) => _pomodoro.Skip();

    // ---- Snapshots (developer aid) -----------------------------------------------

    /// <summary>
    /// Renders every state of the notch to "&lt;base&gt;-&lt;state&gt;.png": the greeting, closed, each page,
    /// each Settings tab, and the closed notch and Control Center in the light theme and floating shape.
    /// </summary>
    public async Task SaveSnapshotsAsync(string basePath)
    {
        // Give the media session, lyrics and idle widgets a moment to answer.
        Tick();
        await Task.Delay(TimeSpan.FromSeconds(5));
        Tick();
        string Path(string state) => System.IO.Path.ChangeExtension(basePath, null) + "-" + state + ".png";

        _greeting = true;
        GreetText.Text = GreetingText();
        GreetText.Visibility = Visibility.Visible;
        _greetingWidth = 38 + 12 + MeasureText(GreetText.Text, GreetText) + 56;
        Save(Path("greeting"), expanded: false);
        _greeting = false;

        Save(Path("closed"), expanded: false);
        foreach (NotchPage page in Enum.GetValues<NotchPage>())
        {
            ShowPage(page, direction: 0);
            if (page == NotchPage.Settings)
            {
                for (int tab = 0; tab < 4; tab++)
                {
                    SettingsPage.SelectTab(tab);
                    Save(Path($"settings-{tab}"), expanded: true);
                }
                SettingsPage.SelectTab(0);
                continue;
            }
            if (page == NotchPage.ControlCenter)
            {
                ControlCenterPage.Refresh();
                await Task.Delay(1500); // radios and brightness are read asynchronously
            }
            Save(Path(page.ToString().ToLowerInvariant()), expanded: true);
        }

        // Light theme and floating shape, without touching the saved preferences.
        ThemeManager.Apply(ThemeMode.Light);
        _shape = NotchShape.Floating;
        _notchColors = null;
        ShowPage(NotchPage.ControlCenter, direction: 0);
        await Task.Delay(800);
        Save(Path("light-closed"), expanded: false);
        Save(Path("light-controlcenter"), expanded: true);
        ThemeManager.Apply(AppSettings.Theme);
        _shape = AppSettings.Shape;
    }

    private void Save(string path, bool expanded)
    {
        SetStateImmediately(expanded);
        BitmapSource notch = RenderRoot();

        double w = Root.ActualWidth, h = Root.ActualHeight;
        // Floating snapshots sit lower, as on screen.
        double top = _shape == NotchShape.Floating ? AppSettings.FloatingDistance : 0;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var backdrop = new LinearGradientBrush(Color.FromRgb(0xE4, 0xE8, 0xF2), Color.FromRgb(0xC7, 0xCE, 0xDE), 90);
            dc.DrawRectangle(backdrop, null, new Rect(0, 0, w, h));
            dc.DrawImage(notch, new Rect(0, top, w, h));
        }
        var full = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        full.Render(visual);

        // Crop to the notch with a margin around it, at the 2x scale of the render.
        const double margin = 28;
        Point origin = NotchHost.TranslatePoint(new Point(), Root);
        double left = Math.Max(0, origin.X - margin);
        double right = Math.Min(w, origin.X + NotchHost.ActualWidth + margin);
        double bottom = Math.Min(h, top + origin.Y + NotchHost.ActualHeight + margin);
        var crop = new Int32Rect((int)(left * 2), 0, (int)((right - left) * 2), (int)(bottom * 2));
        var output = new CroppedBitmap(full, crop);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    private void SetStateImmediately(bool expanded)
    {
        _expanded = expanded;
        ExpandedView.Height = ExpandedHeightFor(_page) - ExpandedVerticalPadding;
        ExpandedView.Width = ExpandedContentWidth;
        UpdateMedia();
        UpdatePomodoro();
        RenderIdleSlots();
        UpdateShape();

        Notch.BeginAnimation(WidthProperty, null);
        Notch.BeginAnimation(HeightProperty, null);
        foreach (UIElement view in new UIElement[] { CollapsedView, ExpandedView, GreetingView, CollapsedLyric, GreetText })
            view.BeginAnimation(OpacityProperty, null);
        _fadeTargets.Clear();
        GreetLogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        GreetLogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        NotchStart.BeginAnimation(GradientStop.ColorProperty, null);
        NotchEnd.BeginAnimation(GradientStop.ColorProperty, null);
        LeftEarFill.BeginAnimation(SolidColorBrush.ColorProperty, null);
        RightEarFill.BeginAnimation(SolidColorBrush.ColorProperty, null);
        (Color start, Color end) = _notchColors ?? (ThemeManager.NotchColor, ThemeManager.NotchColor);
        NotchStart.Color = LeftEarFill.Color = start;
        NotchEnd.Color = RightEarFill.Color = end;
        LyricsScroll.BeginAnimation(TranslateTransform.YProperty, null);
        foreach (NotchPage page in Enum.GetValues<NotchPage>())
        {
            PageElement(page).BeginAnimation(OpacityProperty, null);
            PageElement(page).RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        }

        (_shapeWidth, _shapeHeight) = TargetSize();
        _shapeExpanded = expanded;
        Notch.Width = _shapeWidth;
        Notch.Height = _shapeHeight;
        Notch.CornerRadius = TargetRadius(_shapeHeight);
        GreetingView.Opacity = _greeting && !expanded ? 1 : 0;
        CollapsedView.Opacity = !expanded && !_greeting && !_compact ? 1 : 0;
        ExpandedView.Opacity = expanded ? 1 : 0;
        UpdateLayout();
        UpdateMediaProgress();
        UpdatePomodoro();
        if (_currentLine > 0 && LyricsPanel.Children[_currentLine - 1] is FrameworkElement previous)
            LyricsScroll.Y = -previous.TranslatePoint(new Point(), LyricsPanel).Y;
        UpdateLayout();
    }

    private BitmapSource RenderRoot()
    {
        var bitmap = new RenderTargetBitmap((int)(Root.ActualWidth * 2), (int)(Root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(Root);
        return bitmap;
    }

    // ---- Win32 -------------------------------------------------------------------

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}

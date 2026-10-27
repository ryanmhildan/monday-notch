using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MondayNotch.Services;

namespace MondayNotch.Pages;

/// <summary>General, Appearance, Idle and Advanced settings. Every change is saved at once and announced through <see cref="Changed"/>.</summary>
public partial class SettingsPage : UserControl
{
    private static readonly IdleWidget[] Widgets = Enum.GetValues<IdleWidget>();

    private readonly DispatcherTimer _confirmTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private Button? _armed; // clean/reset button waiting for its confirming second click
    private bool _loading;
    private IdleSnapshot? _lastSnapshot;

    /// <summary>A setting changed; the notch should re-read <see cref="AppSettings"/>.</summary>
    public event Action? Changed;

    public SettingsPage()
    {
        InitializeComponent();
        _confirmTimer.Tick += (_, _) => Disarm();
        UpdateStatus.Text = $"Version {Maintenance.CurrentVersion}";
        Load();
    }

    /// <summary>Shows the stored values. Called on start and after a reset.</summary>
    public void Load()
    {
        _loading = true;

        StartupToggle.IsChecked = StartupManager.IsEnabled;
        GreetingToggle.IsChecked = AppSettings.GreetingEnabled;
        (AppSettings.GreetingNameAuto ? NameAutoBtn : NameCustomBtn).IsChecked = true;
        GreetingNameBox.Text = AppSettings.GreetingName;
        CompactToggle.IsChecked = AppSettings.AutoCompact;

        (AppSettings.Shape == NotchShape.Floating ? FloatingBtn : AnchoredBtn).IsChecked = true;
        DistanceSlider.Value = AppSettings.FloatingDistance;
        (AppSettings.Theme switch
        {
            ThemeMode.Light => LightBtn,
            ThemeMode.Auto => AutoThemeBtn,
            _ => DarkBtn,
        }).IsChecked = true;

        _loading = false;
        UpdateDependentRows();
        ShowIdleChoices();
    }

    public void SelectTab(int index)
    {
        RadioButton[] tabs = { GeneralTabBtn, AppearanceTabBtn, IdleTabBtn, AdvancedTabBtn };
        tabs[Math.Clamp(index, 0, tabs.Length - 1)].IsChecked = true;
    }

    /// <summary>Redraws the live preview of the closed notch. Called every second while the page is open.</summary>
    public void UpdatePreview(IdleSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        IdleWidgetView.Render(PreviewLeft, AppSettings.IdleLeft, snapshot);
        IdleWidgetView.Render(PreviewCenter, AppSettings.IdleCenter, snapshot);
        IdleWidgetView.Render(PreviewRight, AppSettings.IdleRight, snapshot);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (GeneralTab is null)
            return; // fired while the page is still being built
        GeneralTab.Visibility = Show(GeneralTabBtn);
        AppearanceTab.Visibility = Show(AppearanceTabBtn);
        IdleTab.Visibility = Show(IdleTabBtn);
        AdvancedTab.Visibility = Show(AdvancedTabBtn);
        Disarm();
    }

    private static Visibility Show(RadioButton tab) => tab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateDependentRows()
    {
        bool greeting = GreetingToggle.IsChecked == true;
        GreetingNameRow.Visibility = greeting ? Visibility.Visible : Visibility.Collapsed;
        CustomNameBox.Visibility = greeting && NameCustomBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GreetingNamePlaceholder.Visibility = GreetingNameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DistanceRow.Visibility = FloatingBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DistanceText.Text = $"{(int)DistanceSlider.Value} px";
    }

    private void Save(Action write)
    {
        if (_loading)
            return;
        write();
        UpdateDependentRows();
        Changed?.Invoke();
    }

    // ---- General -----------------------------------------------------------------------------

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupManager.SetEnabled(StartupToggle.IsChecked == true);
        }
        catch (Exception)
        {
            StartupToggle.IsChecked = StartupManager.IsEnabled;
        }
    }

    private void GreetingToggle_Click(object sender, RoutedEventArgs e) =>
        Save(() => AppSettings.GreetingEnabled = GreetingToggle.IsChecked == true);

    private void GreetingName_Checked(object sender, RoutedEventArgs e)
    {
        if (NameAutoBtn is null || NameCustomBtn is null)
            return;
        Save(() => AppSettings.GreetingNameAuto = NameAutoBtn.IsChecked == true);
    }

    private void GreetingNameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Save(() => AppSettings.GreetingName = GreetingNameBox.Text.Trim());

    private void CompactToggle_Click(object sender, RoutedEventArgs e) =>
        Save(() => AppSettings.AutoCompact = CompactToggle.IsChecked == true);

    // ---- Appearance --------------------------------------------------------------------------

    private void Shape_Checked(object sender, RoutedEventArgs e)
    {
        if (FloatingBtn is null)
            return;
        Save(() => AppSettings.Shape = FloatingBtn.IsChecked == true ? NotchShape.Floating : NotchShape.Anchored);
    }

    private void DistanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DistanceText is null)
            return;
        DistanceText.Text = $"{(int)e.NewValue} px";
        Save(() => AppSettings.FloatingDistance = (int)e.NewValue);
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (LightBtn is null || AutoThemeBtn is null)
            return;
        Save(() => AppSettings.Theme =
            LightBtn.IsChecked == true ? ThemeMode.Light :
            AutoThemeBtn.IsChecked == true ? ThemeMode.Auto :
            ThemeMode.Dark);
    }

    // ---- Idle --------------------------------------------------------------------------------

    private void Cycle_Click(object sender, RoutedEventArgs e)
    {
        // Tag is "<slot>:<step>", e.g. "Left:1".
        string[] parts = ((string)((FrameworkElement)sender).Tag).Split(':');
        int step = int.Parse(parts[1]);
        IdleWidget Next(IdleWidget current) =>
            Widgets[(Array.IndexOf(Widgets, current) + step + Widgets.Length) % Widgets.Length];

        Save(() =>
        {
            switch (parts[0])
            {
                case "Left": AppSettings.IdleLeft = Next(AppSettings.IdleLeft); break;
                case "Center": AppSettings.IdleCenter = Next(AppSettings.IdleCenter); break;
                default: AppSettings.IdleRight = Next(AppSettings.IdleRight); break;
            }
        });
        ShowIdleChoices();
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        IdleWidget left = AppSettings.IdleLeft, center = AppSettings.IdleCenter, right = AppSettings.IdleRight;
        Save(() =>
        {
            switch ((string)((FrameworkElement)sender).Tag)
            {
                case "LeftCenter": AppSettings.IdleLeft = center; AppSettings.IdleCenter = left; break;
                case "CenterRight": AppSettings.IdleCenter = right; AppSettings.IdleRight = center; break;
                default: AppSettings.IdleRight = left; AppSettings.IdleLeft = right; break;
            }
        });
        ShowIdleChoices();
    }

    private void ShowIdleChoices()
    {
        LeftName.Text = IdleWidgetView.Name(AppSettings.IdleLeft);
        CenterName.Text = IdleWidgetView.Name(AppSettings.IdleCenter);
        RightName.Text = IdleWidgetView.Name(AppSettings.IdleRight);
        if (_lastSnapshot is not null)
            UpdatePreview(_lastSnapshot);
    }

    // ---- Advanced ----------------------------------------------------------------------------

    private bool _updateAvailable;

    /// <summary>"Check" looks for a newer release; once one is found the button turns into "Download".</summary>
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_updateAvailable)
        {
            SystemControls.OpenUri(Maintenance.ReleasesPage);
            return;
        }
        UpdateBtn.IsEnabled = false;
        UpdateStatus.Text = "Checking…";
        (string message, bool available) = await Maintenance.CheckForUpdateAsync();
        UpdateStatus.Text = $"Version {Maintenance.CurrentVersion} · {message}";
        _updateAvailable = available;
        UpdateBtn.Content = available ? "Download" : "Check";
        UpdateBtn.IsEnabled = true;
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm(CleanBtn, CleanStatus, "Click again to delete them"))
            return;
        CleanBtn.IsEnabled = false;
        CleanStatus.Text = "Cleaning…";
        long freed = await Maintenance.CleanTempFilesAsync();
        CleanStatus.Text = $"Freed {freed / (1024.0 * 1024.0):0.#} MB";
        CleanBtn.IsEnabled = true;
    }

    private void RestartNotch_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Restart();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm(ResetBtn, ResetStatus, "Click again to reset everything"))
            return;
        AppSettings.ResetAll();
        try
        {
            StartupManager.SetEnabled(false);
        }
        catch (Exception)
        {
        }
        Load();
        ResetStatus.Text = "Settings are back to their defaults";
        Changed?.Invoke();
    }

    /// <summary>First click arms the button (red, with a hint); a second click within a few seconds confirms.</summary>
    private bool Confirm(Button button, TextBlock hint, string message)
    {
        if (_armed == button)
        {
            Disarm();
            return true;
        }
        Disarm();
        _armed = button;
        button.SetResourceReference(BackgroundProperty, "DangerBrush");
        hint.Text = message;
        _confirmTimer.Start();
        return false;
    }

    private void Disarm()
    {
        _confirmTimer.Stop();
        _armed?.ClearValue(BackgroundProperty);
        _armed = null;
    }
}

using System.Windows;
using System.Windows.Controls;
using MondayNotch.Services;

namespace MondayNotch.Pages;

/// <summary>A single scratch note that saves itself as you type.</summary>
public partial class NotePage : UserControl
{
    private readonly NoteStore _store = new();
    private bool _loading;

    public NotePage()
    {
        InitializeComponent();
        _loading = true;
        NoteBox.Text = _store.Load();
        _loading = false;
        UpdatePlaceholder();
    }

    /// <summary>Saves an unsaved edit right away (on exit).</summary>
    public void Flush() => _store.Flush();

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        if (_loading)
            return;
        _store.Update(NoteBox.Text);
        SavedText.Text = "Saved automatically";
    }

    private void UpdatePlaceholder() =>
        Placeholder.Visibility = NoteBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
}

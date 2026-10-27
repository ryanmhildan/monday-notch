using System;
using System.IO;
using System.Windows.Threading;

namespace MondayNotch.Services;

/// <summary>The quick note, kept in %AppData%\MondayNotch\note.txt and saved shortly after typing stops.</summary>
public sealed class NoteStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MondayNotch", "note.txt");

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private string _text = "";

    public NoteStore()
    {
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
    }

    public string Load()
    {
        try
        {
            _text = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
        }
        catch (Exception)
        {
            _text = "";
        }
        return _text;
    }

    public void Update(string text)
    {
        _text = text;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes any pending change now (used on exit).</summary>
    public void Flush()
    {
        if (!_saveTimer.IsEnabled)
            return;
        _saveTimer.Stop();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, _text);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception)
        {
        }
    }
}

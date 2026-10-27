using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MondayNotch.Services;

public sealed class TodoItem : INotifyPropertyChanged
{
    private string _text = "";
    private bool _done;

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Created { get; set; } = DateTime.Now;

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            OnChanged();
        }
    }

    public bool Done
    {
        get => _done;
        set
        {
            if (_done == value)
                return;
            _done = value;
            OnChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>
/// To-do list kept in %AppData%\MondayNotch\todos.json. Open tasks come first, finished ones sink to the bottom.
/// </summary>
public sealed class TodoStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MondayNotch", "todos.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public ObservableCollection<TodoItem> Items { get; } = new();

    public int DoneCount => Items.Count(item => item.Done);

    public event Action? Changed;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            List<TodoItem>? items = JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(FilePath), Json);
            foreach (TodoItem item in items?.OrderBy(i => i.Done) ?? Enumerable.Empty<TodoItem>())
                Items.Add(item);
        }
        catch (Exception)
        {
            // A damaged file should not stop the notch from starting; it is overwritten on the next change.
        }
        Changed?.Invoke();
    }

    public void Add(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;
        Items.Insert(FirstDoneIndex(), new TodoItem { Text = text });
        Save();
    }

    public void Remove(TodoItem item)
    {
        Items.Remove(item);
        Save();
    }

    /// <summary>Call after <see cref="TodoItem.Done"/> changed, to move the task to its group and save.</summary>
    public void Toggled(TodoItem item)
    {
        Items.Remove(item);
        if (item.Done)
            Items.Add(item);
        else
            Items.Insert(FirstDoneIndex(), item);
        Save();
    }

    private int FirstDoneIndex()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Done)
                return i;
        }
        return Items.Count;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Write then swap, so a crash mid-write never leaves a half-written list.
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Items, Json));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Disk full or folder locked: the list still works for this session.
        }
        Changed?.Invoke();
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Peshtoq.Services;

public sealed class NoteItem : INotifyPropertyChanged
{
    private string _text = "";
    private bool _done;

    public string Text
    {
        get => _text;
        set { _text = value; OnChanged(); }
    }

    public bool Done
    {
        get => _done;
        set { _done = value; OnChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Notes live in %AppData%\Peshtoq\notes.json and are saved on every change.</summary>
public sealed class NotesStore
{
    private static string FilePath => Path.Combine(AppPaths.DataDir, "notes.json");

    public ObservableCollection<NoteItem> Items { get; } = new();

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var items = JsonSerializer.Deserialize<List<NoteItem>>(File.ReadAllText(FilePath)) ?? new();
            foreach (var item in items) Attach(item);
        }
        catch
        {
            // a broken file should not take the app down; start empty
        }
    }

    public void Add(string text)
    {
        Attach(new NoteItem { Text = text.Trim() });
        Save();
    }

    public void Remove(NoteItem item)
    {
        item.PropertyChanged -= OnItemChanged;
        Items.Remove(item);
        Save();
    }

    private void Attach(NoteItem item)
    {
        item.PropertyChanged += OnItemChanged;
        Items.Add(item);
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Save();

    private void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Items.ToList())); }
        catch { }
    }
}

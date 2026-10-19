using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

internal sealed class LibraryRow : INotifyPropertyChanged
{
    private bool _editing;
    private string _editName = string.Empty;
    private string? _editError;

    public LibraryRow(TagInfo tag)
    {
        Tag = tag;
        EditName = tag.Name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TagInfo Tag { get; private set; }

    public Guid Id => Tag.Id;

    public string Name => Tag.Name;

    public string ExtraAliases => Copy.LibraryExtraAliases(Tag.Aliases, Tag.Name);

    public Visibility ExtraAliasesVisibility =>
        string.IsNullOrEmpty(ExtraAliases) ? Visibility.Collapsed : Visibility.Visible;

    public string RenameHint => Copy.LibraryRenameHint;

    public bool Editing
    {
        get => _editing;
        set
        {
            if (_editing == value)
                return;
            _editing = value;
            Notify(nameof(Editing));
            Notify(nameof(DisplayVisibility));
            Notify(nameof(EditVisibility));
        }
    }

    public Visibility DisplayVisibility => Editing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EditVisibility => Editing ? Visibility.Visible : Visibility.Collapsed;

    public string EditName
    {
        get => _editName;
        set
        {
            if (_editName == value)
                return;
            _editName = value;
            Notify();
        }
    }

    public string? EditError
    {
        get => _editError;
        set
        {
            if (_editError == value)
                return;
            _editError = value;
            Notify();
            Notify(nameof(EditErrorVisibility));
        }
    }

    public Visibility EditErrorVisibility =>
        string.IsNullOrEmpty(EditError) ? Visibility.Collapsed : Visibility.Visible;

    public void Replace(TagInfo tag)
    {
        Tag = tag;
        EditName = tag.Name;
        EditError = null;
        Editing = false;
        Notify(nameof(Name));
        Notify(nameof(ExtraAliases));
        Notify(nameof(ExtraAliasesVisibility));
        Notify(nameof(Id));
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

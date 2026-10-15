using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Presentation.ViewModels;

/// <summary>
/// One row in the metadata editor. With several photos selected, a field whose values differ is "mixed": it starts
/// empty (or, for lists, with the items every photo shares) and only changes the photos if you edit or clear it.
/// </summary>
public sealed partial class TagFieldViewModel : ObservableObject
{
    private readonly Action? _changed;

    internal TagFieldViewModel(
        TagKey key,
        string displayName,
        TagCategory category,
        EditorKind editor,
        string originalValue,
        bool isReadOnly,
        Action changed,
        bool isMixed = false)
    {
        Key = key;
        DisplayName = displayName;
        Category = category;
        Editor = editor;
        OriginalValue = originalValue;
        IsReadOnly = isReadOnly;
        IsMixed = isMixed;

        // Set before hooking up the change callback: loading a value isn't an edit.
        Value = originalValue;
        _changed = changed;
    }

    public TagKey Key { get; }

    public string KeyText => Key.ToString();

    public string DisplayName { get; }

    public TagCategory Category { get; }

    public EditorKind Editor { get; }

    public bool IsReadOnly { get; }

    public bool IsList => Editor == EditorKind.List;

    /// <summary>The selected photos have different values for this field.</summary>
    public bool IsMixed { get; }

    public bool CanClearAll => IsMixed && !IsReadOnly;

    /// <summary>The value as loaded; list values are joined with "; ". For a mixed list, the items all photos share.</summary>
    public string OriginalValue { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(RatingValue))]
    public partial string Value { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    /// <summary>For a mixed field: remove it from every selected photo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(Placeholder))]
    public partial bool ClearRequested { get; private set; }

    /// <summary>Mirrors the editor's "show tag keys" option, so item templates needn't reach up to the editor.</summary>
    [ObservableProperty]
    public partial bool ShowKey { get; internal set; }

    public bool IsDirty => !IsReadOnly && (Value != OriginalValue || ClearRequested);

    public bool HasError => Error is not null;

    /// <summary>The value as stars for a rating control; -1 means unset.</summary>
    public double RatingValue
    {
        get => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stars) ? stars : -1;
        set => Value = value < 0 ? string.Empty : ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
    }

    public string Placeholder => (IsMixed, ClearRequested, Editor) switch
    {
        (true, true, _) => "Will be removed from all selected photos",
        (true, false, EditorKind.List) => "Items shared by all photos; other items are kept",
        (true, false, _) => "Multiple values",
        (_, _, EditorKind.DateTime) => "YYYY:MM:DD HH:MM:SS",
        (_, _, EditorKind.List) => "Separate items with ;",
        _ => string.Empty,
    };

    public bool Matches(string filter) =>
        DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || KeyText.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The edits that apply this field's change to every photo. Empty means remove, except for a mixed text field,
    /// where empty means "leave each photo's value alone" (use <see cref="ClearAllCommand"/> to remove).
    /// </summary>
    public IReadOnlyList<MetadataEdit> ToEdits()
    {
        if (ClearRequested)
        {
            return [MetadataEdit.Remove(Key)];
        }

        if (IsList)
        {
            var items = TagCatalog.SplitList(Value);
            if (!IsMixed)
            {
                return [items.Count == 0 ? MetadataEdit.Remove(Key) : MetadataEdit.Set(Key, items)];
            }

            // Mixed list: add and remove only what changed among the shared items, keeping each photo's others.
            var shared = TagCatalog.SplitList(OriginalValue);
            var added = items.Except(shared).ToList();
            var removed = shared.Except(items).ToList();
            var edits = new List<MetadataEdit>();
            if (added.Count > 0)
            {
                edits.Add(MetadataEdit.AddToList(Key, added));
            }

            if (removed.Count > 0)
            {
                edits.Add(MetadataEdit.RemoveFromList(Key, removed));
            }

            return edits;
        }

        // Keep multi-line text exactly as typed; trim single-line fields.
        var value = Editor == EditorKind.MultilineText ? Value : Value.Trim();
        if (value.Length == 0)
        {
            return IsMixed ? [] : [MetadataEdit.Remove(Key)];
        }

        return [MetadataEdit.Set(Key, value)];
    }

    [RelayCommand]
    public void Revert()
    {
        ClearRequested = false;
        Value = OriginalValue;
        _changed?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanClearAll))]
    private void ClearAll()
    {
        ClearRequested = true;
        Value = string.Empty;
        _changed?.Invoke();
    }

    partial void OnValueChanged(string value)
    {
        // Typing into a field marked for clearing means you want that value instead.
        if (value.Length > 0)
        {
            ClearRequested = false;
        }

        Error = IsReadOnly ? null : TagCatalog.Validate(Editor, Editor == EditorKind.MultilineText ? value : value.Trim());
        _changed?.Invoke();
    }
}

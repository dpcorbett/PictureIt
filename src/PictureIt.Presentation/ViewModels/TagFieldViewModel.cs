using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Presentation.ViewModels;

/// <summary>One editable (or read-only) row in the metadata editor.</summary>
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
        Action changed)
    {
        Key = key;
        DisplayName = displayName;
        Category = category;
        Editor = editor;
        OriginalValue = originalValue;
        IsReadOnly = isReadOnly;

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

    /// <summary>The value as loaded from the file; list values are joined with "; ".</summary>
    public string OriginalValue { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(RatingValue))]
    public partial string Value { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    /// <summary>Mirrors the editor's "show tag keys" option, so item templates needn't reach up to the editor.</summary>
    [ObservableProperty]
    public partial bool ShowKey { get; internal set; }

    public bool IsDirty => !IsReadOnly && Value != OriginalValue;

    public bool HasError => Error is not null;

    /// <summary>The value as stars for a rating control; -1 means unset.</summary>
    public double RatingValue
    {
        get => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stars) ? stars : -1;
        set => Value = value < 0 ? string.Empty : ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
    }

    public string Placeholder => Editor switch
    {
        EditorKind.DateTime => "YYYY:MM:DD HH:MM:SS",
        EditorKind.List => "Separate items with ;",
        _ => string.Empty,
    };

    public bool Matches(string filter) =>
        DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || KeyText.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>The edit that turns <see cref="OriginalValue"/> into <see cref="Value"/>. An empty value removes the tag.</summary>
    public MetadataEdit ToEdit()
    {
        var trimmed = Value.Trim();
        if (IsList)
        {
            var items = TagCatalog.SplitList(Value);
            return items.Count == 0 ? MetadataEdit.Remove(Key) : MetadataEdit.Set(Key, items);
        }

        // Keep multi-line text exactly as typed; trim single-line fields.
        var value = Editor == EditorKind.MultilineText ? Value : trimmed;
        return value.Length == 0 ? MetadataEdit.Remove(Key) : MetadataEdit.Set(Key, value);
    }

    public void Revert() => Value = OriginalValue;

    /// <summary>Marks the current value as saved.</summary>
    internal void AcceptValue()
    {
        OriginalValue = Value;
        OnPropertyChanged(nameof(OriginalValue));
        OnPropertyChanged(nameof(IsDirty));
    }

    partial void OnValueChanged(string value)
    {
        Error = IsReadOnly ? null : TagCatalog.Validate(Editor, Editor == EditorKind.MultilineText ? value : value.Trim());
        _changed?.Invoke();
    }
}

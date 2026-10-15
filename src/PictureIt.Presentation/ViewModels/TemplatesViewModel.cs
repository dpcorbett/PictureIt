using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureIt.Core.Catalog;
using PictureIt.Core.Templates;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels;

/// <summary>A template on the Templates page.</summary>
public sealed class TemplateItemViewModel(MetadataTemplate template)
{
    public MetadataTemplate Template { get; } = template;

    public string Name => Template.Name;

    /// <summary>e.g. "Keywords (added): London; Big Ben · Copyright: © Dave".</summary>
    public string Summary => string.Join(" · ", Template.Fields.Select(f =>
        $"{TagCatalog.FindCommonField(f.Key)?.DisplayName ?? f.Key.ToString()}{(f.AddToList ? " (added)" : string.Empty)}: {TagCatalog.JoinList(f.Values)}"));
}

/// <summary>Rename, delete, import and export templates. New ones are made from a photo in the Library.</summary>
public sealed partial class TemplatesViewModel : ObservableObject
{
    private const string FileExtension = ".json";

    private readonly ITemplateStore _store;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _files;

    public TemplatesViewModel(ITemplateStore store, IDialogService dialogs, IFilePicker files)
    {
        _store = store;
        _dialogs = dialogs;
        _files = files;
        Refresh();
    }

    public ObservableCollection<TemplateItemViewModel> Templates { get; } = [];

    public bool IsEmpty => Templates.Count == 0;

    public void Refresh()
    {
        Templates.Clear();
        foreach (var template in _store.Load())
        {
            Templates.Add(new TemplateItemViewModel(template));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task RenameAsync(TemplateItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var name = (await _dialogs.PromptAsync("Rename template", "Template name", item.Name))?.Trim();
        if (string.IsNullOrEmpty(name) || name == item.Name)
        {
            return;
        }

        if (Templates.Any(t => t != item && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            await _dialogs.ShowErrorAsync("Name in use", $"There's already a template called \"{name}\".");
            return;
        }

        Save(Templates.Select(t => t == item ? t.Template with { Name = name } : t.Template));
    }

    [RelayCommand]
    private async Task DeleteAsync(TemplateItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (await _dialogs.ConfirmAsync("Delete template?", $"\"{item.Name}\" will be deleted. Photos aren't affected.", "Delete"))
        {
            Save(Templates.Where(t => t != item).Select(t => t.Template));
        }
    }

    [RelayCommand]
    private async Task ExportAsync(TemplateItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (await _files.PickFileToSaveAsync(item.Name, "PictureIt template", FileExtension) is { } path)
        {
            try
            {
                _store.Export(item.Template, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await _dialogs.ShowErrorAsync("Couldn't export", ex.Message);
            }
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (await _files.PickFileToOpenAsync([FileExtension]) is not { } path)
        {
            return;
        }

        MetadataTemplate template;
        try
        {
            template = _store.Import(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Couldn't import", ex.Message);
            return;
        }

        // Keep both rather than overwrite: "Rights (2)".
        var name = template.Name;
        for (var i = 2; Templates.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++)
        {
            name = $"{template.Name} ({i})";
        }

        Save([.. Templates.Select(t => t.Template), template with { Name = name }]);
    }

    private void Save(IEnumerable<MetadataTemplate> templates)
    {
        _store.Save([.. templates]);
        Refresh();
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureIt.Core.Model;
using PictureIt.Core.Templates;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels.Tools;

namespace PictureIt.Presentation.ViewModels;

/// <summary>
/// The bulk tools for the photos loaded in the editor: templates, copy and paste, find and replace, shift dates and
/// remove metadata. Each one builds edits per photo and hands them to <see cref="BatchRunner"/>.
/// </summary>
public sealed partial class ToolsViewModel : ObservableObject
{
    private readonly MetadataEditorViewModel _editor;
    private readonly BatchRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IFolderPicker _folderPicker;
    private readonly ITemplateStore _templates;
    private ImageMetadata? _copied;

    public ToolsViewModel(
        MetadataEditorViewModel editor,
        BatchRunner runner,
        IDialogService dialogs,
        IFolderPicker folderPicker,
        ITemplateStore templates)
    {
        _editor = editor;
        _runner = runner;
        _dialogs = dialogs;
        _folderPicker = folderPicker;
        _templates = templates;
        Templates = [.. templates.Load()];
        editor.PropertyChanged += OnEditorPropertyChanged;
    }

    /// <summary>Saved templates, for the "Apply template" menu.</summary>
    public ObservableCollection<MetadataTemplate> Templates { get; }

    /// <summary>The photo whose metadata was copied, for display ("Paste from Big Ben.jpg").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteMetadataCommand))]
    public partial string? CopiedFrom { get; private set; }

    private bool HasPhotos => _editor.HasPhoto && !_editor.IsLoading;

    private bool HasOnePhoto => HasPhotos && _editor.PhotoCount == 1;

    private bool CanPaste => HasPhotos && _copied is not null;

    /// <summary>Re-reads saved templates (they may have been changed on the Templates page).</summary>
    public void RefreshTemplates()
    {
        Templates.Clear();
        foreach (var template in _templates.Load())
        {
            Templates.Add(template);
        }
    }

    [RelayCommand(CanExecute = nameof(HasPhotos))]
    private async Task ApplyTemplateAsync(MetadataTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (await _editor.ConfirmLeaveAsync())
        {
            await RunAsync($"Apply template \"{template.Name}\" to {PhotoCountText()}", _ => template.ToEdits());
        }
    }

    [RelayCommand(CanExecute = nameof(HasOnePhoto))]
    private async Task SaveAsTemplateAsync()
    {
        var photo = _editor.LoadedMetadata.Values.Single();
        var name = await _dialogs.PromptAsync("Save as template", "Template name", Path.GetFileNameWithoutExtension(photo.FilePath));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var template = MetadataTemplate.FromPhoto(name, photo);
        if (template.Fields.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Nothing to save", "This photo's common fields (title, description, keywords, rights, place) are empty.");
            return;
        }

        var existing = _templates.Load();
        if (existing.Any(t => t.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase))
            && !await _dialogs.ConfirmAsync("Replace template?", $"There's already a template called \"{template.Name}\".", "Replace"))
        {
            return;
        }

        _templates.Save([.. existing.Where(t => !t.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase)), template]);
        RefreshTemplates();
    }

    [RelayCommand(CanExecute = nameof(HasOnePhoto))]
    private void CopyMetadata()
    {
        _copied = _editor.LoadedMetadata.Values.Single();
        CopiedFrom = Path.GetFileName(_copied.FilePath);
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task PasteMetadataAsync() => RunToolAsync(new PasteMetadataToolViewModel(_editor.PhotoCount, _copied!));

    [RelayCommand(CanExecute = nameof(HasPhotos))]
    private Task FindReplaceAsync() => RunToolAsync(new FindReplaceToolViewModel(_editor.PhotoCount));

    [RelayCommand(CanExecute = nameof(HasPhotos))]
    private Task ShiftDatesAsync()
    {
        var reference = _editor.RequestedPaths.Select(p => _editor.LoadedMetadata.GetValueOrDefault(p)).FirstOrDefault(m => m is not null);
        return RunToolAsync(new ShiftDatesToolViewModel(_editor.PhotoCount, reference));
    }

    [RelayCommand(CanExecute = nameof(HasPhotos))]
    private async Task RemoveMetadataAsync()
    {
        var tool = new RemoveMetadataToolViewModel(_editor.PhotoCount, _folderPicker);
        if (!await _editor.ConfirmLeaveAsync() || !await _dialogs.ShowToolAsync(tool))
        {
            return;
        }

        if (!tool.SaveAsCopies)
        {
            await RunAsync(tool.Description, tool.EditsFor);
            return;
        }

        // Plan copies (same metadata, new paths) so the preview shows them; make them only once the user confirms.
        var copies = _editor.LoadedMetadata.Values
            .Select(m => (Source: m.FilePath, Copy: new ImageMetadata(UniquePath(tool.CopyFolder, Path.GetFileName(m.FilePath)), m.Tags)))
            .ToList();
        var editSet = EditSet.Create(tool.Description, copies.Select(c => new FileEdits(c.Copy.FilePath, tool.EditsFor(c.Copy))));
        var result = await _runner.RunAsync(
            editSet,
            copies.ToDictionary(c => c.Copy.FilePath, c => c.Copy, StringComparer.OrdinalIgnoreCase),
            async cancellationToken =>
            {
                Directory.CreateDirectory(tool.CopyFolder);
                foreach (var (source, copy) in copies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Run(() => File.Copy(source, copy.FilePath), cancellationToken);
                }
            });

        if (result is { FailedCount: 0 })
        {
            await _dialogs.ShowInfoAsync("Copies saved", $"Cleaned copies of {PhotoCountText()} are in {tool.CopyFolder}. The originals weren't changed.");
        }
    }

    private async Task RunToolAsync(MetadataToolViewModel tool)
    {
        if (await _editor.ConfirmLeaveAsync() && await _dialogs.ShowToolAsync(tool))
        {
            await RunAsync(tool.Description, tool.EditsFor);
        }
    }

    private async Task RunAsync(string description, Func<ImageMetadata, IReadOnlyList<MetadataEdit>> editsFor)
    {
        var current = _editor.LoadedMetadata;
        EditSet editSet;
        try
        {
            editSet = EditSet.Create(description, current.Values.Select(m => new FileEdits(m.FilePath, editsFor(m))));
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            await _dialogs.ShowErrorAsync(description, ex.Message);
            return;
        }

        await _runner.RunAsync(editSet, current);
    }

    private string PhotoCountText() => _editor.PhotoCount == 1 ? "1 photo" : $"{_editor.PhotoCount} photos";

    /// <summary>"name.jpg", or "name (2).jpg" etc. if that's taken.</summary>
    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)} ({i}){Path.GetExtension(fileName)}");
        }

        return path;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MetadataEditorViewModel.HasPhoto) or nameof(MetadataEditorViewModel.IsLoading) or nameof(MetadataEditorViewModel.PhotoCount))
        {
            ApplyTemplateCommand.NotifyCanExecuteChanged();
            SaveAsTemplateCommand.NotifyCanExecuteChanged();
            CopyMetadataCommand.NotifyCanExecuteChanged();
            PasteMetadataCommand.NotifyCanExecuteChanged();
            FindReplaceCommand.NotifyCanExecuteChanged();
            ShiftDatesCommand.NotifyCanExecuteChanged();
            RemoveMetadataCommand.NotifyCanExecuteChanged();
        }
    }
}

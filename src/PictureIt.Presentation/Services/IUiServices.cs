using PictureIt.Core.Model;
using PictureIt.Core.Settings;
using PictureIt.Presentation.ViewModels.Tools;

namespace PictureIt.Presentation.Services;

public interface IFolderPicker
{
    /// <summary>Returns the chosen folder, or null if the user cancelled.</summary>
    Task<string?> PickFolderAsync();
}

public interface IFilePicker
{
    /// <summary>Returns the chosen file, or null if the user cancelled.</summary>
    Task<string?> PickFileToOpenAsync(string extension);

    Task<string?> PickFileToSaveAsync(string suggestedName, string extension);
}

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel,
}

public interface IDialogService
{
    /// <summary>Shows what will change and asks to go ahead.</summary>
    Task<bool> ConfirmChangesAsync(ChangePreview preview);

    Task<UnsavedChangesChoice> AskToSaveChangesAsync(string fileName);

    /// <summary>A yes/no question; returns true for <paramref name="confirmText"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    Task ShowErrorAsync(string title, string message);

    Task ShowInfoAsync(string title, string message);

    /// <summary>Lists the files a batch couldn't write, and why.</summary>
    Task ShowWriteFailuresAsync(WriteResult result);

    /// <summary>Asks for a line of text (e.g. a template name). Null if cancelled.</summary>
    Task<string?> PromptAsync(string title, string label, string initialText);

    /// <summary>Shows a tool's options (find &amp; replace, shift dates...). True if the user chose to go ahead.</summary>
    Task<bool> ShowToolAsync(ToolViewModel tool);

    /// <summary>Runs <paramref name="work"/> behind a progress dialog with a Cancel button.</summary>
    Task<T> RunWithProgressAsync<T>(string title, Func<IProgress<WriteProgress>, CancellationToken, Task<T>> work);
}

public interface IThemeService
{
    void Apply(AppTheme theme);
}

/// <summary>Sent after files are written or restored, so anything showing them can reload.</summary>
public sealed record FilesChangedMessage(IReadOnlyList<string> FilePaths);

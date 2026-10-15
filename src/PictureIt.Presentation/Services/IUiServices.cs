using PictureIt.Core.Model;
using PictureIt.Core.Settings;

namespace PictureIt.Presentation.Services;

public interface IFolderPicker
{
    /// <summary>Returns the chosen folder, or null if the user cancelled.</summary>
    Task<string?> PickFolderAsync();
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

    Task ShowErrorAsync(string title, string message);
}

public interface IThemeService
{
    void Apply(AppTheme theme);
}

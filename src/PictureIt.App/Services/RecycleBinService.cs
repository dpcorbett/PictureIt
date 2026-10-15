using System.Runtime.InteropServices;
using PictureIt.Core.Abstractions;

namespace PictureIt.App.Services;

/// <summary>
/// Sends files to the Recycle Bin with the shell's own delete. Where a file can't be recycled (e.g. on a network
/// drive), Windows asks before deleting it permanently instead of doing so silently.
/// </summary>
public sealed class RecycleBinService(WindowContext window) : IRecycleBin
{
    public Task<string?> RecycleAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            return Task.FromResult<string?>("The file no longer exists.");
        }

        var operation = new NativeMethods.ShFileOpStruct
        {
            Window = window.Handle,
            Function = NativeMethods.FoDelete,

            // The list of files ends with an extra null.
            From = filePath + "\0\0",
            Flags = NativeMethods.FofAllowUndo | NativeMethods.FofNoConfirmation | NativeMethods.FofNoErrorUi
                | NativeMethods.FofSilent | NativeMethods.FofWantNukeWarning,
        };

        var code = NativeMethods.SHFileOperation(ref operation);
        string? error = code != 0
            ? $"Windows couldn't move it to the Recycle Bin (error {code})."
            : operation.AnyOperationsAborted || File.Exists(filePath) ? "It wasn't deleted." : null;
        return Task.FromResult(error);
    }

    private static class NativeMethods
    {
        public const uint FoDelete = 0x0003;
        public const ushort FofSilent = 0x0004;
        public const ushort FofNoConfirmation = 0x0010;
        public const ushort FofAllowUndo = 0x0040;
        public const ushort FofNoErrorUi = 0x0400;
        public const ushort FofWantNukeWarning = 0x4000;

        [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int SHFileOperation(ref ShFileOpStruct operation);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ShFileOpStruct
        {
            public nint Window;
            public uint Function;
            public string From;
            public string? To;
            public ushort Flags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool AnyOperationsAborted;
            public nint NameMappings;
            public string? ProgressTitle;
        }
    }
}

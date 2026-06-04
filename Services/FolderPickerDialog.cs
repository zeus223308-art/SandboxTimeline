using System.Windows;
using WinForms = System.Windows.Forms;

namespace SandboxTimeline;

public static class FolderPickerDialog
{
    public static bool TryPickFolder(Window? owner, out string? selectedPath)
    {
        selectedPath = null;

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Sandbox Guard가 감시할 폴더를 선택하세요. (예: 다운로드 폴더)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var defaultDownloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        if (Directory.Exists(defaultDownloads))
        {
            dialog.InitialDirectory = defaultDownloads;
        }

        var result = owner == null
            ? dialog.ShowDialog()
            : ShowDialogWithOwner(owner, dialog);

        if (result != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return false;
        }

        selectedPath = Path.GetFullPath(dialog.SelectedPath);
        return Directory.Exists(selectedPath);
    }

    private static WinForms.DialogResult ShowDialogWithOwner(Window owner, WinForms.FolderBrowserDialog dialog)
    {
        var helper = new System.Windows.Interop.WindowInteropHelper(owner);
        return dialog.ShowDialog(new Win32WindowWrapper(helper.Handle));
    }

    private sealed class Win32WindowWrapper : WinForms.IWin32Window
    {
        public Win32WindowWrapper(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }
    }
}

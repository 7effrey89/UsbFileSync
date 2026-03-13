using System.IO;
using System.Windows.Forms;

namespace UsbFileSync.App.Services;

public static class WindowsSourceLocationPickerService
{
    public static string? PickSourceLocation(string? initialPath, IFolderPickerService folderPickerService)
    {
        if (!OperatingSystem.IsWindows() || System.Windows.Application.Current is null)
        {
            return folderPickerService.PickFolder("Select the source drive or folder", initialPath);
        }

        using var form = new Form
        {
            Text = "Select source drive or folder",
            Width = 520,
            Height = 420,
            MinimizeBox = false,
            MaximizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
        };

        var instructions = new Label
        {
            Dock = DockStyle.Top,
            Height = 52,
            Text = "Choose a drive root directly for APFS/removable media, or use Browse Folder for a normal Windows folder source.",
            Padding = new Padding(12, 12, 12, 0),
        };

        var driveList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            Margin = new Padding(12),
        };

        var drives = DriveInfo.GetDrives()
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .Select(drive => new DriveSelectionOption(drive))
            .ToArray();
        driveList.Items.AddRange(drives);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 12, 12),
        };

        string? selectedPath = null;

        var cancelButton = new Button { Text = "Cancel", AutoSize = true };
        cancelButton.Click += (_, _) => form.DialogResult = DialogResult.Cancel;

        var browseFolderButton = new Button { Text = "Browse Folder...", AutoSize = true };
        browseFolderButton.Click += (_, _) =>
        {
            var folderPath = folderPickerService.PickFolder("Select the source drive or folder", initialPath);
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                selectedPath = folderPath;
                form.DialogResult = DialogResult.OK;
            }
        };

        var useDriveButton = new Button { Text = "Use Selected Drive", AutoSize = true };
        void SelectCurrentDrive()
        {
            if (driveList.SelectedItem is DriveSelectionOption option)
            {
                selectedPath = option.RootPath;
                form.DialogResult = DialogResult.OK;
            }
        }

        useDriveButton.Click += (_, _) => SelectCurrentDrive();
        driveList.DoubleClick += (_, _) => SelectCurrentDrive();

        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(useDriveButton);
        buttonPanel.Controls.Add(browseFolderButton);

        form.Controls.Add(driveList);
        form.Controls.Add(buttonPanel);
        form.Controls.Add(instructions);

        if (driveList.Items.Count > 0)
        {
            var normalizedInitialRoot = string.IsNullOrWhiteSpace(initialPath)
                ? string.Empty
                : Path.GetPathRoot(Path.GetFullPath(initialPath)) ?? string.Empty;
            var initialSelection = drives.FirstOrDefault(option => string.Equals(option.RootPath, normalizedInitialRoot, StringComparison.OrdinalIgnoreCase));
            driveList.SelectedItem = initialSelection ?? drives[0];
        }

        return form.ShowDialog() == DialogResult.OK ? selectedPath : null;
    }

    private sealed class DriveSelectionOption(DriveInfo drive)
    {
        public string RootPath { get; } = drive.Name;

        public override string ToString()
        {
            if (drive.IsReady)
            {
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Drive" : drive.VolumeLabel;
                return $"{label} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)}) - {drive.DriveType}";
            }

            return $"{drive.Name.TrimEnd(Path.DirectorySeparatorChar)} - {drive.DriveType} (not ready or unreadable)";
        }
    }
}
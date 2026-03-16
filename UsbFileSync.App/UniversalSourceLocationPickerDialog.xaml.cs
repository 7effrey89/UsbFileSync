using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Volumes;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.App;

public partial class UniversalSourceLocationPickerDialog : Window
{
    private static readonly ImageSource GoogleDriveRootIcon = CreateGoogleDriveIcon();
    private static readonly ImageSource OneDriveRootIcon = CreateOneDriveIcon();
    private static readonly ImageSource DropboxRootIcon = CreateDropboxIcon();

    private static readonly DialogTextOptions DefaultTextOptions = new(
        WindowTitle: "Select Folder",
        Heading: "Browse folders across supported volumes",
        Description: "Select a root on the left, browse folders on the right, and choose the current folder.",
        NoRootsMessage: "No supported volumes are currently available.",
        InvalidPathMessage: "Enter a valid folder path under one of the available roots.",
        InvalidPathTitle: "Invalid folder",
        FolderNotFoundMessage: "That folder was not found on the selected volume.",
        FolderNotFoundTitle: "Folder not found");

    private readonly List<RootOption> _roots;
    private readonly List<RootListItem> _rootItems;
    private readonly IFileIconProvider _iconProvider;
    private readonly DialogTextOptions _textOptions;
    private RootOption? _currentRoot;
    private string _currentRelativePath = string.Empty;

    public UniversalSourceLocationPickerDialog(IEnumerable<RootOption> roots, string? initialPath = null, DialogTextOptions? textOptions = null)
    {
        InitializeComponent();
        _iconProvider = ShellFileIconProvider.Instance;
        _textOptions = textOptions ?? DefaultTextOptions;
        _roots = roots
            .OrderBy(root => LooksLikeCustomRoot(root.RootPath) ? $"0|{root.DisplayText}" : $"1|{root.RootPath}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        _rootItems = _roots.Select(root => new RootListItem(root, GetRootIcon(root))).ToList();
        RootsListBox.ItemsSource = _rootItems;
        Title = _textOptions.WindowTitle;
        DialogHeadingTextBlock.Text = _textOptions.Heading;
        DialogDescriptionTextBlock.Text = _textOptions.Description;

        Loaded += (_, _) => InitializeSelection(initialPath);
    }

    public string SelectedPath { get; private set; } = string.Empty;

    private void InitializeSelection(string? initialPath)
    {
        if (_roots.Count == 0)
        {
            FolderStatusTextBlock.Text = _textOptions.NoRootsMessage;
            return;
        }

        var initialRoot = FindInitialRoot(initialPath, out var initialRelativePath);
        var selectedRootPath = SelectInitialRootPath(_roots.Select(root => root.RootPath), initialRoot?.RootPath);
        var selectedRootItem = _rootItems.FirstOrDefault(item => string.Equals(item.Root.RootPath, selectedRootPath, StringComparison.OrdinalIgnoreCase)) ?? _rootItems[0];
        RootsListBox.SelectedItem = selectedRootItem;

        if (selectedRootItem is not null)
        {
            TryNavigateTo(selectedRootItem.Root, initialRelativePath, showMessageBox: false);
        }
    }

    private RootOption? FindInitialRoot(string? initialPath, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(initialPath))
        {
            return null;
        }

        try
        {
            var customRoot = FindCustomRoot(initialPath, out relativePath);
            if (customRoot is not null)
            {
                return customRoot;
            }

            var fullPath = Path.GetFullPath(initialPath);
            var rootPath = Path.GetPathRoot(fullPath) ?? string.Empty;
            var root = _roots.FirstOrDefault(candidate => string.Equals(candidate.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
            if (root is null)
            {
                return null;
            }

            relativePath = fullPath.Length <= rootPath.Length
                ? string.Empty
                : fullPath[rootPath.Length..].Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/').Trim('/');
            return root;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnRootSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RootsListBox.SelectedItem is RootListItem rootItem)
        {
            TryNavigateTo(rootItem.Root, string.Empty, showMessageBox: true);
        }
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        if (FoldersListBox.SelectedItem is BrowserEntry entry)
        {
            TryNavigateTo(_currentRoot, CombineRelativePath(_currentRelativePath, entry.RelativePath), showMessageBox: true);
        }
    }

    private void OnFolderDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FoldersListBox.SelectedItem is BrowserEntry entry)
        {
            TryNavigateTo(_currentRoot, CombineRelativePath(_currentRelativePath, entry.RelativePath), showMessageBox: true);
        }
    }

    private void OnUpClicked(object sender, RoutedEventArgs e)
    {
        if (_currentRoot is null)
        {
            return;
        }

        var parentRelativePath = GetParentRelativePath(_currentRelativePath);
        TryNavigateTo(_currentRoot, parentRelativePath, showMessageBox: true);
    }

    private void OnSelectCurrentFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_currentRoot is null)
        {
            return;
        }

        var scopedVolume = CreateScopedVolume(_currentRoot.Volume, _currentRelativePath);
        SelectedPath = scopedVolume.Root;
        DialogResult = true;
    }

    private void OnNavigateToPathClicked(object sender, RoutedEventArgs e) => NavigateToTypedPath();

    private void OnNewFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_currentRoot is null)
        {
            return;
        }

        var scopedVolume = CreateScopedVolume(_currentRoot.Volume, _currentRelativePath);
        if (scopedVolume.IsReadOnly)
        {
            System.Windows.MessageBox.Show(
                this,
                "The current location is read-only, so a folder cannot be created here.",
                "Folder creation unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new NewFolderDialog
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!TryNormalizeNewFolderName(dialog.FolderName, out var normalizedFolderName, out var errorMessage))
        {
            System.Windows.MessageBox.Show(
                this,
                errorMessage,
                "Invalid folder name",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var childRelativePath = CombineRelativePath(_currentRelativePath, normalizedFolderName);
        try
        {
            _currentRoot.Volume.CreateDirectory(childRelativePath);
            NavigateTo(_currentRoot, _currentRelativePath);
            SelectFolder(normalizedFolderName);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Could not create the folder.\n\n{exception.Message}",
                "Folder creation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCurrentFolderTextBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateToTypedPath();
            e.Handled = true;
        }
    }

    private void NavigateTo(RootOption? root, string? relativePath)
    {
        if (root is null)
        {
            return;
        }

        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var scopedVolume = CreateScopedVolume(root.Volume, normalizedRelativePath);
        _currentRoot = root;
        _currentRelativePath = normalizedRelativePath;
        CurrentFolderTextBox.Text = scopedVolume.Root;
        RenderBreadcrumbs(root, normalizedRelativePath);

        var entries = scopedVolume.Enumerate(string.Empty)
            .Where(entry => entry.Exists)
            .OrderBy(entry => entry.IsDirectory ? 0 : 1)
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var folders = entries
            .Where(entry => entry.IsDirectory)
            .Select(entry => new BrowserEntry(
                entry.Name,
                entry.Name,
                string.Empty,
                _iconProvider.GetIcon(entry.FullPath, isDirectory: true),
                "\uE8B7"))
            .ToList();

        var files = entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => new BrowserEntry(
                entry.Name,
                entry.Name,
                FormatFileSize(entry.Size),
                _iconProvider.GetIcon(entry.FullPath, isDirectory: false),
                "\uE8A5"))
            .ToList();

        FoldersListBox.ItemsSource = folders;
        FilesListBox.ItemsSource = files;
        UpdateNewFolderButtonState(scopedVolume);
        FolderStatusTextBlock.Text = $"{folders.Count} folder{(folders.Count == 1 ? string.Empty : "s")}, {files.Count} file{(files.Count == 1 ? string.Empty : "s")}. Double-click a folder to open it.";
    }

    internal static bool TryNormalizeNewFolderName(string? folderName, out string normalizedFolderName, out string errorMessage)
    {
        normalizedFolderName = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(folderName))
        {
            errorMessage = "Enter a folder name.";
            return false;
        }

        normalizedFolderName = folderName.Trim();
        if (normalizedFolderName is "." or "..")
        {
            errorMessage = "The folder name cannot be '.' or '..'.";
            return false;
        }

        if (normalizedFolderName.Contains('/') || normalizedFolderName.Contains('\\'))
        {
            errorMessage = "The folder name cannot contain path separators.";
            return false;
        }

        if (normalizedFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errorMessage = "The folder name contains invalid characters.";
            return false;
        }

        return true;
    }

    private void NavigateToTypedPath()
    {
        if (!TryResolveDialogPath(_roots.Select(root => root.RootPath), CurrentFolderTextBox.Text, out var rootPath, out var relativePath))
        {
            System.Windows.MessageBox.Show(
                this,
                _textOptions.InvalidPathMessage,
                _textOptions.InvalidPathTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            CurrentFolderTextBox.Focus();
            CurrentFolderTextBox.SelectAll();
            return;
        }

        var root = _roots.FirstOrDefault(candidate => string.Equals(candidate.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return;
        }

        try
        {
            TryNavigateTo(root, relativePath, showMessageBox: true);
        }
        catch (DirectoryNotFoundException)
        {
            System.Windows.MessageBox.Show(
                this,
                _textOptions.FolderNotFoundMessage,
                _textOptions.FolderNotFoundTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            CurrentFolderTextBox.Focus();
            CurrentFolderTextBox.SelectAll();
        }
    }

    private void RenderBreadcrumbs(RootOption root, string relativePath)
    {
        BreadcrumbPanel.Children.Clear();
        var segments = BuildBreadcrumbSegments(root.RootPath, relativePath).ToList();
        if (segments.Count > 0)
        {
            segments[0] = new BreadcrumbSegment(root.DisplayText, string.Empty);
        }

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var button = new System.Windows.Controls.Button
            {
                Content = segment.DisplayText,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 3, 8, 3),
                MinHeight = 28,
                Tag = segment.RelativePath,
            };
            button.Click += OnBreadcrumbClicked;
            BreadcrumbPanel.Children.Add(button);

            if (index < segments.Count - 1)
            {
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = ">",
                    Margin = new Thickness(0, 4, 6, 0),
                    Foreground = System.Windows.Media.Brushes.DimGray,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }
    }

    private void OnBreadcrumbClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string relativePath })
        {
            TryNavigateTo(_currentRoot, relativePath, showMessageBox: true);
        }
    }

    internal static string SelectInitialRootPath(IEnumerable<string> rootPaths, string? initialRootPath)
    {
        ArgumentNullException.ThrowIfNull(rootPaths);

        var availableRootPaths = rootPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        if (availableRootPaths.Count == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(initialRootPath))
        {
            return initialRootPath;
        }

        return availableRootPaths.FirstOrDefault(path => !LooksLikeCustomRoot(path))
            ?? availableRootPaths[0];
    }

    private static IVolumeSource CreateScopedVolume(IVolumeSource rootVolume, string? relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        return string.IsNullOrEmpty(normalizedRelativePath)
            ? rootVolume
            : new SubdirectoryVolumeSource(rootVolume, normalizedRelativePath);
    }

    private static string CombineRelativePath(string parentRelativePath, string childRelativePath)
    {
        var normalizedParentRelativePath = NormalizeRelativePath(parentRelativePath);
        var normalizedChildRelativePath = NormalizeRelativePath(childRelativePath);
        return string.IsNullOrEmpty(normalizedParentRelativePath)
            ? normalizedChildRelativePath
            : $"{normalizedParentRelativePath}/{normalizedChildRelativePath}";
    }

    private static string GetParentRelativePath(string relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return string.Empty;
        }

        var separatorIndex = normalizedRelativePath.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : normalizedRelativePath[..separatorIndex];
    }

    internal static IReadOnlyList<BreadcrumbSegment> BuildBreadcrumbSegments(string rootPath, string? relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var segments = new List<BreadcrumbSegment>
        {
            new(GetRootDisplayText(rootPath), string.Empty),
        };

        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return segments;
        }

        var currentPath = string.Empty;
        foreach (var part in normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
            segments.Add(new BreadcrumbSegment(part, currentPath));
        }

        return segments;
    }

    internal static bool TryResolveDialogPath(IEnumerable<string> availableRoots, string? typedPath, out string rootPath, out string relativePath)
    {
        rootPath = string.Empty;
        relativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(typedPath))
        {
            return false;
        }

        foreach (var availableRoot in availableRoots.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            if (!LooksLikeCustomRoot(availableRoot))
            {
                continue;
            }

            if (string.Equals(typedPath, availableRoot, StringComparison.OrdinalIgnoreCase))
            {
                rootPath = availableRoot;
                relativePath = string.Empty;
                return true;
            }

            var prefix = availableRoot.TrimEnd('/', '\\') + "/";
            if (typedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rootPath = availableRoot;
                relativePath = NormalizeRelativePath(typedPath[prefix.Length..]);
                return true;
            }
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(typedPath);
        }
        catch (Exception)
        {
            return false;
        }

        rootPath = availableRoots
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate.EndsWith(Path.DirectorySeparatorChar) ? candidate : candidate + Path.DirectorySeparatorChar)
            .FirstOrDefault(candidate => fullPath.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        if (string.IsNullOrEmpty(rootPath))
        {
            return false;
        }

        relativePath = fullPath.Length <= rootPath.Length
            ? string.Empty
            : NormalizeRelativePath(fullPath[rootPath.Length..]);
        return true;
    }

    private static string GetRootDisplayText(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        if (GoogleDrivePath.IsGoogleDrivePath(rootPath))
        {
            return "Google Drive";
        }

        if (OneDrivePath.IsOneDrivePath(rootPath))
        {
            return "OneDrive";
        }

        if (DropboxPath.IsDropboxPath(rootPath))
        {
            return "Dropbox";
        }

        return rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static ImageSource? GetRootIcon(RootOption root) =>
        root.Volume.FileSystemType switch
        {
            "Google Drive" => GoogleDriveRootIcon,
            "OneDrive" => OneDriveRootIcon,
            "Dropbox" => DropboxRootIcon,
            _ => LooksLikeCustomRoot(root.RootPath)
                ? ShellFileIconProvider.Instance.GetIcon(root.DisplayText, isDirectory: true)
                : ShellFileIconProvider.Instance.GetDriveIcon(root.RootPath),
        };

    private RootOption? FindCustomRoot(string initialPath, out string relativePath)
    {
        relativePath = string.Empty;

        foreach (var root in _roots.Where(root => LooksLikeCustomRoot(root.RootPath)))
        {
            if (string.Equals(initialPath, root.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            var prefix = root.RootPath.TrimEnd('/', '\\') + "/";
            if (initialPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = NormalizeRelativePath(initialPath[prefix.Length..]);
                return root;
            }
        }

        return null;
    }

    private static bool LooksLikeCustomRoot(string rootPath) => rootPath.Contains("://", StringComparison.Ordinal);

    private static string FormatFileSize(long? size)
    {
        if (size is null)
        {
            return string.Empty;
        }

        const double kilobyte = 1024d;
        const double megabyte = 1024d * kilobyte;
        const double gigabyte = 1024d * megabyte;
        return size.Value switch
        {
            >= (long)gigabyte => $"{size.Value / gigabyte:0.0} GB",
            >= (long)megabyte => $"{size.Value / megabyte:0.0} MB",
            >= (long)kilobyte => $"{size.Value / kilobyte:0.0} KB",
            _ => $"{size.Value} B",
        };
    }

    private static string NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        return relativePath
            .Replace('\\', '/')
            .Trim('/');
    }

    private void TryNavigateTo(RootOption? root, string? relativePath, bool showMessageBox)
    {
        if (root is null)
        {
            return;
        }

        try
        {
            NavigateTo(root, relativePath);
        }
        catch (DirectoryNotFoundException)
        {
            FolderStatusTextBlock.Text = _textOptions.FolderNotFoundMessage;
            if (showMessageBox)
            {
                System.Windows.MessageBox.Show(
                    this,
                    _textOptions.FolderNotFoundMessage,
                    _textOptions.FolderNotFoundTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            FolderStatusTextBlock.Text = exception.Message;
            if (showMessageBox)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Could not browse that location.\n\n{exception.Message}",
                    "Browse failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void UpdateNewFolderButtonState(IVolumeSource scopedVolume)
    {
        NewFolderButton.IsEnabled = !scopedVolume.IsReadOnly;
        NewFolderButton.ToolTip = scopedVolume.IsReadOnly
            ? "This location is read-only."
            : "Create a folder in the current location.";
    }

    private void SelectFolder(string folderName)
    {
        if (FoldersListBox.ItemsSource is not IEnumerable<BrowserEntry> entries)
        {
            return;
        }

        var entry = entries.FirstOrDefault(candidate => string.Equals(candidate.DisplayText, folderName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return;
        }

        FoldersListBox.SelectedItem = entry;
        FoldersListBox.ScrollIntoView(entry);
    }

    public sealed record RootOption(string RootPath, string DisplayText, IVolumeSource Volume);

    public sealed record DialogTextOptions(
        string WindowTitle,
        string Heading,
        string Description,
        string NoRootsMessage,
        string InvalidPathMessage,
        string InvalidPathTitle,
        string FolderNotFoundMessage,
        string FolderNotFoundTitle);

    internal sealed record BreadcrumbSegment(string DisplayText, string RelativePath);

    private sealed class RootListItem(RootOption root, ImageSource? iconSource)
    {
        public RootOption Root { get; } = root;

        public string DisplayText { get; } = root.DisplayText;

        public ImageSource? IconSource { get; } = iconSource;
    }

    private sealed class BrowserEntry(string relativePath, string displayText, string secondaryText, ImageSource? iconSource, string iconGlyph)
    {
        public string RelativePath { get; } = relativePath;

        public string DisplayText { get; } = displayText;

        public string SecondaryText { get; } = secondaryText;

        public ImageSource? IconSource { get; } = iconSource;

        public string IconGlyph { get; } = iconGlyph;
    }

    private static ImageSource CreateGoogleDriveIcon()
    {
        var geometryGroup = new GeometryGroup();
        geometryGroup.Children.Add(Geometry.Parse("M8,1 L14,11 L10.8,16.5 L4.5,6 Z"));
        geometryGroup.Children.Add(Geometry.Parse("M8,1 L4.5,6 L1.2,11.7 L4.4,17.2 L7.7,11.6 L11,6 Z"));
        geometryGroup.Children.Add(Geometry.Parse("M14,11 L7.7,11.6 L4.4,17.2 L11,17.2 L14.2,11.6 Z"));

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x42, 0x85, 0xF4)), null, Geometry.Parse("M8,1 L14,11 L11,11.6 L4.5,6 Z")));
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x9D, 0x58)), null, Geometry.Parse("M4.5,6 L1.2,11.7 L4.4,17.2 L7.7,11.6 Z")));
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0xB4, 0x00)), null, Geometry.Parse("M14,11 L11,17.2 L4.4,17.2 L7.7,11.6 Z")));
        drawingGroup.Freeze();
        return new DrawingImage(drawingGroup);
    }

    private static ImageSource CreateOneDriveIcon()
    {
        var drawingGroup = new DrawingGroup();
        var cloudGeometry = Geometry.Parse("M4,13 C4.3,10.5 6.3,8.7 8.8,8.7 C9.6,6.7 11.5,5.4 13.7,5.4 C16.8,5.4 19.3,7.8 19.5,10.9 C21.1,11.2 22.3,12.6 22.3,14.3 C22.3,16.4 20.6,18 18.5,18 L7.2,18 C4.9,18 3,16.2 3,13.9 C3,13.6 3.1,13.3 3.1,13 Z");
        drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)), null, cloudGeometry));
        drawingGroup.Freeze();
        return new DrawingImage(drawingGroup);
    }

    private static ImageSource CreateDropboxIcon()
    {
        var blue = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x71, 0xE3));
        blue.Freeze();

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(blue, null, Geometry.Parse("M4,3 L8,5.6 L4,8.2 L0,5.6 Z")));
        drawingGroup.Children.Add(new GeometryDrawing(blue, null, Geometry.Parse("M12,3 L16,5.6 L12,8.2 L8,5.6 Z")));
        drawingGroup.Children.Add(new GeometryDrawing(blue, null, Geometry.Parse("M4,10 L8,12.6 L4,15.2 L0,12.6 Z")));
        drawingGroup.Children.Add(new GeometryDrawing(blue, null, Geometry.Parse("M12,10 L16,12.6 L12,15.2 L8,12.6 Z")));
        drawingGroup.Children.Add(new GeometryDrawing(blue, null, Geometry.Parse("M8,16.6 L12,19.2 L8,21.8 L4,19.2 Z")));
        drawingGroup.Freeze();
        return new DrawingImage(drawingGroup);
    }
}
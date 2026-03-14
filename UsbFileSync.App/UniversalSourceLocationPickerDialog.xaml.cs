using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.App;

public partial class UniversalSourceLocationPickerDialog : Window
{
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
        _roots = roots.OrderBy(root => root.RootPath, StringComparer.OrdinalIgnoreCase).ToList();
        _rootItems = _roots.Select(root => new RootListItem(root, ShellFileIconProvider.Instance.GetDriveIcon(root.RootPath))).ToList();
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
        var selectedRootItem = _rootItems.FirstOrDefault(item => item.Root == initialRoot) ?? _rootItems[0];
        RootsListBox.SelectedItem = selectedRootItem;

        if (selectedRootItem is not null)
        {
            NavigateTo(selectedRootItem.Root, initialRelativePath);
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
            NavigateTo(rootItem.Root, string.Empty);
        }
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        if (FoldersListBox.SelectedItem is BrowserEntry entry)
        {
            NavigateTo(_currentRoot, CombineRelativePath(_currentRelativePath, entry.RelativePath));
        }
    }

    private void OnFolderDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FoldersListBox.SelectedItem is BrowserEntry entry)
        {
            NavigateTo(_currentRoot, CombineRelativePath(_currentRelativePath, entry.RelativePath));
        }
    }

    private void OnUpClicked(object sender, RoutedEventArgs e)
    {
        if (_currentRoot is null)
        {
            return;
        }

        var parentRelativePath = GetParentRelativePath(_currentRelativePath);
        NavigateTo(_currentRoot, parentRelativePath);
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
        FolderStatusTextBlock.Text = $"{folders.Count} folder{(folders.Count == 1 ? string.Empty : "s")}, {files.Count} file{(files.Count == 1 ? string.Empty : "s")}. Double-click a folder to open it.";
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
            NavigateTo(root, relativePath);
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
        var segments = BuildBreadcrumbSegments(root.RootPath, relativePath);
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
            NavigateTo(_currentRoot, relativePath);
        }
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

        return rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

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
}
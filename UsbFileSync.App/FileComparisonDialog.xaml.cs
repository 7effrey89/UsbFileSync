using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;
using UsbFileSync.App.Controls;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;

namespace UsbFileSync.App;

public partial class FileComparisonDialog : Window
{
    private const double DefaultImageZoom = 1d;
    private const double ImageZoomStep = 1.25d;
    private const double MaxImageZoom = 6d;
    private const int PreviewCharacterLimit = 8000;

    private readonly IFileLauncherService _fileLauncherService = new WindowsFileLauncherService();
    private readonly IShellPreviewHandlerResolver _previewHandlerResolver = new WindowsShellPreviewHandlerResolver();
    private readonly string _sourcePath;
    private readonly string _sourceSize;
    private readonly string _sourceModified;
    private readonly string _destinationPath;
    private readonly string _destinationSize;
    private readonly string _destinationModified;
    private readonly IReadOnlyDictionary<string, string>? _previewProviderMappings;
    private readonly bool _showDestinationPane;
    private readonly string? _dialogTitle;
    private readonly string? _headerText;
    private FileComparisonDialogViewModel? _viewModel;
    private double _sourceImageZoom = DefaultImageZoom;
    private double _destinationImageZoom = DefaultImageZoom;
    private static readonly System.Windows.Input.Cursor ZoomInCursor = PreviewCursorFactory.CreateZoomInCursor();
    private static readonly System.Windows.Input.Cursor ZoomOutCursor = PreviewCursorFactory.CreateZoomOutCursor();

    public FileComparisonDialog(
        SyncPreviewRowViewModel row,
        IReadOnlyDictionary<string, string>? previewProviderMappings = null,
        bool showDestinationPane = true,
        string? dialogTitle = null,
        string? headerText = null)
    {
        InitializeComponent();
        _sourcePath = row.SourcePath;
        _sourceSize = row.SourceSize;
        _sourceModified = row.SourceModified;
        _destinationPath = row.DestinationPath;
        _destinationSize = row.DestinationSize;
        _destinationModified = row.DestinationModified;
        _showDestinationPane = showDestinationPane;
        _dialogTitle = dialogTitle;
        _headerText = headerText;
        _previewProviderMappings = previewProviderMappings is null
            ? null
            : new Dictionary<string, string>(previewProviderMappings, StringComparer.OrdinalIgnoreCase);
        Loaded += OnLoadedAsync;
        Closed += OnClosed;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            _viewModel = await Task.Run(() => new FileComparisonDialogViewModel(
                _sourcePath,
                _sourceSize,
                _sourceModified,
                _destinationPath,
                _destinationSize,
                _destinationModified,
                _previewProviderMappings,
                _showDestinationPane,
                _dialogTitle,
                _headerText));

            ApplyDialogMode(_viewModel);

            PopulatePane(
                _viewModel.SourcePane,
                SourceSideLabelTextBlock,
                SourceFileNameTextBlock,
                SourceMetadataGrid,
                SourceSizeTextBlock,
                SourceDateTextBlock,
                SourcePreviewBorder,
                SourceTextPreviewViewer,
                SourcePreviewTextBlock,
                SourceImagePreviewViewer,
                SourcePreviewImage,
                SourceShellPreviewHost,
                SourcePdfPreview,
                SourceMediaPreviewGrid,
                SourceMediaElement,
                SourceEmptyStateTextBlock,
                SourcePathPanel,
                SourcePathButton,
                SourcePreviewModeComboBox);
            PopulatePane(
                _viewModel.DestinationPane,
                DestinationSideLabelTextBlock,
                DestinationFileNameTextBlock,
                DestinationMetadataGrid,
                DestinationSizeTextBlock,
                DestinationDateTextBlock,
                DestinationPreviewBorder,
                DestinationTextPreviewViewer,
                DestinationPreviewTextBlock,
                DestinationImagePreviewViewer,
                DestinationPreviewImage,
                DestinationShellPreviewHost,
                DestinationPdfPreview,
                DestinationMediaPreviewGrid,
                DestinationMediaElement,
                DestinationEmptyStateTextBlock,
                DestinationPathPanel,
                DestinationPathButton,
                DestinationPreviewModeComboBox);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Could not load the comparison preview.\n\n{exception.Message}",
                "Comparison unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Close();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyDialogMode(FileComparisonDialogViewModel viewModel)
    {
        Title = viewModel.DialogTitle;
        DialogHeaderTextBlock.Text = viewModel.HeaderText;

        if (viewModel.ShowDestinationPane)
        {
            ComparisonSplitterColumnDefinition.Width = new GridLength(16);
            DestinationColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
            DestinationPaneBorder.Visibility = Visibility.Visible;
            MinWidth = 900;
            return;
        }

        ComparisonSplitterColumnDefinition.Width = new GridLength(0);
        DestinationColumnDefinition.Width = new GridLength(0);
        DestinationPaneBorder.Visibility = Visibility.Collapsed;
        MinWidth = 620;
        Width = Math.Min(Width, 760);
    }

    private void OnOpenPathClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string path } && !string.IsNullOrWhiteSpace(path))
        {
            _fileLauncherService.OpenItem(path);
        }
    }

    private void PopulatePane(
        FileComparisonPaneViewModel pane,
        System.Windows.Controls.TextBlock sideLabelTextBlock,
        System.Windows.Controls.TextBlock fileNameTextBlock,
        System.Windows.Controls.Grid metadataGrid,
        System.Windows.Controls.TextBlock sizeTextBlock,
        System.Windows.Controls.TextBlock dateTextBlock,
        System.Windows.Controls.Border previewBorder,
        System.Windows.Controls.ScrollViewer textPreviewViewer,
        System.Windows.Controls.TextBlock previewTextBlock,
        System.Windows.Controls.ScrollViewer imagePreviewViewer,
        System.Windows.Controls.Image previewImage,
        ShellPreviewHost shellPreviewHost,
        WebView2 pdfPreview,
        System.Windows.Controls.Grid mediaPreviewGrid,
        System.Windows.Controls.MediaElement mediaElement,
        System.Windows.Controls.TextBlock emptyStateTextBlock,
        System.Windows.Controls.StackPanel pathPanel,
        System.Windows.Controls.Button pathButton,
        System.Windows.Controls.ComboBox? previewModeComboBox = null)
    {
        sideLabelTextBlock.Text = pane.SideLabel;
        fileNameTextBlock.Text = pane.FileName;
        sizeTextBlock.Text = $"Size: {pane.SizeText}";
        dateTextBlock.Text = $"Date: {pane.ModifiedText}";

        if (!pane.HasFile)
        {
            fileNameTextBlock.Visibility = Visibility.Collapsed;
            metadataGrid.Visibility = Visibility.Collapsed;
            previewBorder.Visibility = Visibility.Visible;
            pathPanel.Visibility = Visibility.Collapsed;
            emptyStateTextBlock.Visibility = Visibility.Collapsed;
            imagePreviewViewer.Visibility = Visibility.Collapsed;
            imagePreviewViewer.ScrollToHorizontalOffset(0);
            imagePreviewViewer.ScrollToVerticalOffset(0);
            imagePreviewViewer.Cursor = ZoomInCursor;
            shellPreviewHost.Visibility = Visibility.Collapsed;
            shellPreviewHost.ClearPreview();
            textPreviewViewer.Visibility = Visibility.Visible;
            pdfPreview.Visibility = Visibility.Collapsed;
            mediaPreviewGrid.Visibility = Visibility.Collapsed;
            mediaElement.Source = null;
            pdfPreview.Source = null;
            previewImage.Source = null;
            previewTextBlock.Text = pane.PreviewText;
            previewTextBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            previewTextBlock.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            previewTextBlock.TextAlignment = System.Windows.TextAlignment.Center;
            return;
        }

        fileNameTextBlock.Visibility = Visibility.Visible;
        metadataGrid.Visibility = Visibility.Visible;
        previewBorder.Visibility = Visibility.Visible;
        pathPanel.Visibility = Visibility.Visible;
        emptyStateTextBlock.Visibility = Visibility.Collapsed;
        pdfPreview.Visibility = Visibility.Collapsed;
        pdfPreview.Source = null;
        mediaPreviewGrid.Visibility = Visibility.Collapsed;
        mediaElement.Stop();
        mediaElement.Source = null;
        shellPreviewHost.Visibility = Visibility.Collapsed;
        shellPreviewHost.ClearPreview();

        if (pane.HasImagePreview)
        {
            textPreviewViewer.Visibility = Visibility.Collapsed;
            imagePreviewViewer.Visibility = Visibility.Visible;
            previewImage.Source = pane.Preview.ImageSource;
            previewImage.Width = 0;
            previewImage.Height = 0;
            previewTextBlock.Text = string.Empty;
            ResetImageZoom(imagePreviewViewer);
        }
        else if (pane.HasShellPreview)
        {
            textPreviewViewer.Visibility = Visibility.Collapsed;
            imagePreviewViewer.Visibility = Visibility.Collapsed;
            previewImage.Source = null;
            previewTextBlock.Text = string.Empty;

            if (shellPreviewHost.TryLoadPreview(pane.Preview.FilePath, out var errorMessage))
            {
                shellPreviewHost.Visibility = Visibility.Visible;
            }
            else
            {
                textPreviewViewer.Visibility = Visibility.Visible;
                previewTextBlock.Text = !string.IsNullOrWhiteSpace(pane.PreviewText)
                    ? pane.PreviewText
                    : $"Windows preview handler could not be loaded.{Environment.NewLine}{Environment.NewLine}{errorMessage}";
                previewTextBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                previewTextBlock.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                previewTextBlock.TextAlignment = System.Windows.TextAlignment.Left;
            }
        }
        else if (pane.HasPdfPreview)
        {
            textPreviewViewer.Visibility = Visibility.Collapsed;
            imagePreviewViewer.Visibility = Visibility.Collapsed;
            pdfPreview.Visibility = Visibility.Visible;
            previewImage.Source = null;
            previewTextBlock.Text = string.Empty;
            pdfPreview.Source = new Uri(pane.Preview.FilePath, UriKind.Absolute);
        }
        else if (pane.HasMediaPreview)
        {
            textPreviewViewer.Visibility = Visibility.Collapsed;
            imagePreviewViewer.Visibility = Visibility.Collapsed;
            mediaPreviewGrid.Visibility = Visibility.Visible;
            previewImage.Source = null;
            previewTextBlock.Text = string.Empty;
            mediaElement.Source = new Uri(pane.Preview.FilePath, UriKind.Absolute);
        }
        else
        {
            imagePreviewViewer.Visibility = Visibility.Collapsed;
            textPreviewViewer.Visibility = Visibility.Visible;
            previewTextBlock.Text = pane.PreviewText;
            var isUnsupportedPreview = string.Equals(pane.PreviewText, "Preview for item type not supported", StringComparison.Ordinal);
            previewTextBlock.HorizontalAlignment = isUnsupportedPreview ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left;
            previewTextBlock.VerticalAlignment = isUnsupportedPreview ? System.Windows.VerticalAlignment.Center : System.Windows.VerticalAlignment.Top;
            previewTextBlock.TextAlignment = isUnsupportedPreview ? System.Windows.TextAlignment.Center : System.Windows.TextAlignment.Left;
            previewImage.Source = null;
            imagePreviewViewer.Cursor = ZoomInCursor;
        }

        pathButton.Content = pane.HasPath ? pane.FullPath : "Not available";
        pathButton.Tag = pane.FullPath;
        pathButton.IsEnabled = pane.HasPath;

        if (previewModeComboBox is not null)
        {
            var previewModePanel = previewModeComboBox.Parent as System.Windows.Controls.StackPanel;
            previewModeComboBox.Items.Clear();

            if (pane.IsOfficeFile)
            {
                if (!string.IsNullOrWhiteSpace(pane.FullPath)
                    && _previewHandlerResolver.TryGetPreviewHandlerClsid(pane.FullPath, out _))
                {
                    previewModeComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Shell Preview", Tag = OfficePreviewMode.Shell });
                }
                previewModeComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Open XML", Tag = OfficePreviewMode.OpenXml });
                previewModeComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Office Interop", Tag = OfficePreviewMode.OfficeInterop });
                if (previewModePanel is not null) previewModePanel.Visibility = Visibility.Visible;
                previewModeComboBox.SelectedIndex = 0;
            }
            else if (pane.HasFile && pane.Preview.Kind is not (FilePreviewKind.None or FilePreviewKind.Unsupported))
            {
                var builtInLabel = pane.Preview.Kind switch
                {
                    FilePreviewKind.Text => "Text Viewer",
                    FilePreviewKind.Image => "Image Viewer",
                    FilePreviewKind.Pdf => "PDF Viewer",
                    FilePreviewKind.Media => "Media Player",
                    FilePreviewKind.Shell => "Shell Preview",
                    _ => "Built-in",
                };
                previewModeComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = builtInLabel, Tag = "BuiltIn" });
                if (pane.Preview.Kind != FilePreviewKind.Shell
                    && !string.IsNullOrWhiteSpace(pane.FullPath)
                    && _previewHandlerResolver.TryGetPreviewHandlerClsid(pane.FullPath, out _))
                {
                    previewModeComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Shell Preview", Tag = "Shell" });
                }
                if (previewModePanel is not null) previewModePanel.Visibility = Visibility.Visible;
                previewModeComboBox.SelectedIndex = 0;
            }
            else
            {
                if (previewModePanel is not null) previewModePanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void OnPreviewModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox comboBox || _viewModel is null)
        {
            return;
        }

        if (comboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem selectedItem)
        {
            return;
        }

        var isSource = Equals(comboBox.Tag, "Source");
        var pane = isSource ? _viewModel.SourcePane : _viewModel.DestinationPane;
        if (!pane.HasFile || string.IsNullOrWhiteSpace(pane.FullPath))
        {
            return;
        }

        var textPreviewViewer = isSource ? SourceTextPreviewViewer : DestinationTextPreviewViewer;
        var previewTextBlock = isSource ? SourcePreviewTextBlock : DestinationPreviewTextBlock;
        var shellPreviewHost = isSource ? SourceShellPreviewHost : DestinationShellPreviewHost;
        var imagePreviewViewer = isSource ? SourceImagePreviewViewer : DestinationImagePreviewViewer;
        var previewImage = isSource ? SourcePreviewImage : DestinationPreviewImage;
        var pdfPreview = isSource ? SourcePdfPreview : DestinationPdfPreview;
        var mediaPreviewGrid = isSource ? SourceMediaPreviewGrid : DestinationMediaPreviewGrid;
        var mediaElement = isSource ? SourceMediaElement : DestinationMediaElement;

        // Handle string tags for non-Office files: "BuiltIn" or "Shell"
        if (selectedItem.Tag is string tagString)
        {
            // Hide all viewers first
            textPreviewViewer.Visibility = Visibility.Collapsed;
            imagePreviewViewer.Visibility = Visibility.Collapsed;
            shellPreviewHost.Visibility = Visibility.Collapsed;
            shellPreviewHost.ClearPreview();
            pdfPreview.Visibility = Visibility.Collapsed;
            mediaPreviewGrid.Visibility = Visibility.Collapsed;
            mediaElement.Stop();
            mediaElement.Source = null;
            pdfPreview.Source = null;
            previewImage.Source = null;
            previewTextBlock.Text = string.Empty;

            if (tagString == "Shell")
            {
                if (shellPreviewHost.TryLoadPreview(pane.FullPath, out var errorMessage))
                {
                    shellPreviewHost.Visibility = Visibility.Visible;
                }
                else
                {
                    textPreviewViewer.Visibility = Visibility.Visible;
                    previewTextBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    previewTextBlock.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                    previewTextBlock.TextAlignment = System.Windows.TextAlignment.Left;
                    previewTextBlock.Text = $"Windows preview handler could not be loaded.{Environment.NewLine}{Environment.NewLine}{errorMessage}";
                }
            }
            else // "BuiltIn" — restore the original built-in preview
            {
                if (pane.HasImagePreview)
                {
                    imagePreviewViewer.Visibility = Visibility.Visible;
                    previewImage.Source = pane.Preview.ImageSource;
                    previewImage.Width = 0;
                    previewImage.Height = 0;
                    ResetImageZoom(imagePreviewViewer);
                }
                else if (pane.HasShellPreview)
                {
                    if (shellPreviewHost.TryLoadPreview(pane.Preview.FilePath, out var errorMessage))
                    {
                        shellPreviewHost.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        textPreviewViewer.Visibility = Visibility.Visible;
                        previewTextBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                        previewTextBlock.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                        previewTextBlock.TextAlignment = System.Windows.TextAlignment.Left;
                        previewTextBlock.Text = !string.IsNullOrWhiteSpace(pane.PreviewText)
                            ? pane.PreviewText
                            : $"Windows preview handler could not be loaded.{Environment.NewLine}{Environment.NewLine}{errorMessage}";
                    }
                }
                else if (pane.HasPdfPreview)
                {
                    pdfPreview.Visibility = Visibility.Visible;
                    pdfPreview.Source = new Uri(pane.Preview.FilePath, UriKind.Absolute);
                }
                else if (pane.HasMediaPreview)
                {
                    mediaPreviewGrid.Visibility = Visibility.Visible;
                    mediaElement.Source = new Uri(pane.Preview.FilePath, UriKind.Absolute);
                }
                else
                {
                    textPreviewViewer.Visibility = Visibility.Visible;
                    previewTextBlock.Text = pane.PreviewText;
                    previewTextBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    previewTextBlock.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                    previewTextBlock.TextAlignment = System.Windows.TextAlignment.Left;
                }
            }

            return;
        }

        // Handle OfficePreviewMode tags for Office files
        if (selectedItem.Tag is not OfficePreviewMode mode)
        {
            return;
        }

        if (!pane.IsOfficeFile)
        {
            return;
        }

        shellPreviewHost.Visibility = Visibility.Collapsed;
        shellPreviewHost.ClearPreview();
        imagePreviewViewer.Visibility = Visibility.Collapsed;
        pdfPreview.Visibility = Visibility.Collapsed;
        mediaPreviewGrid.Visibility = Visibility.Collapsed;
        textPreviewViewer.Visibility = Visibility.Visible;
        previewTextBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        previewTextBlock.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        previewTextBlock.TextAlignment = System.Windows.TextAlignment.Left;

        if (mode == OfficePreviewMode.Shell)
        {
            textPreviewViewer.Visibility = Visibility.Collapsed;
            previewTextBlock.Text = string.Empty;

            if (shellPreviewHost.TryLoadPreview(pane.FullPath, out var errorMessage))
            {
                shellPreviewHost.Visibility = Visibility.Visible;
            }
            else
            {
                textPreviewViewer.Visibility = Visibility.Visible;
                previewTextBlock.Text = $"Windows preview handler could not be loaded.{Environment.NewLine}{Environment.NewLine}{errorMessage}";
            }

            return;
        }

        previewTextBlock.Text = "Loading...";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        var result = await Task.Run(() =>
            OfficePreviewExtractor.ExtractPreviewWithMode(pane.FullPath, PreviewCharacterLimit, mode));

        previewTextBlock.Text = result.HasPreview ? result.PreviewText : result.DiagnosticText;
    }

    private void OnPlayMediaClicked(object sender, RoutedEventArgs e)
    {
        GetMediaElement(sender)?.Play();
    }

    private void OnPauseMediaClicked(object sender, RoutedEventArgs e)
    {
        GetMediaElement(sender)?.Pause();
    }

    private void OnStopMediaClicked(object sender, RoutedEventArgs e)
    {
        GetMediaElement(sender)?.Stop();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SourceShellPreviewHost.ClearPreview();
        DestinationShellPreviewHost.ClearPreview();
        SourceMediaElement.Stop();
        SourceMediaElement.Source = null;
        DestinationMediaElement.Stop();
        DestinationMediaElement.Source = null;
    }

    private void OnImagePreviewZoomIn(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        ZoomImage(scrollViewer, GetImageZoom(scrollViewer) * ImageZoomStep, e.GetPosition(scrollViewer));
        e.Handled = true;
    }

    private void OnImagePreviewZoomOut(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        ZoomImage(scrollViewer, GetImageZoom(scrollViewer) / ImageZoomStep, e.GetPosition(scrollViewer));
        e.Handled = true;
    }

    private void OnImagePreviewViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            ApplyImageZoom(scrollViewer);
        }
    }

    private System.Windows.Controls.MediaElement? GetMediaElement(object sender) => sender switch
    {
        System.Windows.Controls.Button { Tag: "Source" } => SourceMediaElement,
        System.Windows.Controls.Button { Tag: "Destination" } => DestinationMediaElement,
        _ => null,
    };

    private double GetImageZoom(ScrollViewer scrollViewer) => Equals(scrollViewer.Tag, "Source")
        ? _sourceImageZoom
        : _destinationImageZoom;

    private void ZoomImage(ScrollViewer scrollViewer, double zoom, System.Windows.Point viewportPoint)
    {
        var image = Equals(scrollViewer.Tag, "Source") ? SourcePreviewImage : DestinationPreviewImage;
        var previousWidth = GetContentWidth(image);
        var previousHeight = GetContentHeight(image);
        var previousHorizontalOffset = scrollViewer.HorizontalOffset;
        var previousVerticalOffset = scrollViewer.VerticalOffset;

        var normalizedZoom = Math.Clamp(zoom, DefaultImageZoom, MaxImageZoom);
        if (Equals(scrollViewer.Tag, "Source"))
        {
            _sourceImageZoom = normalizedZoom;
        }
        else
        {
            _destinationImageZoom = normalizedZoom;
        }

        ApplyImageZoom(scrollViewer);

        var newWidth = GetContentWidth(image);
        var newHeight = GetContentHeight(image);
        if (previousWidth <= 0 || previousHeight <= 0 || newWidth <= 0 || newHeight <= 0)
        {
            return;
        }

        var newOffsets = ImagePreviewZoomCalculator.CalculateScrollOffsets(
            previousWidth,
            previousHeight,
            newWidth,
            newHeight,
            previousHorizontalOffset,
            previousVerticalOffset,
            viewportPoint.X,
            viewportPoint.Y,
            scrollViewer.ViewportWidth,
            scrollViewer.ViewportHeight);

        scrollViewer.ScrollToHorizontalOffset(newOffsets.X);
        scrollViewer.ScrollToVerticalOffset(newOffsets.Y);
    }

    private void ApplyImageZoom(ScrollViewer scrollViewer)
    {
        var image = Equals(scrollViewer.Tag, "Source") ? SourcePreviewImage : DestinationPreviewImage;
        if (image.Source is not BitmapSource bitmap)
        {
            return;
        }

        var viewportWidth = scrollViewer.ViewportWidth;
        var viewportHeight = scrollViewer.ViewportHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0 || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return;
        }

        var zoom = GetImageZoom(scrollViewer);
        var displayedSize = ImagePreviewZoomCalculator.CalculateDisplayedSize(
            bitmap.Width,
            bitmap.Height,
            viewportWidth,
            viewportHeight,
            zoom);

        image.Width = displayedSize.Width;
        image.Height = displayedSize.Height;
        scrollViewer.Cursor = zoom > DefaultImageZoom ? ZoomOutCursor : ZoomInCursor;
    }

    private static double GetContentWidth(System.Windows.Controls.Image image) => image.ActualWidth > 0 ? image.ActualWidth : image.Width;

    private static double GetContentHeight(System.Windows.Controls.Image image) => image.ActualHeight > 0 ? image.ActualHeight : image.Height;

    private void ResetImageZoom(ScrollViewer scrollViewer)
    {
        if (Equals(scrollViewer.Tag, "Source"))
        {
            _sourceImageZoom = DefaultImageZoom;
        }
        else
        {
            _destinationImageZoom = DefaultImageZoom;
        }

        scrollViewer.ScrollToHorizontalOffset(0);
        scrollViewer.ScrollToVerticalOffset(0);
        scrollViewer.Cursor = ZoomInCursor;
        Dispatcher.BeginInvoke(() => ApplyImageZoom(scrollViewer), DispatcherPriority.Loaded);
    }
}

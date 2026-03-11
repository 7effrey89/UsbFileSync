using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;

namespace UsbFileSync.App;

public partial class FileComparisonDialog : Window
{
    private const double DefaultImageZoom = 1d;
    private const double ImageZoomStep = 1.25d;
    private const double MaxImageZoom = 6d;

    private readonly IFileLauncherService _fileLauncherService = new WindowsFileLauncherService();
    private readonly string _sourcePath;
    private readonly string _sourceSize;
    private readonly string _sourceModified;
    private readonly string _destinationPath;
    private readonly string _destinationSize;
    private readonly string _destinationModified;
    private readonly IReadOnlyDictionary<string, string>? _previewProviderMappings;
    private FileComparisonDialogViewModel? _viewModel;
    private double _sourceImageZoom = DefaultImageZoom;
    private double _destinationImageZoom = DefaultImageZoom;
    private static readonly System.Windows.Input.Cursor ZoomInCursor = PreviewCursorFactory.CreateZoomInCursor();
    private static readonly System.Windows.Input.Cursor ZoomOutCursor = PreviewCursorFactory.CreateZoomOutCursor();

    public FileComparisonDialog(SyncPreviewRowViewModel row, IReadOnlyDictionary<string, string>? previewProviderMappings = null)
    {
        InitializeComponent();
        _sourcePath = row.SourcePath;
        _sourceSize = row.SourceSize;
        _sourceModified = row.SourceModified;
        _destinationPath = row.DestinationPath;
        _destinationSize = row.DestinationSize;
        _destinationModified = row.DestinationModified;
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
                _previewProviderMappings));

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
                SourcePdfPreview,
                SourceMediaPreviewGrid,
                SourceMediaElement,
                SourceEmptyStateTextBlock,
                SourcePathPanel,
                SourcePathButton);
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
                DestinationPdfPreview,
                DestinationMediaPreviewGrid,
                DestinationMediaElement,
                DestinationEmptyStateTextBlock,
                DestinationPathPanel,
                DestinationPathButton);
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
        WebView2 pdfPreview,
        System.Windows.Controls.Grid mediaPreviewGrid,
        System.Windows.Controls.MediaElement mediaElement,
        System.Windows.Controls.TextBlock emptyStateTextBlock,
        System.Windows.Controls.StackPanel pathPanel,
        System.Windows.Controls.Button pathButton)
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
using System.Windows;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;

namespace UsbFileSync.App;

public partial class FileComparisonDialog : Window
{
    private readonly IFileLauncherService _fileLauncherService = new WindowsFileLauncherService();
    private readonly FileComparisonDialogViewModel _viewModel;

    public FileComparisonDialog(SyncPreviewRowViewModel row)
    {
        InitializeComponent();
        _viewModel = new FileComparisonDialogViewModel(row);
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
            DestinationEmptyStateTextBlock,
            DestinationPathPanel,
            DestinationPathButton);
    }

    private void OnOpenPathClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string path } && !string.IsNullOrWhiteSpace(path))
        {
            _fileLauncherService.OpenItem(path);
        }
    }

    private static void PopulatePane(
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
            textPreviewViewer.Visibility = Visibility.Visible;
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

        if (pane.HasImagePreview)
        {
            textPreviewViewer.Visibility = Visibility.Collapsed;
            imagePreviewViewer.Visibility = Visibility.Visible;
            previewImage.Source = pane.PreviewImageSource;
            previewTextBlock.Text = string.Empty;
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
        }

        pathButton.Content = pane.HasPath ? pane.FullPath : "Not available";
        pathButton.Tag = pane.FullPath;
        pathButton.IsEnabled = pane.HasPath;
    }
}
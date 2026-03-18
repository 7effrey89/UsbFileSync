using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Threading;
using UsbFileSync.App.ViewModels;

namespace UsbFileSync.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private const double DefaultDashboardMinHeight = 170d;
    private GridLength _savedDashboardHeight = new(220);

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        Closed += OnClosed;
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(
            _viewModel.ParallelCopyCount,
            _viewModel.HideMacOsSystemFiles,
            _viewModel.GetExcludedPathPatterns(),
            _viewModel.GetPreviewProviderMappings(),
            _viewModel.GetUseCustomCloudProviderCredentials(),
            _viewModel.GetCloudProviderAppRegistrations())
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.UpdateParallelCopyCount(dialog.ParallelCopyCount);
            _viewModel.UpdateHideMacOsSystemFiles(dialog.HideMacOsSystemFiles);
            _viewModel.UpdateExcludedPathPatterns(dialog.ExcludedPathPatterns);
            _viewModel.UpdatePreviewProviderMappings(dialog.PreviewProviderMappings);
            _viewModel.UpdateUseCustomCloudProviderCredentials(dialog.UseCustomCloudProviderCredentials);
            _viewModel.UpdateCloudProviderAppRegistrations(dialog.CloudProviderAppRegistrations);
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e) => Close();

    private void OnSelectAllInTabClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDriveToolsWorkspaceSelected && _viewModel.IsDuplicateDriveToolSelected)
        {
            _viewModel.SelectAllDriveToolDuplicates();
            return;
        }

        _viewModel.SelectAllInTab(GetSelectedPreviewTabKind());
    }

    private void OnSelectByPatternClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SelectByPatternDialog
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_viewModel.IsDriveToolsWorkspaceSelected && _viewModel.IsDuplicateDriveToolSelected)
        {
            _viewModel.SelectDriveToolDuplicatesByPattern(dialog.PatternText, dialog.SelectionTarget);
            return;
        }

        _viewModel.SelectByPattern(GetSelectedPreviewTabKind(), dialog.PatternText, dialog.SelectionTarget);
    }

    private void OnInvertSelectionClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDriveToolsWorkspaceSelected && _viewModel.IsDuplicateDriveToolSelected)
        {
            _viewModel.InvertDriveToolDuplicateSelection();
            return;
        }

        _viewModel.InvertSelectionInTab(GetSelectedPreviewTabKind());
    }

    private void OnToggleInfoBoxesClicked(object sender, RoutedEventArgs e)
    {
        var shouldShow = InfoBoxesGrid.Visibility != Visibility.Visible;
        ApplyInfoBoxesVisibility(shouldShow);
    }

    private void OnResetLayoutClicked(object sender, RoutedEventArgs e)
    {
        SourceLocationColumnDefinition.Width = new GridLength(1.35, GridUnitType.Star);
        SyncSettingsColumnDefinition.Width = new GridLength(360);
        DestinationLocationColumnDefinition.Width = new GridLength(1.35, GridUnitType.Star);

        PreviewPaneRowDefinition.Height = new GridLength(1, GridUnitType.Star);
        DashboardSplitterRowDefinition.Height = new GridLength(12);
        DashboardRowDefinition.Height = new GridLength(220);
        _savedDashboardHeight = DashboardRowDefinition.Height;

        QueueColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
        InfoBoxesSplitterColumnDefinition.Width = new GridLength(12);
        ActivityColumnDefinition.Width = new GridLength(1, GridUnitType.Star);

        ApplyInfoBoxesVisibility(true);
    }

    private void OnOpenAboutClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog("https://github.com/7effrey89/UsbFileSync")
        {
            Owner = this,
        };

        dialog.ShowDialog();
    }

    private void OnShowComparisonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        try
        {
            var row = _viewModel.CreatePreviewDialogRow(menuItem.Tag);
            if (row is null)
            {
                return;
            }

            var dialog = new FileComparisonDialog(row, _viewModel.GetPreviewProviderMappings())
            {
                Owner = this,
            };

            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Could not open the preview view.\n\n{exception.Message}",
                "Preview unavailable",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private async void OnRenameDriveToolDuplicateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DriveToolDuplicateRowViewModel row } ||
            !_viewModel.CanModifyDriveToolDuplicate(row))
        {
            return;
        }

        var dialog = new TextInputDialog(
            prompt: "Rename file",
            description: "Enter the new file name.",
            initialValue: row.FileName)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _viewModel.RenameDriveToolDuplicateAsync(row, dialog.EnteredText).ConfigureAwait(true);
    }

    private async void OnMoveDriveToolDuplicateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DriveToolDuplicateRowViewModel row } ||
            !_viewModel.CanModifyDriveToolDuplicate(row))
        {
            return;
        }

        var initialFolder = System.IO.Path.GetDirectoryName(row.OpenPath) ?? _viewModel.DriveToolsPath;
        var destinationFolder = _viewModel.BrowseForDriveToolMoveTarget(initialFolder);
        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            return;
        }

        await _viewModel.MoveDriveToolDuplicateAsync(row, destinationFolder).ConfigureAwait(true);
    }

    private void OnSourcePathTextBoxGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.SetSourcePathFocused(true);
        SelectAllText(sender);
    }

    private void OnSourcePathTextBoxLostKeyboardFocus(object sender, RoutedEventArgs e) =>
        _viewModel.SetSourcePathFocused(false);

    private void OnDestinationPathTextBoxGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.SetDestinationPathFocused(true);
        SelectAllText(sender);
    }

    private void OnDestinationPathTextBoxLostKeyboardFocus(object sender, RoutedEventArgs e) =>
        _viewModel.SetDestinationPathFocused(false);

    private void OnDriveToolsPathTextBoxGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.SetDriveToolsPathFocused(true);
        SelectAllText(sender);
    }

    private void OnDriveToolsPathTextBoxLostKeyboardFocus(object sender, RoutedEventArgs e) =>
        _viewModel.SetDriveToolsPathFocused(false);

    private void OnAdditionalDestinationPathTextBoxGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.SetAdditionalDestinationPathFocused((sender as FrameworkElement)?.DataContext, true);
        SelectAllText(sender);
    }

    private void OnAdditionalDestinationPathTextBoxLostKeyboardFocus(object sender, RoutedEventArgs e) =>
        _viewModel.SetAdditionalDestinationPathFocused((sender as FrameworkElement)?.DataContext, false);

    private void OnPreviewColumnFilterButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PreviewColumnHeader header } element)
        {
            return;
        }

        _viewModel.OpenPreviewColumnFilter(GetSelectedPreviewTabKind(), header);
        PreviewFilterPopup.PlacementTarget = element;
        PreviewFilterPopup.IsOpen = true;
        PreviewFilterSearchTextBox.Dispatcher.BeginInvoke(() =>
        {
            PreviewFilterSearchTextBox.Focus();
            PreviewFilterSearchTextBox.SelectAll();
        }, DispatcherPriority.Input);
        e.Handled = true;
    }

    private void OnSelectVisiblePreviewFilterOptionsClicked(object sender, RoutedEventArgs e) =>
        _viewModel.SetAllVisiblePreviewFilterOptions(true);

    private void OnClearVisiblePreviewFilterOptionsClicked(object sender, RoutedEventArgs e) =>
        _viewModel.SetAllVisiblePreviewFilterOptions(false);

    private void OnDeselectNonShownPreviewFilterOptionsClicked(object sender, RoutedEventArgs e) =>
        _viewModel.SetAllNonShownPreviewFilterOptions(false);

    private void OnSortPreviewColumnAscendingClicked(object sender, RoutedEventArgs e) =>
        _viewModel.SortActivePreviewColumn(ListSortDirection.Ascending);

    private void OnSortPreviewColumnDescendingClicked(object sender, RoutedEventArgs e) =>
        _viewModel.SortActivePreviewColumn(ListSortDirection.Descending);

    private void OnApplyPreviewColumnFilterClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplyActivePreviewColumnFilter();
        PreviewFilterPopup.IsOpen = false;
    }

    private void OnClearPreviewColumnFilterClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearActivePreviewColumnFilter();
        PreviewFilterPopup.IsOpen = false;
    }

    private void OnPreviewFilterPopupClosed(object sender, EventArgs e) =>
        _viewModel.PreviewFilterSearchText = string.Empty;

    private void OnPreviewFilterResizeThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        var nextWidth = Math.Max(PreviewFilterPopupBorder.MinWidth, PreviewFilterPopupBorder.Width + e.HorizontalChange);
        var nextHeight = Math.Max(PreviewFilterPopupBorder.MinHeight, PreviewFilterPopupBorder.Height + e.VerticalChange);
        PreviewFilterPopupBorder.Width = nextWidth;
        PreviewFilterPopupBorder.Height = nextHeight;
    }

    private static void SelectAllText(object sender)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        textBox.Dispatcher.BeginInvoke(textBox.SelectAll, DispatcherPriority.Input);
    }

    private PreviewTabKind GetSelectedPreviewTabKind() => PreviewTabControl.SelectedIndex switch
    {
        0 => PreviewTabKind.NewFiles,
        1 => PreviewTabKind.ChangedFiles,
        2 => PreviewTabKind.DeletedFiles,
        3 => PreviewTabKind.UnchangedFiles,
        _ => PreviewTabKind.AllFiles,
    };

    private void ApplyInfoBoxesVisibility(bool isVisible)
    {
        if (!isVisible)
        {
            _savedDashboardHeight = DashboardRowDefinition.Height.Value > 0
                ? DashboardRowDefinition.Height
                : new GridLength(220);
        }

        var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewDashboardGridSplitter.Visibility = visibility;
        InfoBoxesGrid.Visibility = visibility;
        ToggleInfoBoxesMenuItem.IsChecked = isVisible;

        DashboardSplitterRowDefinition.Height = isVisible ? new GridLength(12) : new GridLength(0);
        DashboardRowDefinition.MinHeight = isVisible ? DefaultDashboardMinHeight : 0;
        DashboardRowDefinition.Height = isVisible ? _savedDashboardHeight : new GridLength(0);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }
}

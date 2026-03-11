using System.Windows;
using System.Windows.Controls;
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
        var dialog = new SettingsDialog(_viewModel.ParallelCopyCount)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.UpdateParallelCopyCount(dialog.ParallelCopyCount);
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e) => Close();

    private void OnSelectAllInTabClicked(object sender, RoutedEventArgs e) =>
        _viewModel.SelectAllInTab(GetSelectedPreviewTabKind());

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

        _viewModel.SelectByPattern(GetSelectedPreviewTabKind(), dialog.PatternText, dialog.SelectionTarget);
    }

    private void OnInvertSelectionClicked(object sender, RoutedEventArgs e) =>
        _viewModel.InvertSelectionInTab(GetSelectedPreviewTabKind());

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

    private void OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();
}

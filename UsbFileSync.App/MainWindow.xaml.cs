using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UsbFileSync.App.ViewModels;

namespace UsbFileSync.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

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

    private void OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();
}

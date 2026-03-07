using System.Windows;
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

    private void OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();
}

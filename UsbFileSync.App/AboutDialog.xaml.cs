using System.Diagnostics;
using System.Windows;

namespace UsbFileSync.App;

public partial class AboutDialog : Window
{
    private readonly string _repositoryUrl;

    public AboutDialog(string repositoryUrl)
    {
        InitializeComponent();
        _repositoryUrl = repositoryUrl;
        var version = typeof(App).Assembly.GetName().Version;
        VersionTextBlock.Text = version is null
            ? "Version unavailable"
            : $"Version {version.Major}.{version.Minor}.{version.Build}";
        RepositoryLinkText.Text = _repositoryUrl;
    }

    private void OnRepositoryLinkClicked(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_repositoryUrl)
        {
            UseShellExecute = true,
        });
    }
}
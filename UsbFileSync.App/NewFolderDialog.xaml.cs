using System.Windows;

namespace UsbFileSync.App;

public partial class NewFolderDialog : Window
{
    public NewFolderDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            FolderNameTextBox.Focus();
            FolderNameTextBox.SelectAll();
        };
    }

    public string FolderName { get; private set; } = string.Empty;

    private void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        FolderName = FolderNameTextBox.Text;
        DialogResult = true;
    }
}
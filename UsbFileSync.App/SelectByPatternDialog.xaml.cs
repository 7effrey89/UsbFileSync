using System.Windows;
using UsbFileSync.App.ViewModels;

namespace UsbFileSync.App;

public partial class SelectByPatternDialog : Window
{
    public SelectByPatternDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            PatternTextBox.Focus();
            PatternTextBox.SelectAll();
        };
    }

    public string PatternText { get; private set; } = string.Empty;

    public PreviewSelectionTarget SelectionTarget { get; private set; } = PreviewSelectionTarget.FileName;

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        var keyword = PatternTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            System.Windows.MessageBox.Show(
                this,
                "Enter a keyword to match against the selected preview tab.",
                "Select by pattern",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            PatternTextBox.Focus();
            PatternTextBox.SelectAll();
            return;
        }

        PatternText = keyword;
        SelectionTarget = FileFolderRadioButton.IsChecked == true
            ? PreviewSelectionTarget.FileFolder
            : FullPathRadioButton.IsChecked == true
                ? PreviewSelectionTarget.FullPath
                : PreviewSelectionTarget.FileName;
        DialogResult = true;
    }
}
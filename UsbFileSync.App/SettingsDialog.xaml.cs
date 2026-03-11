using System.Globalization;
using System.Windows;

namespace UsbFileSync.App;

public partial class SettingsDialog : Window
{
    public SettingsDialog(int parallelCopyCount)
    {
        InitializeComponent();
        ParallelCopyCount = Math.Max(0, parallelCopyCount);
        ParallelCopyCountTextBox.Text = ParallelCopyCount.ToString(CultureInfo.InvariantCulture);
        Loaded += (_, _) =>
        {
            ParallelCopyCountTextBox.Focus();
            ParallelCopyCountTextBox.SelectAll();
        };
    }

    public int ParallelCopyCount { get; private set; }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!TryParseParallelCopyCount(ParallelCopyCountTextBox.Text, out var value))
        {
            System.Windows.MessageBox.Show(this, "Enter a whole number that is 0 or greater. Use 0 for automatic parallel copy tuning.", "Invalid parallel copies value", MessageBoxButton.OK, MessageBoxImage.Warning);
            ParallelCopyCountTextBox.Focus();
            ParallelCopyCountTextBox.SelectAll();
            return;
        }

        ParallelCopyCount = value;
        DialogResult = true;
    }

    public static bool TryParseParallelCopyCount(string? text, out int value)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) && parsedValue >= 0)
        {
            value = parsedValue;
            return true;
        }

        value = 0;
        return false;
    }
}

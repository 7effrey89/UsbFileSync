using System.Windows;

namespace UsbFileSync.App;

public partial class TextInputDialog : Window
{
    public TextInputDialog(string prompt, string description, string initialValue)
    {
        InitializeComponent();
        PromptTextBlock.Text = prompt;
        DescriptionTextBlock.Text = description;
        ValueTextBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public string EnteredText => ValueTextBox.Text.Trim();

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

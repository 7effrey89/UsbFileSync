using System.Globalization;
using System.Collections.ObjectModel;
using System.Windows;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;

namespace UsbFileSync.App;

public partial class SettingsDialog : Window
{
    public SettingsDialog(int parallelCopyCount, bool hideMacOsSystemFiles, IReadOnlyDictionary<string, string>? previewProviderMappings = null)
    {
        InitializeComponent();
        ParallelCopyCount = Math.Max(0, parallelCopyCount);
        HideMacOsSystemFiles = hideMacOsSystemFiles;
        ParallelCopyCountTextBox.Text = ParallelCopyCount.ToString(CultureInfo.InvariantCulture);
        HideMacOsSystemFilesCheckBox.IsChecked = HideMacOsSystemFiles;
        ProviderOptions = Enum.GetValues<PreviewProviderKind>();
        PreviewProviderMappingItems = CreateMappingItems(previewProviderMappings);
        PreviewProviderMappingsDataGrid.ItemsSource = PreviewProviderMappingItems;
        if (PreviewProviderMappingsDataGrid.Columns[1] is System.Windows.Controls.DataGridComboBoxColumn comboColumn)
        {
            comboColumn.ItemsSource = ProviderOptions;
        }
        Loaded += (_, _) =>
        {
            ParallelCopyCountTextBox.Focus();
            ParallelCopyCountTextBox.SelectAll();
        };
    }

    public int ParallelCopyCount { get; private set; }

    public bool HideMacOsSystemFiles { get; private set; }

    public ObservableCollection<PreviewProviderMappingViewModel> PreviewProviderMappingItems { get; }

    public Array ProviderOptions { get; }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!TryParseParallelCopyCount(ParallelCopyCountTextBox.Text, out var value))
        {
            System.Windows.MessageBox.Show(this, "Enter a whole number that is 0 or greater. Use 0 to let UsbFileSync automatically assess the best amount of parallelism.", "Invalid parallel copies value", MessageBoxButton.OK, MessageBoxImage.Warning);
            ParallelCopyCountTextBox.Focus();
            ParallelCopyCountTextBox.SelectAll();
            return;
        }

        ParallelCopyCount = value;
        HideMacOsSystemFiles = HideMacOsSystemFilesCheckBox.IsChecked != false;
        if (!TryCreateSerializableMappings(PreviewProviderMappingItems, out var mappings, out var errorMessage))
        {
            System.Windows.MessageBox.Show(this, errorMessage, "Invalid preview mapping", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _previewProviderMappings = mappings;
        DialogResult = true;
    }

    public IReadOnlyDictionary<string, string> PreviewProviderMappings => _previewProviderMappings;

    private Dictionary<string, string> _previewProviderMappings = PreviewProviderDefaults.CreateSerializableMapping();

    private void OnAddMappingClicked(object sender, RoutedEventArgs e)
    {
        var item = new PreviewProviderMappingViewModel
        {
            Extension = ".ext",
            ProviderKind = PreviewProviderKind.Unsupported,
        };

        PreviewProviderMappingItems.Add(item);
        PreviewProviderMappingsDataGrid.SelectedItem = item;
        PreviewProviderMappingsDataGrid.ScrollIntoView(item);
    }

    private void OnRemoveMappingClicked(object sender, RoutedEventArgs e)
    {
        if (PreviewProviderMappingsDataGrid.SelectedItem is PreviewProviderMappingViewModel mapping)
        {
            PreviewProviderMappingItems.Remove(mapping);
        }
    }

    private void OnRestoreDefaultsClicked(object sender, RoutedEventArgs e)
    {
        PreviewProviderMappingItems.Clear();
        foreach (var item in CreateMappingItems(PreviewProviderDefaults.CreateSerializableMapping()))
        {
            PreviewProviderMappingItems.Add(item);
        }
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

    public static bool TryCreateSerializableMappings(
        IEnumerable<PreviewProviderMappingViewModel> mappings,
        out Dictionary<string, string> serializedMappings,
        out string errorMessage)
    {
        serializedMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            var normalizedExtension = PreviewProviderDefaults.NormalizeExtension(mapping.Extension);
            if (string.IsNullOrWhiteSpace(normalizedExtension) || normalizedExtension == ".")
            {
                errorMessage = "Each preview mapping needs a valid file extension such as .txt or .pdf.";
                return false;
            }

            if (serializedMappings.ContainsKey(normalizedExtension))
            {
                errorMessage = $"The extension {normalizedExtension} is listed more than once.";
                return false;
            }

            serializedMappings[normalizedExtension] = mapping.ProviderKind.ToString();
        }

        errorMessage = string.Empty;
        return true;
    }

    private static ObservableCollection<PreviewProviderMappingViewModel> CreateMappingItems(IReadOnlyDictionary<string, string>? previewProviderMappings)
    {
        var items = new ObservableCollection<PreviewProviderMappingViewModel>();
        var mappings = previewProviderMappings?.Count > 0
            ? previewProviderMappings
            : PreviewProviderDefaults.CreateSerializableMapping();

        foreach (var pair in mappings.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<PreviewProviderKind>(pair.Value, ignoreCase: true, out var providerKind))
            {
                continue;
            }

            items.Add(new PreviewProviderMappingViewModel
            {
                Extension = pair.Key,
                ProviderKind = providerKind,
            });
        }

        return items;
    }
}

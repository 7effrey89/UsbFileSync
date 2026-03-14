using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.App;

public partial class SettingsDialog : Window
{
    private readonly IFolderPickerService _folderPickerService;
    private readonly ICloudAccountStore _cloudAccountStore;

    public SettingsDialog(
        int parallelCopyCount,
        bool hideMacOsSystemFiles,
        IReadOnlyDictionary<string, string>? previewProviderMappings = null,
        ICloudAccountStore? cloudAccountStore = null,
        IFolderPickerService? folderPickerService = null)
    {
        InitializeComponent();
        _folderPickerService = folderPickerService ?? new WindowsFolderPickerService();
        _cloudAccountStore = cloudAccountStore ?? CloudAccountStoreFactory.CreateDefault();
        ParallelCopyCount = Math.Max(0, parallelCopyCount);
        HideMacOsSystemFiles = hideMacOsSystemFiles;
        ParallelCopyCountTextBox.Text = ParallelCopyCount.ToString(CultureInfo.InvariantCulture);
        HideMacOsSystemFilesCheckBox.IsChecked = HideMacOsSystemFiles;
        ProviderOptions = Enum.GetValues<PreviewProviderKind>();
        CloudProviderOptions = Enum.GetValues<CloudStorageProvider>();
        PreviewProviderMappingItems = CreateMappingItems(previewProviderMappings);
        CloudAccountItems = CreateCloudAccountItems(_cloudAccountStore.Load());
        PreviewProviderMappingsDataGrid.ItemsSource = PreviewProviderMappingItems;
        CloudAccountsDataGrid.ItemsSource = CloudAccountItems;
        if (PreviewProviderMappingsDataGrid.Columns[1] is System.Windows.Controls.DataGridComboBoxColumn comboColumn)
        {
            comboColumn.ItemsSource = ProviderOptions;
        }
        if (CloudAccountsDataGrid.Columns[0] is System.Windows.Controls.DataGridComboBoxColumn providerColumn)
        {
            providerColumn.ItemsSource = CloudProviderOptions;
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

    public ObservableCollection<CloudAccountRegistrationViewModel> CloudAccountItems { get; }

    public Array CloudProviderOptions { get; }

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

        if (!TryCreateSerializableCloudAccounts(CloudAccountItems, out var cloudAccounts, out errorMessage))
        {
            System.Windows.MessageBox.Show(this, errorMessage, "Invalid cloud account", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _cloudAccountStore.Save(cloudAccounts);
        }
        catch (IOException ioException)
        {
            System.Windows.MessageBox.Show(this, $"Cloud account settings could not be saved.\n\n{ioException.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (UnauthorizedAccessException unauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, $"Cloud account settings could not be saved.\n\n{unauthorizedAccessException.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void OnAddCloudAccountClicked(object sender, RoutedEventArgs e)
    {
        var item = new CloudAccountRegistrationViewModel();
        CloudAccountItems.Add(item);
        CloudAccountsDataGrid.SelectedItem = item;
        CloudAccountsDataGrid.ScrollIntoView(item);
    }

    private void OnRemoveCloudAccountClicked(object sender, RoutedEventArgs e)
    {
        if (CloudAccountsDataGrid.SelectedItem is CloudAccountRegistrationViewModel cloudAccount)
        {
            CloudAccountItems.Remove(cloudAccount);
        }
    }

    private void OnBrowseCloudAccountFolderClicked(object sender, RoutedEventArgs e)
    {
        if (CloudAccountsDataGrid.SelectedItem is not CloudAccountRegistrationViewModel cloudAccount)
        {
            return;
        }

        var selectedPath = _folderPickerService.PickFolder("Select the local synced folder for this cloud account", cloudAccount.LocalRootPath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            cloudAccount.LocalRootPath = selectedPath;
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

    public static bool TryCreateSerializableCloudAccounts(
        IEnumerable<CloudAccountRegistrationViewModel> cloudAccounts,
        out List<CloudAccountRegistration> serializedAccounts,
        out string errorMessage)
    {
        serializedAccounts = [];
        var duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cloudAccount in cloudAccounts)
        {
            var login = cloudAccount.Login?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(login))
            {
                errorMessage = "Each cloud account needs a login or account label.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(cloudAccount.LocalRootPath))
            {
                errorMessage = $"Select the local synced folder for {CloudStorageProviderInfo.GetDisplayName(cloudAccount.Provider)} ({login}).";
                return false;
            }

            string fullLocalRootPath;
            try
            {
                fullLocalRootPath = Path.GetFullPath(cloudAccount.LocalRootPath);
            }
            catch (Exception)
            {
                errorMessage = $"The linked folder for {CloudStorageProviderInfo.GetDisplayName(cloudAccount.Provider)} ({login}) is invalid.";
                return false;
            }

            if (!Directory.Exists(fullLocalRootPath))
            {
                errorMessage = $"The linked folder for {CloudStorageProviderInfo.GetDisplayName(cloudAccount.Provider)} ({login}) does not exist.";
                return false;
            }

            var duplicateKey = $"{cloudAccount.Provider}:{login}";
            if (!duplicateKeys.Add(duplicateKey))
            {
                errorMessage = $"The account {login} is already listed for {CloudStorageProviderInfo.GetDisplayName(cloudAccount.Provider)}.";
                return false;
            }

            serializedAccounts.Add(new CloudAccountRegistration
            {
                Id = string.IsNullOrWhiteSpace(cloudAccount.Id) ? Guid.NewGuid().ToString("N") : cloudAccount.Id,
                Provider = cloudAccount.Provider,
                Login = login,
                LocalRootPath = fullLocalRootPath,
            });
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

    private static ObservableCollection<CloudAccountRegistrationViewModel> CreateCloudAccountItems(IReadOnlyList<CloudAccountRegistration> accounts)
    {
        var items = new ObservableCollection<CloudAccountRegistrationViewModel>();
        foreach (var account in accounts
                     .OrderBy(account => account.Provider)
                     .ThenBy(account => account.Login, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new CloudAccountRegistrationViewModel
            {
                Id = account.Id,
                Provider = account.Provider,
                Login = account.Login,
                LocalRootPath = account.LocalRootPath,
            });
        }

        return items;
    }
}

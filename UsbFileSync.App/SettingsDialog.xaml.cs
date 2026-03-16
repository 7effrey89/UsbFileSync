using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.App;

public partial class SettingsDialog : Window
{
    private const string FixedOneDriveTenantId = "consumers";
    private const string DropboxRedirectUri = "http://127.0.0.1:53682/";

    public SettingsDialog(
        int parallelCopyCount,
        bool hideMacOsSystemFiles,
        IReadOnlyDictionary<string, string>? previewProviderMappings = null,
        bool useCustomCloudProviderCredentials = false,
        IReadOnlyList<CloudProviderAppRegistration>? cloudProviderAppRegistrations = null)
    {
        InitializeComponent();
        ParallelCopyCount = Math.Max(0, parallelCopyCount);
        HideMacOsSystemFiles = hideMacOsSystemFiles;
        UseCustomCloudProviderCredentials = useCustomCloudProviderCredentials;
        ParallelCopyCountTextBox.Text = ParallelCopyCount.ToString(CultureInfo.InvariantCulture);
        HideMacOsSystemFilesCheckBox.IsChecked = HideMacOsSystemFiles;
        UseCustomProviderCredentialsCheckBox.IsChecked = UseCustomCloudProviderCredentials;
        ProviderOptions = Enum.GetValues<PreviewProviderKind>();
        CloudStorageProviderOptions = Enum.GetValues<CloudStorageProvider>();
        PreviewProviderMappingItems = CreateMappingItems(previewProviderMappings);
        CloudProviderAppRegistrationItems = CreateCloudProviderAppRegistrationItems(cloudProviderAppRegistrations);
        PreviewProviderMappingsDataGrid.ItemsSource = PreviewProviderMappingItems;
        CloudProviderRegistrationsDataGrid.ItemsSource = CloudProviderAppRegistrationItems;
        foreach (var item in CloudProviderAppRegistrationItems)
        {
            item.PropertyChanged += OnCloudProviderAppRegistrationItemPropertyChanged;
        }

        if (PreviewProviderMappingsDataGrid.Columns[1] is System.Windows.Controls.DataGridComboBoxColumn comboColumn)
        {
            comboColumn.ItemsSource = ProviderOptions;
        }

        if (CloudProviderRegistrationsDataGrid.Columns[0] is System.Windows.Controls.DataGridComboBoxColumn providerComboColumn)
        {
            providerComboColumn.ItemsSource = CloudStorageProviderOptions;
        }

        Loaded += (_, _) =>
        {
            ParallelCopyCountTextBox.Focus();
            ParallelCopyCountTextBox.SelectAll();
        };
    }

    public int ParallelCopyCount { get; private set; }

    public bool HideMacOsSystemFiles { get; private set; }

    public bool UseCustomCloudProviderCredentials { get; private set; }

    public ObservableCollection<PreviewProviderMappingViewModel> PreviewProviderMappingItems { get; }

    public ObservableCollection<CloudProviderAppRegistrationViewModel> CloudProviderAppRegistrationItems { get; }

    public Array ProviderOptions { get; }

    public Array CloudStorageProviderOptions { get; }

    private bool _isTestingCloudProviderConnection;
    private string _testingRegistrationId = string.Empty;

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
        UseCustomCloudProviderCredentials = UseCustomProviderCredentialsCheckBox.IsChecked == true;
        if (!TryCreateSerializableMappings(PreviewProviderMappingItems, out var mappings, out var errorMessage))
        {
            System.Windows.MessageBox.Show(this, errorMessage, "Invalid preview mapping", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryCreateCloudProviderAppRegistrations(CloudProviderAppRegistrationItems, out var registrations, out errorMessage))
        {
            System.Windows.MessageBox.Show(this, errorMessage, "Invalid cloud provider settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _previewProviderMappings = mappings;
        _cloudProviderAppRegistrations = registrations;
        DialogResult = true;
    }

    public IReadOnlyDictionary<string, string> PreviewProviderMappings => _previewProviderMappings;

    public IReadOnlyList<CloudProviderAppRegistration> CloudProviderAppRegistrations => _cloudProviderAppRegistrations;

    private Dictionary<string, string> _previewProviderMappings = PreviewProviderDefaults.CreateSerializableMapping();

    private List<CloudProviderAppRegistration> _cloudProviderAppRegistrations = [];

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
        var item = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
        {
            Alias = CreateSuggestedAlias(CloudStorageProvider.GoogleDrive, CloudProviderAppRegistrationItems),
            ConnectionStatusText = "Add credentials, then test this account.",
        };

        item.PropertyChanged += OnCloudProviderAppRegistrationItemPropertyChanged;
        CloudProviderAppRegistrationItems.Add(item);
        CloudProviderRegistrationsDataGrid.SelectedItem = item;
        CloudProviderRegistrationsDataGrid.ScrollIntoView(item);
    }

    private void OnRemoveCloudAccountClicked(object sender, RoutedEventArgs e)
    {
        if (CloudProviderRegistrationsDataGrid.SelectedItem is not CloudProviderAppRegistrationViewModel registration)
        {
            return;
        }

        CloudProviderAppRegistrationItems.Remove(registration);
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

    public static bool CanTestCloudProviderConnection(
        bool useCustomCloudProviderCredentials,
        CloudProviderAppRegistrationViewModel? registration)
    {
        if (!useCustomCloudProviderCredentials)
        {
            return false;
        }

        return registration is not null && !string.IsNullOrWhiteSpace(registration.ClientId);
    }

    public static string GetCloudProviderConnectionGuidance(
        bool useCustomCloudProviderCredentials,
        CloudProviderAppRegistrationViewModel? registration)
    {
        if (!useCustomCloudProviderCredentials)
        {
            return "Turn on 'Use custom provider credentials' before testing a cloud account.";
        }

        if (registration is null)
        {
            return "Select a cloud account row before testing.";
        }

        if (string.IsNullOrWhiteSpace(registration.ClientId))
        {
            return registration.Provider switch
            {
                CloudStorageProvider.Dropbox => "Enter a Dropbox app key before testing this Dropbox account.",
                CloudStorageProvider.OneDrive => "Enter a Microsoft application client ID before testing this OneDrive account.",
                _ => "Enter a Google OAuth client ID before testing this Google Drive account.",
            };
        }

        return registration.Provider switch
        {
            CloudStorageProvider.Dropbox => string.IsNullOrWhiteSpace(registration.ClientSecret)
                ? $"Dropbox is ready to test. In your Dropbox app settings, register redirect URI '{DropboxRedirectUri}'. Add an app secret too if your Dropbox app requires it."
                : $"Dropbox is ready to test. In your Dropbox app settings, register redirect URI '{DropboxRedirectUri}'.",
            CloudStorageProvider.OneDrive => $"OneDrive is ready to test. UsbFileSync uses the fixed '{FixedOneDriveTenantId}' tenant.",
            _ => string.IsNullOrWhiteSpace(registration.ClientSecret)
                ? "Google Drive is ready to test. Add a client secret too if your Google OAuth client requires one."
                : "Google Drive is ready to test.",
        };
    }

    public static bool TryCreateCloudProviderAppRegistrations(
        IEnumerable<CloudProviderAppRegistrationViewModel> registrations,
        out List<CloudProviderAppRegistration> serializedRegistrations,
        out string errorMessage)
    {
        serializedRegistrations = [];
        var aliasesByProvider = new Dictionary<CloudStorageProvider, HashSet<string>>();

        foreach (var registration in registrations
            .Where(item => item is not null))
        {
            var alias = registration.Alias.Trim();
            var clientId = registration.ClientId.Trim();
            var clientSecret = registration.ClientSecret.Trim();

            if (registration.Provider == CloudStorageProvider.GoogleDrive &&
                !TryNormalizeGoogleDriveCredentials(clientId, clientSecret, out clientId, out clientSecret, out errorMessage))
            {
                return false;
            }

            if (registration.Provider == CloudStorageProvider.OneDrive)
            {
                registration.TenantId = FixedOneDriveTenantId;
            }

            if (string.IsNullOrWhiteSpace(clientId) &&
                string.IsNullOrWhiteSpace(clientSecret) &&
                string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                errorMessage = $"The {registration.ProviderDisplayName} account '{GetAccountLabel(alias)}' is missing the OAuth client ID / app key.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(alias))
            {
                errorMessage = $"Enter an alias for the configured {registration.ProviderDisplayName} account with client ID/app key '{clientId}'.";
                return false;
            }

            if (!aliasesByProvider.TryGetValue(registration.Provider, out var aliases))
            {
                aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                aliasesByProvider[registration.Provider] = aliases;
            }

            if (!aliases.Add(alias))
            {
                errorMessage = $"The alias '{alias}' is listed more than once for {registration.ProviderDisplayName}. Use a unique alias for each account.";
                return false;
            }

            serializedRegistrations.Add(new CloudProviderAppRegistration
            {
                RegistrationId = EnsureRegistrationId(registration),
                Provider = registration.Provider,
                Alias = alias,
                ClientId = clientId,
                ClientSecret = registration.Provider == CloudStorageProvider.OneDrive
                    ? string.Empty
                    : clientSecret,
                TenantId = registration.Provider == CloudStorageProvider.OneDrive
                    ? FixedOneDriveTenantId
                    : string.Empty,
            });
        }

        serializedRegistrations = serializedRegistrations
            .OrderBy(item => item.Provider)
            .ThenBy(item => item.Alias, StringComparer.OrdinalIgnoreCase)
            .ToList();

        errorMessage = string.Empty;
        return true;
    }

    private static string EnsureRegistrationId(CloudProviderAppRegistrationViewModel registration)
    {
        if (string.IsNullOrWhiteSpace(registration.RegistrationId))
        {
            registration.RegistrationId = Guid.NewGuid().ToString("N");
        }

        return registration.RegistrationId;
    }

    private static string GetAccountLabel(string alias) => string.IsNullOrWhiteSpace(alias) ? "(unnamed account)" : alias;

    private static string CreateSuggestedAlias(
        CloudStorageProvider provider,
        IEnumerable<CloudProviderAppRegistrationViewModel> registrations)
    {
        var baseAlias = provider switch
        {
            CloudStorageProvider.Dropbox => "Dropbox account",
            CloudStorageProvider.OneDrive => "OneDrive account",
            _ => "Google Drive account",
        };

        var usedAliases = registrations
            .Where(item => item.Provider == provider)
            .Select(item => item.Alias?.Trim() ?? string.Empty)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!usedAliases.Contains(baseAlias))
        {
            return baseAlias;
        }

        var index = 2;
        while (usedAliases.Contains($"{baseAlias} {index}"))
        {
            index++;
        }

        return $"{baseAlias} {index}";
    }

    private static bool IsMeaningfulCloudRegistrationProperty(string? propertyName) =>
        propertyName is nameof(CloudProviderAppRegistrationViewModel.Provider)
            or nameof(CloudProviderAppRegistrationViewModel.Alias)
            or nameof(CloudProviderAppRegistrationViewModel.ClientId)
            or nameof(CloudProviderAppRegistrationViewModel.ClientSecret)
            or nameof(CloudProviderAppRegistrationViewModel.TenantId);

    private async void OnTestCloudProviderConnectionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CloudProviderAppRegistrationViewModel registration })
        {
            return;
        }

        CloudProviderRegistrationsDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        CloudProviderRegistrationsDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        var useCustomCloudProviderCredentials = UseCustomProviderCredentialsCheckBox.IsChecked == true;
        if (!CanTestCloudProviderConnection(useCustomCloudProviderCredentials, registration))
        {
            var message = GetCloudProviderConnectionGuidance(useCustomCloudProviderCredentials, registration);
            registration.ConnectionStatusText = message;
            System.Windows.MessageBox.Show(this, message, $"{registration.ProviderDisplayName} test unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_isTestingCloudProviderConnection && !string.Equals(_testingRegistrationId, registration.RegistrationId, StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(this, "Wait for the current cloud account test to finish before starting another one.", "Cloud test already running", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var normalizedClientId = registration.ClientId.Trim();
        var normalizedClientSecret = registration.ClientSecret.Trim();
        if (registration.Provider == CloudStorageProvider.GoogleDrive &&
            !TryNormalizeGoogleDriveCredentials(normalizedClientId, normalizedClientSecret, out normalizedClientId, out normalizedClientSecret, out var normalizationErrorMessage))
        {
            registration.ConnectionStatusText = normalizationErrorMessage;
            System.Windows.MessageBox.Show(this, normalizationErrorMessage, "Invalid Google Drive credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isTestingCloudProviderConnection = true;
        _testingRegistrationId = EnsureRegistrationId(registration);
        registration.ConnectionStatusText = $"Testing {registration.AccountDisplayName}...";

        try
        {
            switch (registration.Provider)
            {
                case CloudStorageProvider.Dropbox:
                    await DropboxConnectionTester.TestConnectionAsync(normalizedClientId, normalizedClientSecret, registration.RegistrationId).ConfigureAwait(true);
                    registration.ConnectionStatusText = "Dropbox connection succeeded.";
                    break;
                case CloudStorageProvider.OneDrive:
                    await OneDriveConnectionTester.TestConnectionAsync(normalizedClientId, FixedOneDriveTenantId, registration.RegistrationId).ConfigureAwait(true);
                    registration.ConnectionStatusText = "OneDrive connection succeeded.";
                    break;
                default:
                    await GoogleDriveConnectionTester.TestConnectionAsync(normalizedClientId, normalizedClientSecret, registration.RegistrationId).ConfigureAwait(true);
                    registration.ConnectionStatusText = "Google Drive connection succeeded.";
                    break;
            }
        }
        catch (Exception exception)
        {
            var message = GetCloudProviderTestFailureMessage(registration.Provider, exception.Message);
            registration.ConnectionStatusText = message;
            System.Windows.MessageBox.Show(this, message, $"{registration.ProviderDisplayName} connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isTestingCloudProviderConnection = false;
            _testingRegistrationId = string.Empty;
        }
    }

    private static bool TryNormalizeExistingRegistration(
        CloudProviderAppRegistration registration,
        out CloudProviderAppRegistration normalizedRegistration)
    {
        normalizedRegistration = registration;

        var registrationId = string.IsNullOrWhiteSpace(registration.RegistrationId)
            ? Guid.NewGuid().ToString("N")
            : registration.RegistrationId.Trim();
        var alias = string.IsNullOrWhiteSpace(registration.Alias)
            ? CloudStorageProviderInfo.GetDisplayName(registration.Provider)
            : registration.Alias.Trim();

        normalizedRegistration = new CloudProviderAppRegistration
        {
            RegistrationId = registrationId,
            Provider = registration.Provider,
            Alias = alias,
            ClientId = registration.ClientId?.Trim() ?? string.Empty,
            ClientSecret = registration.Provider == CloudStorageProvider.OneDrive
                ? string.Empty
                : registration.ClientSecret?.Trim() ?? string.Empty,
            TenantId = registration.Provider == CloudStorageProvider.OneDrive
                ? FixedOneDriveTenantId
                : string.Empty,
        };

        return true;
    }

    private static string GetCloudProviderTestFailureMessage(CloudStorageProvider provider, string exceptionMessage)
    {
        if (provider != CloudStorageProvider.Dropbox)
        {
            return exceptionMessage;
        }

        if (exceptionMessage.Contains("folder listing failed", StringComparison.OrdinalIgnoreCase) ||
            exceptionMessage.Contains("missing_scope", StringComparison.OrdinalIgnoreCase) ||
            exceptionMessage.Contains("insufficient_scope", StringComparison.OrdinalIgnoreCase) ||
            exceptionMessage.Contains("not_authorized", StringComparison.OrdinalIgnoreCase))
        {
            return exceptionMessage + " Confirm the Dropbox app Permissions section enables files.metadata.read, files.content.read, files.metadata.write, and files.content.write, then retry sign-in.";
        }

        return exceptionMessage;
    }

    private static ObservableCollection<CloudProviderAppRegistrationViewModel> CreateCloudProviderAppRegistrationItems(
        IReadOnlyList<CloudProviderAppRegistration>? cloudProviderAppRegistrations)
    {
        var items = new ObservableCollection<CloudProviderAppRegistrationViewModel>();
        foreach (var existingRegistration in (cloudProviderAppRegistrations ?? Array.Empty<CloudProviderAppRegistration>())
            .OrderBy(item => item.Provider)
            .ThenBy(item => item.Alias, StringComparer.OrdinalIgnoreCase))
        {
            TryNormalizeExistingRegistration(existingRegistration, out var normalizedRegistration);
            items.Add(new CloudProviderAppRegistrationViewModel(normalizedRegistration.Provider)
            {
                RegistrationId = normalizedRegistration.RegistrationId,
                Alias = normalizedRegistration.Alias,
                ClientId = normalizedRegistration.ClientId,
                ClientSecret = normalizedRegistration.ClientSecret,
                TenantId = normalizedRegistration.Provider == CloudStorageProvider.OneDrive ? FixedOneDriveTenantId : string.Empty,
                ConnectionStatusText = GetCloudProviderConnectionGuidance(true, new CloudProviderAppRegistrationViewModel(normalizedRegistration.Provider)
                {
                    ClientId = normalizedRegistration.ClientId,
                    ClientSecret = normalizedRegistration.ClientSecret,
                    TenantId = normalizedRegistration.TenantId,
                }),
            });
        }

        return items;
    }

    private void OnCloudProviderAppRegistrationItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not CloudProviderAppRegistrationViewModel registration || !IsMeaningfulCloudRegistrationProperty(e.PropertyName))
        {
            return;
        }

        if (registration.Provider == CloudStorageProvider.OneDrive)
        {
            registration.TenantId = FixedOneDriveTenantId;
        }

        registration.ConnectionStatusText = GetCloudProviderConnectionGuidance(UseCustomProviderCredentialsCheckBox.IsChecked == true, registration);
    }

    private static bool TryNormalizeGoogleDriveCredentials(
        string clientId,
        string clientSecret,
        out string normalizedClientId,
        out string normalizedClientSecret,
        out string errorMessage)
    {
        normalizedClientId = clientId;
        normalizedClientSecret = clientSecret;
        errorMessage = string.Empty;

        if (TryParseGoogleDesktopClientJson(clientId, out var parsedClientId, out var parsedClientSecret, out errorMessage))
        {
            normalizedClientId = parsedClientId;
            if (!string.IsNullOrWhiteSpace(parsedClientSecret))
            {
                normalizedClientSecret = parsedClientSecret;
            }
        }
        else if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        if (TryParseGoogleDesktopClientJson(clientSecret, out parsedClientId, out parsedClientSecret, out errorMessage))
        {
            if (!string.IsNullOrWhiteSpace(parsedClientId))
            {
                normalizedClientId = parsedClientId;
            }

            normalizedClientSecret = parsedClientSecret;
        }
        else if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryParseGoogleDesktopClientJson(
        string value,
        out string clientId,
        out string clientSecret,
        out string errorMessage)
    {
        clientId = string.Empty;
        clientSecret = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmedValue = value.Trim();
        if (!trimmedValue.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmedValue);
            if (!document.RootElement.TryGetProperty("installed", out var installedElement))
            {
                errorMessage = "The Google credential JSON must contain an 'installed' object from a Desktop app OAuth client.";
                return false;
            }

            clientId = installedElement.TryGetProperty("client_id", out var clientIdElement)
                ? clientIdElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            clientSecret = installedElement.TryGetProperty("client_secret", out var clientSecretElement)
                ? clientSecretElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                errorMessage = "The Google credential JSON is missing installed.client_id.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            errorMessage = "The Google credential value looks like JSON, but it could not be parsed. Paste either the raw client ID / secret values or the full Desktop app JSON exactly as downloaded from Google.";
            return false;
        }
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

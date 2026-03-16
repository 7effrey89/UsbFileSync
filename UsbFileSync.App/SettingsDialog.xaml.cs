using System.Globalization;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.App;

public partial class SettingsDialog : Window
{
    private const string FixedOneDriveTenantId = "common";

    private enum ConnectionTestStatus
    {
        None,
        Success,
        Failure,
    }

    private static readonly System.Windows.Media.Brush NeutralStatusBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x61, 0x61, 0x61));
    private static readonly System.Windows.Media.Brush SuccessStatusBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1b, 0x5e, 0x20));
    private static readonly System.Windows.Media.Brush ErrorStatusBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xb7, 0x1c, 0x1c));

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

        UseCustomProviderCredentialsCheckBox.Checked += (_, _) =>
        {
            UpdateGoogleDriveConnectionUi();
            UpdateOneDriveConnectionUi();
        };
        UseCustomProviderCredentialsCheckBox.Unchecked += (_, _) =>
        {
            UpdateGoogleDriveConnectionUi();
            UpdateOneDriveConnectionUi();
        };
        Loaded += (_, _) =>
        {
            ParallelCopyCountTextBox.Focus();
            ParallelCopyCountTextBox.SelectAll();
            UpdateGoogleDriveConnectionUi();
            UpdateOneDriveConnectionUi();
        };
    }

    public int ParallelCopyCount { get; private set; }

    public bool HideMacOsSystemFiles { get; private set; }

    public bool UseCustomCloudProviderCredentials { get; private set; }

    public ObservableCollection<PreviewProviderMappingViewModel> PreviewProviderMappingItems { get; }

    public ObservableCollection<CloudProviderAppRegistrationViewModel> CloudProviderAppRegistrationItems { get; }

    public Array ProviderOptions { get; }

    private bool _isTestingGoogleDriveConnection;
    private ConnectionTestStatus _googleDriveTestStatus;
    private string _lastTestedGoogleDriveClientId = string.Empty;
    private string _lastGoogleDriveTestMessage = string.Empty;
    private bool _isTestingOneDriveConnection;
    private ConnectionTestStatus _oneDriveTestStatus;
    private string _lastTestedOneDriveClientId = string.Empty;
    private string _lastTestedOneDriveTenantId = string.Empty;
    private string _lastOneDriveTestMessage = string.Empty;

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

    private async void OnTestGoogleDriveConnectionClicked(object sender, RoutedEventArgs e)
    {
        CloudProviderRegistrationsDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        CloudProviderRegistrationsDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        var registration = GetCloudProviderRegistrationItem(CloudStorageProvider.GoogleDrive);
        if (!CanTestGoogleDriveConnection(UseCustomProviderCredentialsCheckBox.IsChecked == true, CloudProviderAppRegistrationItems))
        {
            var message = GetGoogleDriveConnectionGuidance(UseCustomProviderCredentialsCheckBox.IsChecked == true, registration);
            System.Windows.MessageBox.Show(this, message, "Google Drive test unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            SetGoogleDriveConnectionStatus(message, ErrorStatusBrush);
            return;
        }

        var normalizedClientId = registration!.ClientId.Trim();
        var normalizedClientSecret = registration.ClientSecret.Trim();
        if (!TryNormalizeGoogleDriveCredentials(normalizedClientId, normalizedClientSecret, out normalizedClientId, out normalizedClientSecret, out var normalizationErrorMessage))
        {
            SetGoogleDriveConnectionStatus(normalizationErrorMessage, ErrorStatusBrush);
            System.Windows.MessageBox.Show(this, normalizationErrorMessage, "Invalid Google Drive credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isTestingGoogleDriveConnection = true;
        UpdateGoogleDriveConnectionUi();
        SetGoogleDriveConnectionStatus("Opening Google Drive sign-in in your browser...", NeutralStatusBrush);

        try
        {
            await GoogleDriveConnectionTester.TestConnectionAsync(normalizedClientId, normalizedClientSecret).ConfigureAwait(true);
            _lastTestedGoogleDriveClientId = normalizedClientId;
            _googleDriveTestStatus = ConnectionTestStatus.Success;
            _lastGoogleDriveTestMessage = "Google Drive connection succeeded. The saved client ID can authenticate and open Drive.";
            SetGoogleDriveConnectionStatus(_lastGoogleDriveTestMessage, SuccessStatusBrush);
        }
        catch (Exception exception)
        {
            var message = $"Google Drive connection failed. {exception.Message}";
            _lastTestedGoogleDriveClientId = normalizedClientId;
            _googleDriveTestStatus = ConnectionTestStatus.Failure;
            _lastGoogleDriveTestMessage = message;
            SetGoogleDriveConnectionStatus(message, ErrorStatusBrush);
            System.Windows.MessageBox.Show(this, message, "Google Drive connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isTestingGoogleDriveConnection = false;
            UpdateGoogleDriveConnectionUi();
        }
    }

    private async void OnTestOneDriveConnectionClicked(object sender, RoutedEventArgs e)
    {
        CloudProviderRegistrationsDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        CloudProviderRegistrationsDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        var registration = GetCloudProviderRegistrationItem(CloudStorageProvider.OneDrive);
        if (!CanTestOneDriveConnection(UseCustomProviderCredentialsCheckBox.IsChecked == true, CloudProviderAppRegistrationItems))
        {
            var message = GetOneDriveConnectionGuidance(UseCustomProviderCredentialsCheckBox.IsChecked == true, registration);
            System.Windows.MessageBox.Show(this, message, "OneDrive test unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            SetOneDriveConnectionStatus(message, ErrorStatusBrush);
            return;
        }

        var normalizedClientId = registration!.ClientId.Trim();
        var normalizedTenantId = FixedOneDriveTenantId;

        _isTestingOneDriveConnection = true;
        UpdateOneDriveConnectionUi();
        SetOneDriveConnectionStatus("Opening OneDrive sign-in in your browser...", NeutralStatusBrush);

        try
        {
            await OneDriveConnectionTester.TestConnectionAsync(normalizedClientId, normalizedTenantId).ConfigureAwait(true);
            _lastTestedOneDriveClientId = normalizedClientId;
            _lastTestedOneDriveTenantId = normalizedTenantId;
            _oneDriveTestStatus = ConnectionTestStatus.Success;
            _lastOneDriveTestMessage = "OneDrive connection succeeded. The saved client ID can authenticate and open OneDrive.";
            SetOneDriveConnectionStatus(_lastOneDriveTestMessage, SuccessStatusBrush);
        }
        catch (Exception exception)
        {
            var message = $"OneDrive connection failed. {exception.Message}";
            _lastTestedOneDriveClientId = normalizedClientId;
            _lastTestedOneDriveTenantId = normalizedTenantId;
            _oneDriveTestStatus = ConnectionTestStatus.Failure;
            _lastOneDriveTestMessage = message;
            SetOneDriveConnectionStatus(message, ErrorStatusBrush);
            System.Windows.MessageBox.Show(this, message, "OneDrive connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isTestingOneDriveConnection = false;
            UpdateOneDriveConnectionUi();
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

    public static bool CanTestGoogleDriveConnection(
        bool useCustomCloudProviderCredentials,
        IEnumerable<CloudProviderAppRegistrationViewModel> registrations)
    {
        if (!useCustomCloudProviderCredentials)
        {
            return false;
        }

        var googleDriveRegistration = registrations.FirstOrDefault(item => item.Provider == CloudStorageProvider.GoogleDrive);
        return googleDriveRegistration is not null && !string.IsNullOrWhiteSpace(googleDriveRegistration.ClientId);
    }

    public static string GetGoogleDriveConnectionGuidance(
        bool useCustomCloudProviderCredentials,
        CloudProviderAppRegistrationViewModel? googleDriveRegistration)
    {
        if (!useCustomCloudProviderCredentials)
        {
            return "Turn on 'Use custom provider credentials' before testing Google Drive.";
        }

        if (googleDriveRegistration is null || string.IsNullOrWhiteSpace(googleDriveRegistration.ClientId))
        {
            return "Enter a Google OAuth client ID before testing Google Drive.";
        }

        return string.IsNullOrWhiteSpace(googleDriveRegistration.ClientSecret)
            ? "Google Drive is ready to test. Add a client secret too if your Google OAuth client requires one."
            : "Google Drive is ready to test.";
    }

    public static bool CanTestOneDriveConnection(
        bool useCustomCloudProviderCredentials,
        IEnumerable<CloudProviderAppRegistrationViewModel> registrations)
    {
        if (!useCustomCloudProviderCredentials)
        {
            return false;
        }

        var oneDriveRegistration = registrations.FirstOrDefault(item => item.Provider == CloudStorageProvider.OneDrive);
        return oneDriveRegistration is not null && !string.IsNullOrWhiteSpace(oneDriveRegistration.ClientId);
    }

    public static string GetOneDriveConnectionGuidance(
        bool useCustomCloudProviderCredentials,
        CloudProviderAppRegistrationViewModel? oneDriveRegistration)
    {
        if (!useCustomCloudProviderCredentials)
        {
            return "Turn on 'Use custom provider credentials' before testing OneDrive.";
        }

        if (oneDriveRegistration is null || string.IsNullOrWhiteSpace(oneDriveRegistration.ClientId))
        {
            return "Enter a Microsoft application client ID before testing OneDrive.";
        }

        return $"OneDrive is ready to test. UsbFileSync uses the fixed '{FixedOneDriveTenantId}' tenant.";
    }

    public static bool TryCreateCloudProviderAppRegistrations(
        IEnumerable<CloudProviderAppRegistrationViewModel> registrations,
        out List<CloudProviderAppRegistration> serializedRegistrations,
        out string errorMessage)
    {
        serializedRegistrations = [];
        foreach (var registration in registrations
            .OrderBy(item => item.Provider))
        {
            var clientId = registration.ClientId.Trim();
            var clientSecret = registration.ClientSecret.Trim();
            if (registration.Provider == CloudStorageProvider.GoogleDrive &&
                !TryNormalizeGoogleDriveCredentials(clientId, clientSecret, out clientId, out clientSecret, out errorMessage))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                continue;
            }

            serializedRegistrations.Add(new CloudProviderAppRegistration
            {
                Provider = registration.Provider,
                ClientId = clientId,
                ClientSecret = registration.Provider == CloudStorageProvider.GoogleDrive
                    ? clientSecret
                    : string.Empty,
                TenantId = registration.Provider == CloudStorageProvider.OneDrive
                    ? FixedOneDriveTenantId
                    : string.Empty,
            });
        }

        errorMessage = string.Empty;
        return true;
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

    private static ObservableCollection<CloudProviderAppRegistrationViewModel> CreateCloudProviderAppRegistrationItems(
        IReadOnlyList<CloudProviderAppRegistration>? cloudProviderAppRegistrations)
    {
        var registrationsByProvider = (cloudProviderAppRegistrations ?? Array.Empty<CloudProviderAppRegistration>())
            .GroupBy(item => item.Provider)
            .ToDictionary(group => group.Key, group => group.Last());
        var items = new ObservableCollection<CloudProviderAppRegistrationViewModel>();
        foreach (var provider in Enum.GetValues<CloudStorageProvider>())
        {
            registrationsByProvider.TryGetValue(provider, out var existingRegistration);
            items.Add(new CloudProviderAppRegistrationViewModel(provider)
            {
                ClientId = existingRegistration?.ClientId ?? string.Empty,
                ClientSecret = existingRegistration?.ClientSecret ?? string.Empty,
                TenantId = provider == CloudStorageProvider.OneDrive ? FixedOneDriveTenantId : string.Empty,
            });
        }

        return items;
    }

    private void OnCloudProviderAppRegistrationItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CloudProviderAppRegistrationViewModel.ClientId) or nameof(CloudProviderAppRegistrationViewModel.ClientSecret) or nameof(CloudProviderAppRegistrationViewModel.TenantId))
        {
            ClearGoogleDriveTestStatus();
            ClearOneDriveTestStatus();
            UpdateGoogleDriveConnectionUi();
            UpdateOneDriveConnectionUi();
        }
    }

    private CloudProviderAppRegistrationViewModel? GetCloudProviderRegistrationItem(CloudStorageProvider provider) =>
        CloudProviderAppRegistrationItems.FirstOrDefault(item => item.Provider == provider);

    private void UpdateGoogleDriveConnectionUi()
    {
        var useCustomCloudProviderCredentials = UseCustomProviderCredentialsCheckBox.IsChecked == true;
        var googleDriveRegistration = GetCloudProviderRegistrationItem(CloudStorageProvider.GoogleDrive);
        TestGoogleDriveConnectionButton.IsEnabled = !_isTestingGoogleDriveConnection && CanTestGoogleDriveConnection(useCustomCloudProviderCredentials, CloudProviderAppRegistrationItems);
        TestGoogleDriveConnectionButton.Content = _isTestingGoogleDriveConnection ? "Testing..." : "Test Google Drive";

        if (_isTestingGoogleDriveConnection)
        {
            return;
        }

        var currentGoogleDriveClientId = googleDriveRegistration?.ClientId.Trim() ?? string.Empty;
        if (_googleDriveTestStatus != ConnectionTestStatus.None &&
            useCustomCloudProviderCredentials &&
            string.Equals(currentGoogleDriveClientId, _lastTestedGoogleDriveClientId, StringComparison.Ordinal))
        {
            SetGoogleDriveConnectionStatus(
                _lastGoogleDriveTestMessage,
                _googleDriveTestStatus == ConnectionTestStatus.Success ? SuccessStatusBrush : ErrorStatusBrush);
            return;
        }

        var guidance = GetGoogleDriveConnectionGuidance(useCustomCloudProviderCredentials, googleDriveRegistration);
        SetGoogleDriveConnectionStatus(guidance, NeutralStatusBrush);
    }

    private void ClearGoogleDriveTestStatus()
    {
        _googleDriveTestStatus = ConnectionTestStatus.None;
        _lastTestedGoogleDriveClientId = string.Empty;
        _lastGoogleDriveTestMessage = string.Empty;
    }

    private void SetGoogleDriveConnectionStatus(string message, System.Windows.Media.Brush foreground)
    {
        GoogleDriveConnectionStatusTextBlock.Text = message;
        GoogleDriveConnectionStatusTextBlock.Foreground = foreground;
    }

    private void UpdateOneDriveConnectionUi()
    {
        var useCustomCloudProviderCredentials = UseCustomProviderCredentialsCheckBox.IsChecked == true;
        var oneDriveRegistration = GetCloudProviderRegistrationItem(CloudStorageProvider.OneDrive);
        TestOneDriveConnectionButton.IsEnabled = !_isTestingOneDriveConnection && CanTestOneDriveConnection(useCustomCloudProviderCredentials, CloudProviderAppRegistrationItems);
        TestOneDriveConnectionButton.Content = _isTestingOneDriveConnection ? "Testing..." : "Test OneDrive";

        if (_isTestingOneDriveConnection)
        {
            return;
        }

        var currentClientId = oneDriveRegistration?.ClientId.Trim() ?? string.Empty;
        var currentTenantId = FixedOneDriveTenantId;
        if (_oneDriveTestStatus != ConnectionTestStatus.None &&
            useCustomCloudProviderCredentials &&
            string.Equals(currentClientId, _lastTestedOneDriveClientId, StringComparison.Ordinal) &&
            string.Equals(currentTenantId, _lastTestedOneDriveTenantId, StringComparison.OrdinalIgnoreCase))
        {
            SetOneDriveConnectionStatus(
                _lastOneDriveTestMessage,
                _oneDriveTestStatus == ConnectionTestStatus.Success ? SuccessStatusBrush : ErrorStatusBrush);
            return;
        }

        var guidance = GetOneDriveConnectionGuidance(useCustomCloudProviderCredentials, oneDriveRegistration);
        SetOneDriveConnectionStatus(guidance, NeutralStatusBrush);
    }

    private void ClearOneDriveTestStatus()
    {
        _oneDriveTestStatus = ConnectionTestStatus.None;
        _lastTestedOneDriveClientId = string.Empty;
        _lastTestedOneDriveTenantId = string.Empty;
        _lastOneDriveTestMessage = string.Empty;
    }

    private void SetOneDriveConnectionStatus(string message, System.Windows.Media.Brush foreground)
    {
        OneDriveConnectionStatusTextBlock.Text = message;
        OneDriveConnectionStatusTextBlock.Foreground = foreground;
    }
}

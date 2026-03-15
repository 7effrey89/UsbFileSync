using System.Text.Json.Serialization;

namespace UsbFileSync.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CloudStorageProvider
{
    GoogleDrive,
    Dropbox,
    OneDrive,
}

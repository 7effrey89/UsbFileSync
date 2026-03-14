using System.Text.Json;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Contracts;

public static class SyncWorkerMessageKinds
{
    public const string Start = "start";
    public const string Cancel = "cancel";
    public const string Shutdown = "shutdown";
    public const string Progress = "progress";
    public const string AutoParallelism = "autoParallelism";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Faulted = "faulted";
}

public sealed class SyncWorkerMessage
{
    public string Kind { get; init; } = string.Empty;

    public SyncWorkerRequest? Request { get; init; }

    public SyncProgress? Progress { get; init; }

    public int? EffectiveParallelism { get; init; }

    public SyncResult? Result { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed record SyncWorkerRequest(
    SyncConfiguration Configuration,
    IReadOnlyList<SyncAction> Actions);

public static class SyncWorkerProtocol
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task WriteAsync(StreamWriter writer, SyncWorkerMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(message);

        var json = JsonSerializer.Serialize(message, SerializerOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    public static async Task<SyncWorkerMessage?> ReadAsync(StreamReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(line)
            ? null
            : JsonSerializer.Deserialize<SyncWorkerMessage>(line, SerializerOptions);
    }
}

public sealed class SyncWorkerMessageWriter(StreamWriter writer)
{
    private readonly StreamWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteAsync(SyncWorkerMessage message, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SyncWorkerProtocol.WriteAsync(_writer, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}

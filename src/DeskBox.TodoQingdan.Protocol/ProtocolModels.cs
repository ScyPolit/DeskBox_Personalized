using System.Text.Json.Serialization;

namespace DeskBox.TodoQingdan.Protocol;

public static class BridgeProtocol
{
    public const int Version = 1;
    public const string Handshake = "handshake";
    public const string ReadTasks = "readTasks";
}

public sealed record BridgeRequest(
    int ProtocolVersion,
    string RequestId,
    string Operation,
    long DeadlineUnixMilliseconds);

public sealed record BridgeResponse(
    int ProtocolVersion,
    string RequestId,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    BridgeHandshake? Handshake,
    BridgeTaskSnapshot? Snapshot);

public sealed record BridgeHandshake(
    string ProductVersion,
    string AdapterId,
    string? SchemaFingerprint,
    string ConnectionState,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Risks);

public sealed record BridgeTaskSnapshot(
    string Revision,
    long CapturedAtUnixMilliseconds,
    IReadOnlyList<BridgeTask> Tasks);

public sealed record BridgeTask(
    string Id,
    string Title,
    string? Notes,
    bool IsCompleted,
    long? DueUnixMilliseconds,
    long CreatedUnixMilliseconds,
    long UpdatedUnixMilliseconds,
    double SortOrder);

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BridgeRequest))]
[JsonSerializable(typeof(BridgeResponse))]
[JsonSerializable(typeof(BridgeHandshake))]
[JsonSerializable(typeof(BridgeTaskSnapshot))]
[JsonSerializable(typeof(BridgeTask))]
public sealed partial class BridgeJsonContext : JsonSerializerContext
{
}

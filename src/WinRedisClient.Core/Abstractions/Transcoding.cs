namespace WinRedisClient.Core.Abstractions;

/// <summary>编解码器接口。</summary>
public interface IValueTranscoder
{
    string Id { get; }
    bool CanDecode(ReadOnlyMemory<byte> input);
    ValueTask<DecodeResult> DecodeAsync(
        ReadOnlyMemory<byte> input,
        DecodeOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>解码选项。</summary>
public sealed record DecodeOptions
{
    public long MaxOutputBytes { get; init; } = 10 * 1024 * 1024;
    public string? EncodingHint { get; init; }
}

/// <summary>解码结果。</summary>
public sealed record DecodeResult
{
    public required bool Success { get; init; }
    public string? OutputFormat { get; init; }
    public string? Text { get; init; }
    public bool IsLossy { get; init; }
    public string? Error { get; init; }
}

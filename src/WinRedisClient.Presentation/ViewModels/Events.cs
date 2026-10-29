using WinRedisClient.Core.Localization;

namespace WinRedisClient.Presentation.ViewModels;

public sealed record NotificationMessage(string Message, bool IsError = false);

/// <summary>由 View 层展示确认对话框；VM 等待结果后继续执行。</summary>
public sealed class ConfirmationRequest
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public required string Title { get; init; }
    public required string Message { get; init; }
    public string ConfirmText { get; init; } = Loc.T("确认");
    public string CancelText { get; init; } = Loc.T("取消");
    public bool IsDanger { get; init; } = true;
    /// <summary>要求用户输入的确认文本（高危命令二次授权）。</summary>
    public string? RequiredText { get; init; }

    public Task<bool> WaitAsync() => _completion.Task;
    public void Complete(bool confirmed) => _completion.TrySetResult(confirmed);
}

public sealed record ExportRequest(string SuggestedFileName, string Content);

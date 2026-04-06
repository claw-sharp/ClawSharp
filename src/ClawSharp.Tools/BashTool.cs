using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

internal sealed class BashTool : BaseTool
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public BashTool()
        : base(
            new ToolDescriptor(
                "Bash",
                "Run shell commands",
                IsLongRunningCapable: true,
                SearchHint: "execute shell commands",
                InputSchema: ShellToolSchemas.CreateShellInputSchema(!ShellToolExecutionSupport.AreBackgroundTasksDisabled()),
                OutputSchema: ShellToolSchemas.ShellOutputSchema,
                Strict: true))
    {
    }

    public override string? RenderToolUseMessage(string arguments)
    {
        if (!TryParseArguments(arguments, out var request, out _) || request is null)
        {
            return null;
        }

        return ToolUseRenderFormatting.TruncateCommand(request.Command!);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Failure(errorMessage ?? "Bash arguments must be a JSON object with command.");
        }

        if (ShellToolExecutionSupport.AreBackgroundTasksDisabled() && request.RunInBackground == true)
        {
            return Failure("Background tasks are disabled.");
        }

        var permissionResolution = await ShellToolPermissionRuntime.ResolveAsync(
            Descriptor.Name,
            request.Command!,
            context,
            request.DangerouslyDisableSandbox == true,
            caseInsensitive: false,
            cancellationToken).ConfigureAwait(false);
        if (!permissionResolution.Allowed)
        {
            return Failure(permissionResolution.Message ?? "Bash permission denied.");
        }

        var shellPath = await BashShellDetection.FindSuitableShellAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            var output = new ShellToolOutput(
                string.Empty,
                "No suitable bash or zsh shell was found.",
                Interrupted: false,
                DangerouslyDisableSandbox: request.DangerouslyDisableSandbox);
            return Success(
                ShellToolExecutionSupport.BuildToolResultContent(output),
                ShellToolExecutionSupport.CreateStructuredOutput(output));
        }

        return request.RunInBackground == true
            ? await ExecuteBackgroundAsync(context, request, shellPath, cancellationToken).ConfigureAwait(false)
            : await ExecuteForegroundAsync(context, request, shellPath, cancellationToken).ConfigureAwait(false);
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "Bash arguments must be a JSON object with command."));
        }

        if (ShellToolExecutionSupport.AreBackgroundTasksDisabled() && request.RunInBackground == true)
        {
            return Task.FromResult(ToolValidationResult.Invalid("Background tasks are disabled."));
        }

        var permissionDecision = ShellPermissionEvaluator.Evaluate(
            Descriptor.Name,
            request.Command!,
            context.ToolPermissionContext,
            request.DangerouslyDisableSandbox == true,
            caseInsensitive: false);
        if (permissionDecision.Behavior == FileToolPermissionBehavior.Deny ||
            (permissionDecision.Behavior == FileToolPermissionBehavior.Ask &&
             context.ToolPermissionContext.ShouldAvoidPermissionPrompts == true))
        {
            return Task.FromResult(ToolValidationResult.Invalid(permissionDecision.Message ?? "Bash permission denied."));
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    private async Task<ToolExecutionResult> ExecuteForegroundAsync(
        ToolExecutionContext context,
        ShellToolRequest request,
        string shellPath,
        CancellationToken cancellationToken)
    {
        var timeoutMs = request.Timeout ?? ShellToolExecutionSupport.DefaultTimeoutMs;
        var outputPath = TaskOutputStoragePaths.GetTaskOutputPath(
            context.WorkspaceRoot,
            context.Session.Id,
            $"shell-{Guid.NewGuid():N}");
        await new DiskTaskOutputStore().InitTaskOutputAsync(outputPath, cancellationToken).ConfigureAwait(false);

        var taskId = $"b{Guid.NewGuid():N}"[..9];
        var stopwatch = Stopwatch.StartNew();
        var taskOutput = new TaskOutput(
            taskId,
            outputPath,
            ShellToolExecutionSupport.CreateProgressCallback(context, Descriptor.Name, timeoutMs, taskId, stopwatch),
            stdoutToFile: true,
            managedFileWrites: true);
        var startInfo = new LocalShellProcessStartInfo(
            shellPath,
            ["-lc", request.Command!],
            context.Session.ProjectDirectory,
            taskOutput,
            timeoutMs);

        var runner = new LocalShellProcessRunner();
        var shellCommand = await runner.StartAsync(startInfo, cancellationToken).ConfigureAwait(false);

        var autoBackgroundCompletion = new TaskCompletionSource<LocalShellExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ShellToolExecutionSupport.ShouldAssistantAutoBackground(context, ShellToolKind.Bash, request.Command!))
        {
            shellCommand.OnTimeout(
                background =>
                {
                    _ = background;
                    _ = TryAutoBackgroundForegroundAsync(
                        context,
                        request,
                        shellCommand,
                        autoBackgroundCompletion,
                        cancellationToken);
                });
        }

        var result = await AwaitForegroundResultAsync(
            shellCommand.Result,
            autoBackgroundCompletion.Task,
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var interpretation = ShellCommandSemantics.Interpret(
            ShellToolKind.Bash,
            request.Command!,
            result.Code,
            result.Stdout,
            result.Stderr);
        var persistedOutput = result.OutputFilePath is not null && result.OutputTaskId is not null
            ? await ShellToolResultStorage.TryPersistLargeOutputAsync(
                    context.Session,
                    result.OutputFilePath,
                    result.OutputTaskId,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;

        var output = new ShellToolOutput(
            result.Stdout,
            result.Stderr,
            result.Interrupted,
            BackgroundTaskId: result.BackgroundTaskId,
            BackgroundedByUser: result.BackgroundedByUser,
            AssistantAutoBackgrounded: result.AssistantAutoBackgrounded,
            DangerouslyDisableSandbox: request.DangerouslyDisableSandbox,
            ReturnCodeInterpretation: interpretation.Message,
            PersistedOutputPath: persistedOutput?.FilePath,
            PersistedOutputSize: persistedOutput?.OriginalSize,
            NoOutputExpected: ShellCommandSemantics.IsSilentBashCommand(request.Command!) ? true : null);

        return Success(
            ShellToolExecutionSupport.BuildToolResultContent(output, ResolveBackgroundOutputPath(context, result.BackgroundTaskId)),
            ShellToolExecutionSupport.CreateStructuredOutput(output));
    }

    private async Task<ToolExecutionResult> ExecuteBackgroundAsync(
        ToolExecutionContext context,
        ShellToolRequest request,
        string shellPath,
        CancellationToken cancellationToken)
    {
        var timeoutMs = request.Timeout ?? ShellToolExecutionSupport.DefaultTimeoutMs;
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? request.Command!
            : request.Description.Trim();
        var task = await context.Tasks.CreateLocalBashForSessionAsync(
            context.Session.Id,
            description,
            request.Command!,
            ClawSharp.Tasks.TaskStatus.Running,
            kind: BashTaskKind.Bash,
            isBackgrounded: false,
            agentId: ShellToolExecutionSupport.ResolveOwningAgentId(context),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        var taskOutput = new TaskOutput(
            task.Id,
            task.OutputFile,
            ShellToolExecutionSupport.CreateProgressCallback(context, Descriptor.Name, timeoutMs, task.Id, stopwatch),
            stdoutToFile: true,
            managedFileWrites: true);
        var startInfo = new LocalShellProcessStartInfo(
            shellPath,
            ["-lc", request.Command!],
            context.Session.ProjectDirectory,
            taskOutput,
            timeoutMs);

        var runner = new LocalShellProcessRunner();
        var shellCommand = await runner.StartAsync(startInfo, cancellationToken).ConfigureAwait(false);
        context.Tasks.TryAttachLocalShellCommand(task.Id, shellCommand);

        if (!context.Tasks.TryBackgroundLocalBashTask(task.Id))
        {
            var result = await shellCommand.Result.WaitAsync(cancellationToken).ConfigureAwait(false);
            await context.Tasks.TrackLocalBashCommandAsync(task.Id, shellCommand, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var interpretation = ShellCommandSemantics.Interpret(
                ShellToolKind.Bash,
                request.Command!,
                result.Code,
                result.Stdout,
                result.Stderr);
            var persistedOutput = result.OutputFilePath is not null && result.OutputTaskId is not null
                ? await ShellToolResultStorage.TryPersistLargeOutputAsync(
                        context.Session,
                        result.OutputFilePath,
                        result.OutputTaskId,
                        cancellationToken)
                    .ConfigureAwait(false)
                : null;
            var output = new ShellToolOutput(
                result.Stdout,
                result.Stderr,
                result.Interrupted,
                DangerouslyDisableSandbox: request.DangerouslyDisableSandbox,
                ReturnCodeInterpretation: interpretation.Message,
                PersistedOutputPath: persistedOutput?.FilePath,
                PersistedOutputSize: persistedOutput?.OriginalSize,
                NoOutputExpected: ShellCommandSemantics.IsSilentBashCommand(request.Command!) ? true : null);
            return Success(
                ShellToolExecutionSupport.BuildToolResultContent(output),
                ShellToolExecutionSupport.CreateStructuredOutput(output));
        }

        _ = context.Tasks.TrackLocalBashCommandAsync(task.Id, shellCommand);
        stopwatch.Stop();

        var backgroundOutput = new ShellToolOutput(
            string.Empty,
            string.Empty,
            Interrupted: false,
            BackgroundTaskId: task.Id,
            DangerouslyDisableSandbox: request.DangerouslyDisableSandbox);
        return Success(
            ShellToolExecutionSupport.BuildToolResultContent(backgroundOutput, task.OutputFile),
            ShellToolExecutionSupport.CreateStructuredOutput(backgroundOutput));
    }

    private static async Task TryAutoBackgroundForegroundAsync(
        ToolExecutionContext context,
        ShellToolRequest request,
        LocalShellCommand shellCommand,
        TaskCompletionSource<LocalShellExecutionResult> completionSource,
        CancellationToken cancellationToken)
    {
        try
        {
            var description = string.IsNullOrWhiteSpace(request.Description)
                ? request.Command!
                : request.Description.Trim();
            var task = await context.Tasks.CreateLocalBashForExistingOutputAsync(
                context.Session.Id,
                description,
                request.Command!,
                shellCommand.TaskOutput.Path,
                ClawSharp.Tasks.TaskStatus.Running,
                kind: BashTaskKind.Bash,
                isBackgrounded: false,
                agentId: ShellToolExecutionSupport.ResolveOwningAgentId(context),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!context.Tasks.TryAttachLocalShellCommand(task.Id, shellCommand) ||
                !context.Tasks.TryBackgroundLocalBashTask(task.Id))
            {
                return;
            }

            _ = context.Tasks.TrackLocalBashCommandAsync(task.Id, shellCommand);
            completionSource.TrySetResult(
                new LocalShellExecutionResult(
                    string.Empty,
                    string.Empty,
                    0,
                    Interrupted: false,
                    BackgroundTaskId: task.Id,
                    AssistantAutoBackgrounded: true));
        }
        catch
        {
        }
    }

    private static async Task<LocalShellExecutionResult> AwaitForegroundResultAsync(
        Task<LocalShellExecutionResult> foregroundResultTask,
        Task<LocalShellExecutionResult> autoBackgroundTask,
        CancellationToken cancellationToken)
    {
        var completedTask = await Task.WhenAny(foregroundResultTask, autoBackgroundTask).WaitAsync(cancellationToken).ConfigureAwait(false);
        return await completedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? ResolveBackgroundOutputPath(ToolExecutionContext context, string? backgroundTaskId)
    {
        if (string.IsNullOrWhiteSpace(backgroundTaskId))
        {
            return null;
        }

        return context.Tasks.TryGet(backgroundTaskId, out var task)
            ? task?.OutputFile
            : null;
    }

    private static bool TryParseArguments(string arguments, out ShellToolRequest? request, out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        try
        {
            request = JsonSerializer.Deserialize<ShellToolRequest>(arguments, SerializerOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.Command))
            {
                errorMessage = "Bash arguments must include command.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            errorMessage = "Bash arguments must be a JSON object with command.";
            return false;
        }
    }

    private sealed record ShellToolRequest(
        [property: JsonPropertyName("command")] string? Command,
        [property: JsonPropertyName("timeout")] int? Timeout,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("run_in_background")] bool? RunInBackground,
        [property: JsonPropertyName("dangerouslyDisableSandbox")] bool? DangerouslyDisableSandbox);
}

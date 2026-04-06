using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

internal sealed class PowerShellTool : BaseTool
{
    private const string WindowsSandboxPolicyRefusal =
        "Enterprise policy requires sandboxing, but sandboxing is not available on native Windows. Shell command execution is blocked on this platform by policy.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PowerShellTool()
        : base(
            new ToolDescriptor(
                "PowerShell",
                "Run PowerShell command",
                IsLongRunningCapable: true,
                SearchHint: "execute Windows PowerShell commands",
                InputSchema: ShellToolSchemas.CreateShellInputSchema(!ShellToolExecutionSupport.AreBackgroundTasksDisabled()),
                OutputSchema: ShellToolSchemas.ShellOutputSchema,
                Strict: true))
    {
    }

    public override bool IsEnabled()
    {
        return PowerShellToolFeatureGate.IsEnabled();
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
            return Failure(errorMessage ?? "PowerShell arguments must be a JSON object with command.");
        }

        if (ShellToolExecutionSupport.AreBackgroundTasksDisabled() && request.RunInBackground == true)
        {
            return Failure("Background tasks are disabled.");
        }

        if (IsWindowsSandboxPolicyViolation(context.ToolPermissionContext))
        {
            return Failure(WindowsSandboxPolicyRefusal);
        }

        var permissionResolution = await ShellToolPermissionRuntime.ResolveAsync(
            Descriptor.Name,
            request.Command!,
            context,
            request.DangerouslyDisableSandbox == true,
            caseInsensitive: true,
            cancellationToken).ConfigureAwait(false);
        if (!permissionResolution.Allowed)
        {
            return Failure(permissionResolution.Message ?? "PowerShell permission denied.");
        }

        var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(powerShellPath))
        {
            var output = new ShellToolOutput(
                string.Empty,
                "PowerShell is not available on this system.",
                Interrupted: false,
                DangerouslyDisableSandbox: request.DangerouslyDisableSandbox);
            return Success(
                ShellToolExecutionSupport.BuildToolResultContent(output),
                ShellToolExecutionSupport.CreateStructuredOutput(output));
        }

        return request.RunInBackground == true
            ? await ExecuteBackgroundAsync(context, request, powerShellPath, cancellationToken).ConfigureAwait(false)
            : await ExecuteForegroundAsync(context, request, powerShellPath, cancellationToken).ConfigureAwait(false);
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "PowerShell arguments must be a JSON object with command."));
        }

        if (ShellToolExecutionSupport.AreBackgroundTasksDisabled() && request.RunInBackground == true)
        {
            return Task.FromResult(ToolValidationResult.Invalid("Background tasks are disabled."));
        }

        if (IsWindowsSandboxPolicyViolation(context.ToolPermissionContext))
        {
            return Task.FromResult(ToolValidationResult.Invalid(WindowsSandboxPolicyRefusal));
        }

        var permissionDecision = ShellPermissionEvaluator.Evaluate(
            Descriptor.Name,
            request.Command!,
            context.ToolPermissionContext,
            request.DangerouslyDisableSandbox == true,
            caseInsensitive: true);
        if (permissionDecision.Behavior == FileToolPermissionBehavior.Deny ||
            (permissionDecision.Behavior == FileToolPermissionBehavior.Ask &&
             context.ToolPermissionContext.ShouldAvoidPermissionPrompts == true))
        {
            return Task.FromResult(ToolValidationResult.Invalid(permissionDecision.Message ?? "PowerShell permission denied."));
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    private static bool IsWindowsSandboxPolicyViolation(ToolPermissionContext permissionContext)
    {
        return OperatingSystem.IsWindows() &&
               permissionContext.IsSandboxEnabledInSettings &&
               !permissionContext.AreUnsandboxedCommandsAllowed;
    }

    private async Task<ToolExecutionResult> ExecuteForegroundAsync(
        ToolExecutionContext context,
        ShellToolRequest request,
        string powerShellPath,
        CancellationToken cancellationToken)
    {
        var timeoutMs = request.Timeout ?? ShellToolExecutionSupport.DefaultTimeoutMs;
        var outputPath = TaskOutputStoragePaths.GetTaskOutputPath(
            context.WorkspaceRoot,
            context.Session.Id,
            $"ps-{Guid.NewGuid():N}");
        await new DiskTaskOutputStore().InitTaskOutputAsync(outputPath, cancellationToken).ConfigureAwait(false);

        var taskId = $"p{Guid.NewGuid():N}"[..9];
        var stopwatch = Stopwatch.StartNew();
        var taskOutput = new TaskOutput(
            taskId,
            outputPath,
            ShellToolExecutionSupport.CreateProgressCallback(context, Descriptor.Name, timeoutMs, taskId, stopwatch),
            stdoutToFile: true,
            managedFileWrites: true);

        var provider = new PowerShellShellProvider(powerShellPath);
        var execCommand = provider.BuildExecCommand(request.Command!, Guid.NewGuid().ToString("N"));
        var startInfo = new LocalShellProcessStartInfo(
            powerShellPath,
            provider.GetSpawnArguments(execCommand.CommandString),
            context.Session.ProjectDirectory,
            taskOutput,
            timeoutMs,
            provider.GetEnvironmentOverrides());

        var runner = new LocalShellProcessRunner();
        var shellCommand = await runner.StartAsync(startInfo, cancellationToken).ConfigureAwait(false);

        var autoBackgroundCompletion = new TaskCompletionSource<LocalShellExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ShellToolExecutionSupport.ShouldAssistantAutoBackground(context, ShellToolKind.PowerShell, request.Command!))
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
            ShellToolKind.PowerShell,
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
            PersistedOutputSize: persistedOutput?.OriginalSize);

        return Success(
            ShellToolExecutionSupport.BuildToolResultContent(output, ResolveBackgroundOutputPath(context, result.BackgroundTaskId)),
            ShellToolExecutionSupport.CreateStructuredOutput(output));
    }

    private async Task<ToolExecutionResult> ExecuteBackgroundAsync(
        ToolExecutionContext context,
        ShellToolRequest request,
        string powerShellPath,
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
        var provider = new PowerShellShellProvider(powerShellPath);
        var execCommand = provider.BuildExecCommand(request.Command!, Guid.NewGuid().ToString("N"));
        var startInfo = new LocalShellProcessStartInfo(
            powerShellPath,
            provider.GetSpawnArguments(execCommand.CommandString),
            context.Session.ProjectDirectory,
            taskOutput,
            timeoutMs,
            provider.GetEnvironmentOverrides());

        var runner = new LocalShellProcessRunner();
        var shellCommand = await runner.StartAsync(startInfo, cancellationToken).ConfigureAwait(false);
        context.Tasks.TryAttachLocalShellCommand(task.Id, shellCommand);

        if (!context.Tasks.TryBackgroundLocalBashTask(task.Id))
        {
            var result = await shellCommand.Result.WaitAsync(cancellationToken).ConfigureAwait(false);
            await context.Tasks.TrackLocalBashCommandAsync(task.Id, shellCommand, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var interpretation = ShellCommandSemantics.Interpret(
                ShellToolKind.PowerShell,
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
                PersistedOutputSize: persistedOutput?.OriginalSize);
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
            BackgroundTaskId: task.Id);
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
                errorMessage = "PowerShell arguments must include command.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            errorMessage = "PowerShell arguments must be a JSON object with command.";
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

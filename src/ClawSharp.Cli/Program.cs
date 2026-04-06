using ClawSharp.Bridge;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

StartupProfiler.Checkpoint("cli_entry");
try
{
    var argsList = args.ToList();
    if (IsHelpRequest(argsList))
    {
        Console.WriteLine(CliCommandText.GetTopLevelHelpText());
        StartupProfiler.Checkpoint("main_after_run");
        StartupProfiler.Report();
        return 0;
    }

    var bridgeChildParse = BridgeChildSessionArgumentUtilities.Parse(argsList);
    if (bridgeChildParse.IsBridgeChildMode)
    {
        if (!string.IsNullOrWhiteSpace(bridgeChildParse.Error))
        {
            Console.Error.WriteLine($"Error: {bridgeChildParse.Error}");
            return 1;
        }

        StartupProfiler.Checkpoint("bridge_child_runtime_start");
        var bridgeChildExitCode = await BridgeChildSessionRuntime.RunAsync(
            bridgeChildParse.Options!,
            Console.In,
            Console.Out,
            Console.Error);
        StartupProfiler.Checkpoint("main_after_run");
        StartupProfiler.Report();
        return bridgeChildExitCode;
    }

    if (TryGetOptionValue(argsList, "--handle-uri", out var deepLinkUri))
    {
        var deepLinkLauncher = new DeepLinkTerminalLaunchService(
            new DeepLinkProtocolService(),
            new GitHubRepoPathMappingService());
        var executablePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var deepLinkResult = await deepLinkLauncher.HandleUriAsync(deepLinkUri, executablePath);
        if (!deepLinkResult.Success)
        {
            Console.Error.WriteLine(deepLinkResult.Error ?? "Failed to handle deep link.");
            return 1;
        }

        StartupProfiler.Checkpoint("main_after_run");
        StartupProfiler.Report();
        return 0;
    }

    if (argsList.Count > 0 && BridgeCliEntrypoint.IsBridgeCommand(argsList[0]))
    {
        var bridgeExitCode = await BridgeCliEntrypoint.RunAsync(
            argsList.Skip(1).ToArray(),
            Console.Out,
            Console.Error);
        StartupProfiler.Checkpoint("main_after_run");
        StartupProfiler.Report();
        return bridgeExitCode;
    }

    var deepLinkRuntimeContext = ParseDeepLinkRuntimeContext(argsList);
    var providerFlagError = ProviderFlagUtilities.ApplyProviderFlags(argsList);
    if (!string.IsNullOrWhiteSpace(providerFlagError))
    {
        Console.Error.WriteLine(providerFlagError);
        return 1;
    }

    var visibleArgs = ProviderFlagUtilities.StripProviderFlags(StripDeepLinkRuntimeArgs(argsList));

    if (TryHandleStaticTopLevelCommand(visibleArgs, out var staticCommandExitCode))
    {
        StartupProfiler.Checkpoint("main_after_run");
        StartupProfiler.Report();
        return staticCommandExitCode;
    }

    var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
    StartupProfiler.Checkpoint("application_factory_end");
    ApplyCliModelOverride(app.AppStateStore, argsList);

    if (ShouldRunRepl(visibleArgs))
    {
        StartupProfiler.Checkpoint("repl_bootstrap_start");
        var replArgs = visibleArgs.Count > 0 && string.Equals(visibleArgs[0], "repl", StringComparison.OrdinalIgnoreCase)
            ? visibleArgs.Skip(1).ToArray()
            : visibleArgs.ToArray();

        var (success, initialSession, error) = await app.ReplSessionBootstrapper.ResolveAsync(replArgs);
        StartupProfiler.Checkpoint("repl_bootstrap_end");
        if (!success)
        {
            Console.WriteLine(error);
            return 1;
        }

        ClawSharpTelemetry.UpdateSessionId(initialSession?.Id);
        var exitCode = await app.TerminalShell.RunReplAsync(
            Console.In,
            Console.Out,
            initialSession,
            deepLinkRuntimeContext);
        StartupProfiler.Checkpoint("main_after_run");
        StartupProfiler.Report(initialSession?.Id);
        return exitCode;
    }

    if (visibleArgs.Count == 1 && string.Equals(visibleArgs[0], "tasks", StringComparison.OrdinalIgnoreCase))
    {
        var tasks = app.Tasks.GetAll();
        if (tasks.Count == 0)
        {
            Console.WriteLine("No tasks registered.");
            StartupProfiler.Checkpoint("main_after_run");
            StartupProfiler.Report();
            return 0;
        }

        foreach (var task in tasks)
        {
            Console.WriteLine($"{task.Id} [{task.Status}] {task.Description}");
        }

        StartupProfiler.Checkpoint("main_after_run");
        StartupProfiler.Report();
        return 0;
    }

    Console.WriteLine(CliCommandText.GetTopLevelHelpText());
    StartupProfiler.Checkpoint("main_after_run");
    StartupProfiler.Report();
    return 1;
}
finally
{
    StartupProfiler.Report();
    await ClawSharpTelemetry.FlushAsync();
}

static bool ShouldRunRepl(IReadOnlyList<string> argsList)
{
    return argsList.Count == 0 ||
           string.Equals(argsList[0], "repl", StringComparison.OrdinalIgnoreCase) ||
           argsList[0] is "--continue" or "-c" or "--resume" or "-r" or "--name" or "-n" or "--provider" or "--model";
}

static bool IsHelpRequest(IReadOnlyList<string> argsList)
{
    return argsList.Count == 1 &&
           (argsList[0] is "--help" or "-h" ||
            string.Equals(argsList[0], "help", StringComparison.OrdinalIgnoreCase));
}

static bool TryHandleStaticTopLevelCommand(IReadOnlyList<string> argsList, out int exitCode)
{
    if (argsList.Count == 1 && (argsList[0] is "--version" or "-v" or "-V"))
    {
        Console.WriteLine(AppMetadata.DisplayVersion);
        exitCode = 0;
        return true;
    }

    if (argsList.Count == 1 && string.Equals(argsList[0], "update", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(CliCommandText.GetUpdateText());
        exitCode = 0;
        return true;
    }

    if (argsList.Count == 2 && string.Equals(argsList[0], "completion", StringComparison.OrdinalIgnoreCase))
    {
        if (!CliCommandText.TryGetCompletionScript(argsList[1], out var script))
        {
            Console.Error.WriteLine(CliCommandText.GetCompletionUsageText());
            exitCode = 1;
            return true;
        }

        Console.WriteLine(script);
        exitCode = 0;
        return true;
    }

    exitCode = 1;
    return false;
}

static bool TryGetOptionValue(IReadOnlyList<string> argsList, string optionName, out string value)
{
    for (var index = 0; index < argsList.Count; index++)
    {
        if (!string.Equals(argsList[index], optionName, StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 < argsList.Count)
        {
            value = argsList[index + 1];
            return true;
        }

        break;
    }

    value = string.Empty;
    return false;
}

static DeepLinkRuntimeContext? ParseDeepLinkRuntimeContext(IReadOnlyList<string> argsList)
{
    var isOrigin = argsList.Any(static argument => string.Equals(argument, "--deep-link-origin", StringComparison.Ordinal));
    if (!isOrigin)
    {
        return null;
    }

    TryGetOptionValue(argsList, "--deep-link-draft", out var draftPrompt);
    TryGetOptionValue(argsList, "--deep-link-repo", out var repo);
    return new DeepLinkRuntimeContext(
        true,
        string.IsNullOrWhiteSpace(draftPrompt) ? null : draftPrompt,
        string.IsNullOrWhiteSpace(repo) ? null : repo);
}

static List<string> StripDeepLinkRuntimeArgs(IReadOnlyList<string> argsList)
{
    var stripped = new List<string>();
    for (var index = 0; index < argsList.Count; index++)
    {
        switch (argsList[index])
        {
            case "--deep-link-origin":
                break;
            case "--deep-link-draft":
            case "--deep-link-repo":
                index++;
                break;
            default:
                stripped.Add(argsList[index]);
                break;
        }
    }

    return stripped;
}

static void ApplyCliModelOverride(IClawSharpAppStateStore appStateStore, IReadOnlyList<string> argsList)
{
    var provider = ProviderFlagUtilities.ParseProviderFlag(argsList);
    var model = ProviderFlagUtilities.ParseModelFlag(argsList);
    if (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(model))
    {
        return;
    }

    var selectedModel = string.IsNullOrWhiteSpace(model)
        ? ProviderRuntimeResolver.GetDefaultModelForCurrentProvider()
        : model.Trim();
    appStateStore.SetState(state => ClawSharpAppStateMutations.WithMainLoopModel(state, selectedModel));
}

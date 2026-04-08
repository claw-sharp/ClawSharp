using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using Microsoft.Playwright;

namespace ClawSharp.Tools;

internal sealed class WebBrowserTool : BaseTool
{
    public const string ToolName = "WebBrowser";

    public WebBrowserTool()
        : base(
            new ToolDescriptor(
                ToolName,
                "Browser automation for development tasks such as loading pages, clicking elements, typing text, evaluating JavaScript, reading console logs, and taking screenshots.",
                SearchHint: "browser automation for dev servers, JS eval, console logs, and screenshots",
                ShouldDefer: true,
                InputSchema: WebBrowserToolSchemas.InputSchema,
                OutputSchema: WebBrowserToolSchemas.OutputSchema,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return false;
    }

    public override bool IsReadOnly(string arguments)
    {
        return WebBrowserToolInputParser.TryParse(arguments, out var input, out _) &&
               input is not null &&
               input.Action is WebBrowserAction.Info or WebBrowserAction.Console;
    }

    public override async Task<ToolExecutionResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!WebBrowserToolInputParser.TryParse(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return ToolValidationResult.Invalid(errorMessage ?? "Invalid WebBrowser input.");
        }

        if ((input.Action is WebBrowserAction.Open or WebBrowserAction.Navigate) &&
            string.IsNullOrWhiteSpace(input.Url))
        {
            return ToolValidationResult.Invalid("'url' is required for open and navigate.");
        }

        if ((input.Action is WebBrowserAction.Click or WebBrowserAction.Type) &&
            string.IsNullOrWhiteSpace(input.Selector))
        {
            return ToolValidationResult.Invalid("'selector' is required for click and type.");
        }

        if (input.Action is WebBrowserAction.Type && input.Text is null)
        {
            return ToolValidationResult.Invalid("'text' is required for type.");
        }

        if (input.Action is WebBrowserAction.Evaluate && string.IsNullOrWhiteSpace(input.Script))
        {
            return ToolValidationResult.Invalid("'script' is required for evaluate.");
        }

        if (input.Url is not null)
        {
            try
            {
                ClawSharp.Infrastructure.BrowserLauncher.ValidateUrl(input.Url);
            }
            catch (Exception ex)
            {
                return ToolValidationResult.Invalid(ex.Message);
            }
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!WebBrowserToolInputParser.TryParse(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Failure(errorMessage ?? "Invalid WebBrowser input.");
        }

        try
        {
            var result = await WebBrowserRuntime.ExecuteAsync(input, context, cancellationToken).ConfigureAwait(false);
            return Success(result.Message, result.StructuredOutput);
        }
        catch (Exception ex)
        {
            return Failure($"WebBrowser {input.Action.ToWireValue()} failed: {ex.Message}");
        }
    }
}

internal static class WebBrowserToolSchemas
{
    public static JsonObject InputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("action", ToolJsonSchemaFactory.StringEnum(
                    [
                        "open",
                        "navigate",
                        "click",
                        "type",
                        "evaluate",
                        "console",
                        "screenshot",
                        "info",
                        "close"
                    ])),
                ("url", ToolJsonSchemaFactory.String("URL for open or navigate", Required: false)),
                ("selector", ToolJsonSchemaFactory.String("CSS selector for click or type", Required: false)),
                ("text", ToolJsonSchemaFactory.String("Text for type", Required: false)),
                ("script", ToolJsonSchemaFactory.String("JavaScript expression or function body for evaluate", Required: false)),
                ("fullPage", ToolJsonSchemaFactory.Boolean("Whether screenshot captures the full page", defaultValue: true, Required: false)),
                ("waitMs", ToolJsonSchemaFactory.Integer("Optional wait time before executing the action", minimum: 0, defaultValue: 0, Required: false))
            ],
            required: ["action"]);

    public static JsonObject OutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("action", ToolJsonSchemaFactory.String()),
                ("url", ToolJsonSchemaFactory.String("Current page URL", Required: false)),
                ("title", ToolJsonSchemaFactory.String("Current page title", Required: false)),
                ("result", ToolJsonSchemaFactory.String("Action result text", Required: false)),
                ("screenshotPath", ToolJsonSchemaFactory.String("Absolute path to a saved screenshot", Required: false)),
                ("consoleMessages", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String(), description: "Captured console messages", Required: false))
            ],
            required: ["action"]);
}

internal static class WebBrowserToolInputParser
{
    public static bool TryParse(string arguments, out WebBrowserInput? input, out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        try
        {
            var root = JsonNode.Parse(arguments)?.AsObject();
            if (root is null)
            {
                errorMessage = "Arguments must be a JSON object.";
                return false;
            }

            var actionText = root["action"]?.GetValue<string>() ?? string.Empty;
            if (!WebBrowserActionParser.TryParse(actionText, out var action))
            {
                errorMessage = "Invalid WebBrowser action.";
                return false;
            }

            input = new WebBrowserInput(
                action,
                root["url"]?.GetValue<string>(),
                root["selector"]?.GetValue<string>(),
                root["text"]?.GetValue<string>(),
                root["script"]?.GetValue<string>(),
                root["fullPage"]?.GetValue<bool?>(),
                root["waitMs"]?.GetValue<int?>());
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}

internal sealed record WebBrowserInput(
    WebBrowserAction Action,
    string? Url,
    string? Selector,
    string? Text,
    string? Script,
    bool? FullPage,
    int? WaitMs);

internal enum WebBrowserAction
{
    Open,
    Navigate,
    Click,
    Type,
    Evaluate,
    Console,
    Screenshot,
    Info,
    Close
}

internal static class WebBrowserActionParser
{
    public static bool TryParse(string value, out WebBrowserAction action)
    {
        action = value switch
        {
            "open" => WebBrowserAction.Open,
            "navigate" => WebBrowserAction.Navigate,
            "click" => WebBrowserAction.Click,
            "type" => WebBrowserAction.Type,
            "evaluate" => WebBrowserAction.Evaluate,
            "console" => WebBrowserAction.Console,
            "screenshot" => WebBrowserAction.Screenshot,
            "info" => WebBrowserAction.Info,
            "close" => WebBrowserAction.Close,
            _ => default
        };

        return value is "open" or "navigate" or "click" or "type" or "evaluate" or "console" or "screenshot" or "info" or "close";
    }

    public static string ToWireValue(this WebBrowserAction action)
    {
        return action switch
        {
            WebBrowserAction.Open => "open",
            WebBrowserAction.Navigate => "navigate",
            WebBrowserAction.Click => "click",
            WebBrowserAction.Type => "type",
            WebBrowserAction.Evaluate => "evaluate",
            WebBrowserAction.Console => "console",
            WebBrowserAction.Screenshot => "screenshot",
            WebBrowserAction.Info => "info",
            WebBrowserAction.Close => "close",
            _ => action.ToString()
        };
    }
}

internal sealed record WebBrowserExecutionResult(string Message, JsonObject StructuredOutput);

internal static class WebBrowserRuntime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private static IBrowserContext? _context;
    private static IPage? _page;
    private static readonly List<string> ConsoleMessages = [];
    private const int MaxConsoleMessages = 100;

    public static async Task<WebBrowserExecutionResult> ExecuteAsync(
        WebBrowserInput input,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (input.Action == WebBrowserAction.Close)
            {
                await CloseAsync().ConfigureAwait(false);
                return new WebBrowserExecutionResult(
                    "Closed browser session.",
                    new JsonObject { ["action"] = input.Action.ToWireValue() });
            }

            await EnsurePageAsync(cancellationToken).ConfigureAwait(false);
            if (_page is null)
            {
                throw new InvalidOperationException("Browser page is not available.");
            }

            if (input.WaitMs is > 0)
            {
                await _page.WaitForTimeoutAsync(input.WaitMs.Value).ConfigureAwait(false);
            }

            switch (input.Action)
            {
                case WebBrowserAction.Open:
                case WebBrowserAction.Navigate:
                    await _page.GotoAsync(input.Url!, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.NetworkIdle
                    }).ConfigureAwait(false);
                    return await BuildPageStateResultAsync(input.Action, $"Loaded {_page.Url}", null).ConfigureAwait(false);

                case WebBrowserAction.Click:
                    await _page.ClickAsync(input.Selector!).ConfigureAwait(false);
                    return await BuildPageStateResultAsync(input.Action, $"Clicked {input.Selector}", null).ConfigureAwait(false);

                case WebBrowserAction.Type:
                    await _page.FillAsync(input.Selector!, input.Text!).ConfigureAwait(false);
                    return await BuildPageStateResultAsync(input.Action, $"Filled {input.Selector}", null).ConfigureAwait(false);

                case WebBrowserAction.Evaluate:
                    var evaluation = await _page.EvaluateAsync<object>(input.Script!).ConfigureAwait(false);
                    return await BuildPageStateResultAsync(
                        input.Action,
                        evaluation is null ? "null" : JsonSerializer.Serialize(evaluation),
                        null).ConfigureAwait(false);

                case WebBrowserAction.Console:
                    return await BuildPageStateResultAsync(
                        input.Action,
                        $"Captured {ConsoleMessages.Count} console message(s).",
                        new JsonArray(ConsoleMessages.Select(static message => (JsonNode?)message).ToArray())).ConfigureAwait(false);

                case WebBrowserAction.Screenshot:
                    var screenshotPath = await SaveScreenshotAsync(context, _page, input.FullPage ?? true, cancellationToken).ConfigureAwait(false);
                    return await BuildPageStateResultAsync(
                        input.Action,
                        $"Saved screenshot to {screenshotPath}",
                        null,
                        screenshotPath).ConfigureAwait(false);

                case WebBrowserAction.Info:
                    return await BuildPageStateResultAsync(input.Action, "Current browser session state.", null).ConfigureAwait(false);

                default:
                    throw new InvalidOperationException($"Unsupported action {input.Action}.");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task EnsurePageAsync(CancellationToken cancellationToken)
    {
        if (_page is not null)
        {
            return;
        }

        _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
        _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Channel = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows() ? "chrome" : null
        }).ConfigureAwait(false);
        _context ??= await _browser.NewContextAsync(new BrowserNewContextOptions()).ConfigureAwait(false);
        _page = await _context.NewPageAsync().ConfigureAwait(false);
        _page.Console += (_, message) =>
        {
            var entry = $"[{message.Type}] {message.Text}";
            ConsoleMessages.Add(entry);
            if (ConsoleMessages.Count > MaxConsoleMessages)
            {
                ConsoleMessages.RemoveRange(0, ConsoleMessages.Count - MaxConsoleMessages);
            }
        };

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task<WebBrowserExecutionResult> BuildPageStateResultAsync(
        WebBrowserAction action,
        string result,
        JsonArray? consoleMessages,
        string? screenshotPath = null)
    {
        var title = _page is null ? null : await _page.TitleAsync().ConfigureAwait(false);
        var output = new JsonObject
        {
            ["action"] = action.ToWireValue(),
            ["url"] = _page?.Url,
            ["title"] = title,
            ["result"] = result
        };

        if (screenshotPath is not null)
        {
            output["screenshotPath"] = screenshotPath;
        }

        if (consoleMessages is not null)
        {
            output["consoleMessages"] = consoleMessages;
        }

        return new WebBrowserExecutionResult(result, output);
    }

    private static async Task<string> SaveScreenshotAsync(
        ToolExecutionContext context,
        IPage page,
        bool fullPage,
        CancellationToken cancellationToken)
    {
        var toolResultsDir = Path.Combine(
            SessionStoragePaths.GetProjectDir(context.Session.ProjectDirectory),
            context.Session.Id,
            "tool-results");
        Directory.CreateDirectory(toolResultsDir);

        var filePath = Path.Combine(
            toolResultsDir,
            $"webbrowser-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = filePath,
            FullPage = fullPage
        }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return filePath;
    }

    private static async Task CloseAsync()
    {
        if (_page is not null)
        {
            await _page.CloseAsync().ConfigureAwait(false);
            _page = null;
        }

        if (_context is not null)
        {
            await _context.CloseAsync().ConfigureAwait(false);
            _context = null;
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync().ConfigureAwait(false);
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        ConsoleMessages.Clear();
    }
}

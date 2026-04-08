using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed class RemoteTriggerTool : BaseTool
{
    private const string TriggerBetaHeader = "ccr-triggers-2026-01-30";
    private static readonly HttpClient HttpClient = new();

    public RemoteTriggerTool()
        : base(
            new ToolDescriptor(
                "RemoteTrigger",
                "Manage Claude remote trigger definitions using the authenticated Claude OAuth session",
                SearchHint: "manage scheduled remote agent triggers",
                ShouldDefer: true,
                InputSchema: ToolJsonSchemaFactory.StrictObject(
                    [
                        ("action", ToolJsonSchemaFactory.StringEnum(["list", "get", "create", "update", "run"])),
                        ("trigger_id", ToolJsonSchemaFactory.String("Required for get, update, and run", Required: false)),
                        ("body", ToolJsonSchemaFactory.Object(Required: false, description: "JSON body for create and update"))
                    ],
                    required: ["action"]),
                OutputSchema: ToolJsonSchemaFactory.StrictObject(
                    [
                        ("status", ToolJsonSchemaFactory.Integer()),
                        ("json", ToolJsonSchemaFactory.String())
                    ],
                    required: ["status", "json"]),
                Strict: true))
    {
    }

    public override bool IsEnabled()
    {
        return !string.IsNullOrWhiteSpace(RemoteTriggerAuth.ReadAccessToken()) &&
               !string.IsNullOrWhiteSpace(RemoteTriggerAuth.GetOrganizationUuid());
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override bool IsReadOnly(string arguments)
    {
        if (!RemoteTriggerInputParser.TryParse(arguments, out var input, out _) || input is null)
        {
            return false;
        }

        return input.Action is "list" or "get";
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!RemoteTriggerInputParser.TryParse(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "Invalid RemoteTrigger input."));
        }

        if (RemoteTriggerAuth.ReadAccessToken() is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid("No Claude OAuth access token is available."));
        }

        if (RemoteTriggerAuth.GetOrganizationUuid() is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid("No Claude organization UUID is available."));
        }

        return input.Action switch
        {
            "get" or "run" when string.IsNullOrWhiteSpace(input.TriggerId)
                => Task.FromResult(ToolValidationResult.Invalid($"'{input.Action}' requires trigger_id.")),
            "update" when string.IsNullOrWhiteSpace(input.TriggerId)
                => Task.FromResult(ToolValidationResult.Invalid("'update' requires trigger_id.")),
            "create" or "update" when input.Body is null
                => Task.FromResult(ToolValidationResult.Invalid($"'{input.Action}' requires body.")),
            _ => Task.FromResult(ToolValidationResult.Valid())
        };
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!RemoteTriggerInputParser.TryParse(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Failure(errorMessage ?? "Invalid RemoteTrigger input.");
        }

        var accessToken = RemoteTriggerAuth.ReadAccessToken();
        var organizationUuid = RemoteTriggerAuth.GetOrganizationUuid();
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(organizationUuid))
        {
            return Failure("RemoteTrigger requires an authenticated Claude OAuth session and organization UUID.");
        }

        var baseUrl = $"{RemoteTriggerAuth.GetBaseApiUrl().TrimEnd('/')}/v1/code/triggers";
        var request = new HttpRequestMessage();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Headers.TryAddWithoutValidation("anthropic-beta", TriggerBetaHeader);
        request.Headers.TryAddWithoutValidation("x-organization-uuid", organizationUuid);

        switch (input.Action)
        {
            case "list":
                request.Method = HttpMethod.Get;
                request.RequestUri = new Uri(baseUrl);
                break;
            case "get":
                request.Method = HttpMethod.Get;
                request.RequestUri = new Uri($"{baseUrl}/{input.TriggerId}");
                break;
            case "create":
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri(baseUrl);
                request.Content = CreateJsonContent(input.Body!);
                break;
            case "update":
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri($"{baseUrl}/{input.TriggerId}");
                request.Content = CreateJsonContent(input.Body!);
                break;
            case "run":
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri($"{baseUrl}/{input.TriggerId}/run");
                request.Content = CreateJsonContent(new JsonObject());
                break;
            default:
                return Failure($"Unsupported action '{input.Action}'.");
        }

        var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var prettyJson = RemoteTriggerAuth.PrettyPrintJson(bodyText);

        return Success(
            $"HTTP {(int)response.StatusCode}\n{prettyJson}",
            new JsonObject
            {
                ["status"] = (int)response.StatusCode,
                ["json"] = prettyJson
            });
    }

    private static StringContent CreateJsonContent(JsonNode body)
    {
        return new StringContent(
            body.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            Encoding.UTF8,
            "application/json");
    }
}

internal static class RemoteTriggerInputParser
{
    public static bool TryParse(string arguments, out RemoteTriggerInput? input, out string? errorMessage)
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

            var action = root["action"]?.GetValue<string>() ?? string.Empty;
            if (action is not ("list" or "get" or "create" or "update" or "run"))
            {
                errorMessage = "'action' must be one of: list, get, create, update, run.";
                return false;
            }

            var triggerId = root["trigger_id"]?.GetValue<string>();
            var body = root["body"];
            if (body is not null && body is not JsonObject)
            {
                errorMessage = "'body' must be a JSON object when provided.";
                return false;
            }

            input = new RemoteTriggerInput(action, triggerId, body as JsonObject);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}

internal sealed record RemoteTriggerInput(string Action, string? TriggerId, JsonObject? Body);

internal static class RemoteTriggerAuth
{
    public static string? ReadAccessToken()
    {
        var descriptorValue = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR");
        if (!string.IsNullOrWhiteSpace(descriptorValue) &&
            int.TryParse(descriptorValue, out var descriptor))
        {
            var descriptorPath = GetDescriptorPath(descriptor);
            if (!string.IsNullOrWhiteSpace(descriptorPath) && File.Exists(descriptorPath))
            {
                try
                {
                    var token = File.ReadAllText(descriptorPath).Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return token;
                    }
                }
                catch
                {
                }
            }
        }

        var fallbackPath = "/home/claude/.claude/remote/.oauth_token";
        if (File.Exists(fallbackPath))
        {
            try
            {
                var token = File.ReadAllText(fallbackPath).Trim();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public static string? GetOrganizationUuid()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("CLAUDE_CODE_ORGANIZATION_UUID");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        var globalConfigPath = GetGlobalClaudeFilePath();
        if (!File.Exists(globalConfigPath))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(globalConfigPath))?.AsObject();
            return root?["oauthAccount"]?["organizationUuid"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public static string GetBaseApiUrl()
    {
        return Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL")?.TrimEnd('/') ??
               ProviderRuntimeResolver.DefaultAnthropicBaseUrl;
    }

    public static string PrettyPrintJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "{}";
        }

        try
        {
            return JsonNode.Parse(content)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? content;
        }
        catch
        {
            return content;
        }
    }

    private static string GetGlobalClaudeFilePath()
    {
        var legacyPath = Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), ".config.json");
        if (File.Exists(legacyPath))
        {
            return legacyPath;
        }

        var configDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var parentDirectory = string.IsNullOrWhiteSpace(configDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : configDirectory;

        return Path.Combine(parentDirectory, ".claude.json");
    }

    private static string? GetDescriptorPath(int descriptor)
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            return $"/dev/fd/{descriptor}";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"/proc/self/fd/{descriptor}";
        }

        return null;
    }
}

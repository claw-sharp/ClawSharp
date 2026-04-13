using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Skills;

public sealed class SkillCatalogService
{
    private static readonly Regex SkillNamePattern = new("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.Compiled);
    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RecentProjectStore _recentProjectStore;

    public SkillCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
    }

    public async Task<ListSkillsResponse> ListSkillsAsync(
        ListSkillsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        return BuildCatalog(projectId, app);
    }

    public async Task<CreateSkillResponse> CreateSkillAsync(
        CreateSkillRequest request,
        CancellationToken cancellationToken = default)
    {
        var skillName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(skillName))
        {
            throw new AgentHostException("invalid_request", "Skill name is required.");
        }

        if (!SkillNamePattern.IsMatch(skillName))
        {
            throw new AgentHostException(
                "invalid_request",
                "Skill names must start with a letter or number and only use letters, numbers, underscores, or hyphens.");
        }

        var instructions = request.Instructions?.Trim();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new AgentHostException("invalid_request", "Skill instructions are required.");
        }

        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var state = app.AppStateStore.GetState();
        if (state.Skills.Any(skill => string.Equals(skill.Name, skillName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AgentHostException("skill_exists", $"A skill named '{skillName}' is already available in this project.");
        }

        var skillDirectory = Path.Combine(state.WorkspaceRoot, ".clawsharp", "skills", skillName);
        var skillFilePath = Path.Combine(skillDirectory, "SKILL.md");
        if (Directory.Exists(skillDirectory) || File.Exists(skillFilePath))
        {
            throw new AgentHostException("skill_exists", $"Skill path already exists: {skillDirectory}");
        }

        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(
            skillFilePath,
            BuildSkillTemplate(skillName, request.Description, instructions),
            Encoding.UTF8,
            cancellationToken);

        await RefreshSkillsAsync(app, cancellationToken);

        var response = BuildCatalog(projectId, app);
        var createdSkill = response.Skills.FirstOrDefault(skill =>
            string.Equals(skill.Name, skillName, StringComparison.OrdinalIgnoreCase));
        if (createdSkill is null)
        {
            throw new AgentHostException("skill_refresh_failed", $"Skill '{skillName}' was created but did not appear in the refreshed catalog.");
        }

        return new CreateSkillResponse(projectId, response.WorkspaceRoot, createdSkill, response.Skills);
    }

    private async Task<(string ProjectId, ClawSharpApplication App)> ResolveApplicationAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
            if (project is null)
            {
                throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
            }

            return (project.ProjectId, await _applicationRegistry.GetOrCreateAsync(project.Path, cancellationToken));
        }

        var recentProject = (await _recentProjectStore.ListAsync(cancellationToken)).FirstOrDefault();
        if (recentProject is null)
        {
            throw new AgentHostException("project_not_found", "No project is currently open.");
        }

        return (recentProject.ProjectId, await _applicationRegistry.GetOrCreateAsync(recentProject.Path, cancellationToken));
    }

    private static ListSkillsResponse BuildCatalog(string projectId, ClawSharpApplication app)
    {
        var state = app.AppStateStore.GetState();
        var skills = state.Skills
            .OrderBy(static skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static skill => new SkillSummaryDto(
                skill.Name,
                skill.Source,
                skill.FilePath,
                skill.BaseDirectory))
            .ToArray();

        return new ListSkillsResponse(projectId, state.WorkspaceRoot, skills);
    }

    private static async Task RefreshSkillsAsync(
        ClawSharpApplication app,
        CancellationToken cancellationToken)
    {
        var refreshService = new PluginRefreshService(
            new ExtensionBootstrapper(builtInPluginRegistry: BuiltInPluginCatalog.CreateRegistry()),
            app.AppStateStore);
        await refreshService.RefreshAsync(cancellationToken);
    }

    private static string BuildSkillTemplate(string skillName, string? description, string instructions)
    {
        var builder = new StringBuilder();
        builder.Append("# ");
        builder.AppendLine(FormatSkillHeading(skillName));
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine(description.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("## Guidance");
        builder.AppendLine();
        builder.AppendLine(instructions.Trim());
        builder.AppendLine();
        return builder.ToString();
    }

    private static string FormatSkillHeading(string skillName)
    {
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        var words = skillName
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.ToLowerInvariant())
            .Select(part => textInfo.ToTitleCase(part));

        return string.Join(' ', words);
    }
}

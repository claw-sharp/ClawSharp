using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace ClawSharp.Tools.Lsp;

internal sealed class LspTool : BaseTool
{
    public LspTool()
        : base(
            new ToolDescriptor(
                "LSP",
                "Use Roslyn-backed code intelligence for the current .NET workspace",
                SearchHint: "code intelligence (definitions, references, symbols, hover)",
                ShouldDefer: true,
                InputSchema: LspToolSchemas.InputSchema,
                OutputSchema: LspToolSchemas.OutputSchema,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override bool IsReadOnly(string arguments)
    {
        return true;
    }

    public override async Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!LspToolInputParser.TryParse(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return ToolValidationResult.Invalid(errorMessage ?? "Invalid LSP input.");
        }

        var permissionResolution = await FileToolPermissionEvaluator.ResolveForReadAsync(
            input.FilePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return ToolValidationResult.Invalid(permissionResolution.Message ?? "Read permission denied.");
        }

        if (!File.Exists(permissionResolution.ResolvedPath))
        {
            return ToolValidationResult.Invalid($"File does not exist: {input.FilePath}");
        }

        if (!string.Equals(Path.GetExtension(permissionResolution.ResolvedPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return ToolValidationResult.Invalid("LSP currently supports C# source files only in ClawSharp.");
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!LspToolInputParser.TryParse(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Failure(errorMessage ?? "Invalid LSP input.");
        }

        var permissionResolution = await FileToolPermissionEvaluator.ResolveForReadAsync(
            input.FilePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return Failure(permissionResolution.Message ?? "Read permission denied.");
        }

        using var workspaceContext = await RoslynWorkspaceLoader.LoadAsync(context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (workspaceContext is null)
        {
            return Failure("No Roslyn workspace could be loaded for the current directory. Expected a .sln or .csproj.");
        }

        var document = workspaceContext.FindDocument(permissionResolution.ResolvedPath);
        if (document is null)
        {
            return Failure($"File is not part of the loaded Roslyn workspace: {input.FilePath}");
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var position = LspPositionResolver.TryResolve(sourceText, input.Line, input.Character, out var resolvedPosition, out var positionError)
            ? resolvedPosition
            : -1;
        if (position < 0)
        {
            return Failure(positionError ?? "Invalid source position.");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return Failure("Failed to load semantic model for the requested document.");
        }

        var symbol = await LspSymbolResolver.ResolveAsync(document, semanticModel, root, position, cancellationToken).ConfigureAwait(false);
        var response = input.Operation switch
        {
            LspOperation.GoToDefinition => await GoToDefinitionAsync(symbol, workspaceContext, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false),
            LspOperation.FindReferences => await FindReferencesAsync(symbol, workspaceContext, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false),
            LspOperation.Hover => Hover(symbol),
            LspOperation.DocumentSymbol => await DocumentSymbolsAsync(document, semanticModel, root, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false),
            LspOperation.WorkspaceSymbol => await WorkspaceSymbolsAsync(workspaceContext, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false),
            LspOperation.GoToImplementation => await GoToImplementationAsync(symbol, workspaceContext, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false),
            LspOperation.PrepareCallHierarchy => PrepareCallHierarchy(symbol, context.WorkspaceRoot),
            LspOperation.IncomingCalls => await IncomingCallsAsync(symbol, workspaceContext, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false),
            LspOperation.OutgoingCalls => await OutgoingCallsAsync(document, semanticModel, root, position, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false),
            _ => LspResultPayload.Empty(input.Operation, input.FilePath, "Unsupported LSP operation.")
        };

        return Success(
            response.Result,
            new JsonObject
            {
                ["operation"] = response.Operation.ToWireValue(),
                ["result"] = response.Result,
                ["filePath"] = input.FilePath,
                ["resultCount"] = response.ResultCount,
                ["fileCount"] = response.FileCount
            });
    }

    private static async Task<LspResultPayload> GoToDefinitionAsync(
        ISymbol? symbol,
        RoslynWorkspaceContext workspaceContext,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (symbol is null)
        {
            return LspResultPayload.Empty(LspOperation.GoToDefinition, string.Empty, "No symbol found at this position.");
        }

        var definitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in symbol.Locations.Where(static location => location.IsInSource))
        {
            definitions.Add(LspFormatting.FormatLocation(location, workspaceContext.Solution, workspaceRoot, includeSymbol: true, symbol));
        }

        if (definitions.Count == 0)
        {
            var sourceDefinition = await SymbolFinder.FindSourceDefinitionAsync(symbol, workspaceContext.Solution, cancellationToken).ConfigureAwait(false);
            if (sourceDefinition is not null)
            {
                foreach (var location in sourceDefinition.Locations.Where(static location => location.IsInSource))
                {
                    definitions.Add(LspFormatting.FormatLocation(location, workspaceContext.Solution, workspaceRoot, includeSymbol: true, sourceDefinition));
                }
            }
        }

        return LspFormatting.FromLines(
            LspOperation.GoToDefinition,
            definitions.Count == 0 ? ["No definition found."] : definitions,
            definitions.Count == 0 ? 0 : definitions.Count);
    }

    private static async Task<LspResultPayload> FindReferencesAsync(
        ISymbol? symbol,
        RoslynWorkspaceContext workspaceContext,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (symbol is null)
        {
            return LspResultPayload.Empty(LspOperation.FindReferences, string.Empty, "No symbol found at this position.");
        }

        var lines = new List<string>();
        var files = new HashSet<string>(StringComparer.Ordinal);
        var references = await SymbolFinder.FindReferencesAsync(symbol, workspaceContext.Solution, cancellationToken).ConfigureAwait(false);
        foreach (var reference in references)
        {
            foreach (var location in reference.Locations.Where(static location => location.Location.IsInSource))
            {
                lines.Add(LspFormatting.FormatReferenceLocation(location.Location, workspaceContext.Solution, workspaceRoot));
                files.Add(location.Document.FilePath ?? string.Empty);
            }
        }

        return LspFormatting.FromLines(LspOperation.FindReferences, lines, lines.Count, files.Count);
    }

    private static LspResultPayload Hover(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return LspResultPayload.Empty(LspOperation.Hover, string.Empty, "No symbol found at this position.");
        }

        var builder = new StringBuilder();
        builder.AppendLine(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        var documentation = LspDocumentationFormatter.Format(symbol);
        if (!string.IsNullOrWhiteSpace(documentation))
        {
            builder.AppendLine();
            builder.AppendLine(documentation);
        }

        return new LspResultPayload(LspOperation.Hover, builder.ToString().Trim(), 1, 1);
    }

    private static async Task<LspResultPayload> DocumentSymbolsAsync(
        Document document,
        SemanticModel semanticModel,
        SyntaxNode root,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var symbols = await LspSymbolCollector.CollectDocumentSymbolsAsync(document, semanticModel, root, workspaceRoot, cancellationToken).ConfigureAwait(false);
        return LspFormatting.FromLines(LspOperation.DocumentSymbol, symbols, symbols.Count, symbols.Count == 0 ? 0 : 1);
    }

    private static async Task<LspResultPayload> WorkspaceSymbolsAsync(
        RoslynWorkspaceContext workspaceContext,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var files = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in workspaceContext.Solution.Projects.SelectMany(static project => project.Documents))
        {
            if (!string.Equals(Path.GetExtension(document.FilePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null)
            {
                continue;
            }

            var documentSymbols = await LspSymbolCollector.CollectDocumentSymbolsAsync(document, semanticModel, root, workspaceRoot, cancellationToken).ConfigureAwait(false);
            foreach (var line in documentSymbols)
            {
                lines.Add(line);
            }

            if (documentSymbols.Count > 0)
            {
                files.Add(document.FilePath ?? string.Empty);
            }

            if (lines.Count >= 200)
            {
                break;
            }
        }

        return LspFormatting.FromLines(LspOperation.WorkspaceSymbol, lines.Take(200), Math.Min(lines.Count, 200), files.Count);
    }

    private static async Task<LspResultPayload> GoToImplementationAsync(
        ISymbol? symbol,
        RoslynWorkspaceContext workspaceContext,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (symbol is null)
        {
            return LspResultPayload.Empty(LspOperation.GoToImplementation, string.Empty, "No symbol found at this position.");
        }

        var implementations = await FindImplementationSymbolsAsync(symbol, workspaceContext.Solution, cancellationToken).ConfigureAwait(false);
        var lines = implementations
            .SelectMany(static implementation => implementation.Locations.Select(location => (implementation, location)))
            .Where(static pair => pair.location.IsInSource)
            .Select(pair => LspFormatting.FormatLocation(pair.location, workspaceContext.Solution, workspaceRoot, includeSymbol: true, pair.implementation))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return LspFormatting.FromLines(LspOperation.GoToImplementation, lines, lines.Length);
    }

    private static LspResultPayload PrepareCallHierarchy(ISymbol? symbol, string workspaceRoot)
    {
        if (symbol is null)
        {
            return LspResultPayload.Empty(LspOperation.PrepareCallHierarchy, string.Empty, "No callable symbol found at this position.");
        }

        var callable = LspCallHierarchyResolver.NormalizeCallableSymbol(symbol);
        if (callable is null)
        {
            return LspResultPayload.Empty(LspOperation.PrepareCallHierarchy, string.Empty, "No callable symbol found at this position.");
        }

        var lines = callable.Locations
            .Where(static location => location.IsInSource)
            .Select(location => LspFormatting.FormatSourceSpan(location.GetLineSpan(), location.SourceTree?.FilePath, workspaceRoot, callable))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return LspFormatting.FromLines(LspOperation.PrepareCallHierarchy, lines, lines.Length);
    }

    private static async Task<LspResultPayload> IncomingCallsAsync(
        ISymbol? symbol,
        RoslynWorkspaceContext workspaceContext,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var callable = LspCallHierarchyResolver.NormalizeCallableSymbol(symbol);
        if (callable is null)
        {
            return LspResultPayload.Empty(LspOperation.IncomingCalls, string.Empty, "No callable symbol found at this position.");
        }

        var callers = new HashSet<string>(StringComparer.Ordinal);
        var references = await SymbolFinder.FindReferencesAsync(callable, workspaceContext.Solution, cancellationToken).ConfigureAwait(false);
        foreach (var reference in references)
        {
            foreach (var location in reference.Locations.Where(static location => location.Location.IsInSource))
            {
                var document = location.Document;
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                var node = root.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
                var caller = semanticModel.GetEnclosingSymbol(location.Location.SourceSpan.Start, cancellationToken);
                caller = LspCallHierarchyResolver.NormalizeCallableSymbol(caller);
                if (caller is null || SymbolEqualityComparer.Default.Equals(caller, callable))
                {
                    continue;
                }

                callers.Add(LspFormatting.FormatCallerOrCallee(caller, workspaceRoot));
            }
        }

        return LspFormatting.FromLines(LspOperation.IncomingCalls, callers, callers.Count);
    }

    private static async Task<LspResultPayload> OutgoingCallsAsync(
        Document document,
        SemanticModel semanticModel,
        SyntaxNode root,
        int position,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var callableNode = LspCallHierarchyResolver.FindEnclosingCallableNode(root, position);
        if (callableNode is null)
        {
            return LspResultPayload.Empty(LspOperation.OutgoingCalls, string.Empty, "No callable symbol found at this position.");
        }

        var callees = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invocationNode in callableNode.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ISymbol? callee = invocationNode switch
            {
                InvocationExpressionSyntax invocation => semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol,
                ObjectCreationExpressionSyntax creation => semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol,
                ConstructorInitializerSyntax initializer => semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol,
                _ => null
            };

            callee = LspCallHierarchyResolver.NormalizeCallableSymbol(callee);
            if (callee is null)
            {
                continue;
            }

            callees.Add(LspFormatting.FormatCallerOrCallee(callee, workspaceRoot));
        }

        return LspFormatting.FromLines(LspOperation.OutgoingCalls, callees, callees.Count);
    }

    private static async Task<IReadOnlyList<ISymbol>> FindImplementationSymbolsAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        if (symbol is INamedTypeSymbol namedTypeSymbol)
        {
            return await SymbolFinder.FindImplementationsAsync(
                    namedTypeSymbol,
                    solution,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (symbol.ContainingType is not INamedTypeSymbol containingType ||
            containingType.TypeKind is not TypeKind.Interface and not TypeKind.Class)
        {
            return symbol.Locations.Any(static location => location.IsInSource) ? [symbol] : [];
        }

        var implementingTypes = await SymbolFinder.FindImplementationsAsync(
                containingType,
                solution,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var results = new List<ISymbol>();
        foreach (var implementingType in implementingTypes)
        {
            foreach (var member in implementingType.GetMembers().Where(member => string.Equals(member.Name, symbol.Name, StringComparison.Ordinal)))
            {
                if (member.Kind != symbol.Kind)
                {
                    continue;
                }

                results.Add(member);
            }
        }

        return results.Count == 0 && symbol.Locations.Any(static location => location.IsInSource)
            ? [symbol]
            : results;
    }
}

internal static class LspToolSchemas
{
    public static JsonObject InputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("operation", ToolJsonSchemaFactory.StringEnum(
                    [
                        "goToDefinition",
                        "findReferences",
                        "hover",
                        "documentSymbol",
                        "workspaceSymbol",
                        "goToImplementation",
                        "prepareCallHierarchy",
                        "incomingCalls",
                        "outgoingCalls"
                    ])),
                ("filePath", ToolJsonSchemaFactory.String("Absolute or relative path to the file")),
                ("line", ToolJsonSchemaFactory.Integer("1-based line number", minimum: 1)),
                ("character", ToolJsonSchemaFactory.Integer("1-based character offset", minimum: 1))
            ],
            required: ["operation", "filePath", "line", "character"]);

    public static JsonObject OutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("operation", ToolJsonSchemaFactory.String()),
                ("result", ToolJsonSchemaFactory.String()),
                ("filePath", ToolJsonSchemaFactory.String()),
                ("resultCount", ToolJsonSchemaFactory.Integer()),
                ("fileCount", ToolJsonSchemaFactory.Integer())
            ],
            required: ["operation", "result", "filePath", "resultCount", "fileCount"]);
}

internal static class LspToolInputParser
{
    public static bool TryParse(string arguments, out LspInput? input, out string? errorMessage)
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

            var operationText = root["operation"]?.GetValue<string>() ?? string.Empty;
            if (!LspOperationParser.TryParse(operationText, out var operation))
            {
                errorMessage = "Invalid LSP operation.";
                return false;
            }

            var filePath = root["filePath"]?.GetValue<string>();
            var line = root["line"]?.GetValue<int?>() ?? 0;
            var character = root["character"]?.GetValue<int?>() ?? 0;
            if (string.IsNullOrWhiteSpace(filePath) || line <= 0 || character <= 0)
            {
                errorMessage = "'filePath', 'line', and 'character' are required.";
                return false;
            }

            input = new LspInput(operation, filePath, line, character);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}

internal sealed record LspInput(LspOperation Operation, string FilePath, int Line, int Character);

internal enum LspOperation
{
    GoToDefinition,
    FindReferences,
    Hover,
    DocumentSymbol,
    WorkspaceSymbol,
    GoToImplementation,
    PrepareCallHierarchy,
    IncomingCalls,
    OutgoingCalls
}

internal static class LspOperationParser
{
    public static bool TryParse(string value, out LspOperation operation)
    {
        operation = value switch
        {
            "goToDefinition" => LspOperation.GoToDefinition,
            "findReferences" => LspOperation.FindReferences,
            "hover" => LspOperation.Hover,
            "documentSymbol" => LspOperation.DocumentSymbol,
            "workspaceSymbol" => LspOperation.WorkspaceSymbol,
            "goToImplementation" => LspOperation.GoToImplementation,
            "prepareCallHierarchy" => LspOperation.PrepareCallHierarchy,
            "incomingCalls" => LspOperation.IncomingCalls,
            "outgoingCalls" => LspOperation.OutgoingCalls,
            _ => default
        };

        return value is
            "goToDefinition" or
            "findReferences" or
            "hover" or
            "documentSymbol" or
            "workspaceSymbol" or
            "goToImplementation" or
            "prepareCallHierarchy" or
            "incomingCalls" or
            "outgoingCalls";
    }

    public static string ToWireValue(this LspOperation operation)
    {
        return operation switch
        {
            LspOperation.GoToDefinition => "goToDefinition",
            LspOperation.FindReferences => "findReferences",
            LspOperation.Hover => "hover",
            LspOperation.DocumentSymbol => "documentSymbol",
            LspOperation.WorkspaceSymbol => "workspaceSymbol",
            LspOperation.GoToImplementation => "goToImplementation",
            LspOperation.PrepareCallHierarchy => "prepareCallHierarchy",
            LspOperation.IncomingCalls => "incomingCalls",
            LspOperation.OutgoingCalls => "outgoingCalls",
            _ => operation.ToString()
        };
    }
}

internal sealed record LspResultPayload(LspOperation Operation, string Result, int ResultCount, int FileCount)
{
    public static LspResultPayload Empty(LspOperation operation, string filePath, string message)
    {
        return new LspResultPayload(operation, message, 0, 0);
    }
}

internal static class RoslynWorkspaceLoader
{
    private static int _registered;

    public static async Task<RoslynWorkspaceContext?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        EnsureRegistered();

        var solutionPath = Directory.GetFiles(workspaceRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var projectPath = solutionPath is null
            ? Directory.GetFiles(workspaceRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
        if (solutionPath is null && projectPath is null)
        {
            return null;
        }

        var workspace = MSBuildWorkspace.Create();
        if (solutionPath is not null)
        {
            var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new RoslynWorkspaceContext(workspace, solution);
        }

        var project = await workspace.OpenProjectAsync(projectPath!, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new RoslynWorkspaceContext(workspace, project.Solution);
    }

    private static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }
}

internal sealed class RoslynWorkspaceContext : IDisposable
{
    public RoslynWorkspaceContext(MSBuildWorkspace workspace, Solution solution)
    {
        Workspace = workspace;
        Solution = solution;
    }

    public MSBuildWorkspace Workspace { get; }
    public Solution Solution { get; }

    public Document? FindDocument(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return Solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(document =>
                document.FilePath is not null &&
                string.Equals(Path.GetFullPath(document.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        Workspace.Dispose();
    }
}

internal static class LspPositionResolver
{
    public static bool TryResolve(SourceText sourceText, int line, int character, out int position, out string? error)
    {
        position = -1;
        error = null;

        var zeroBasedLine = line - 1;
        var zeroBasedCharacter = character - 1;
        if (zeroBasedLine < 0 || zeroBasedLine >= sourceText.Lines.Count)
        {
            error = "Line is outside the bounds of the file.";
            return false;
        }

        var textLine = sourceText.Lines[zeroBasedLine];
        if (zeroBasedCharacter < 0 || zeroBasedCharacter > textLine.Span.Length)
        {
            error = "Character is outside the bounds of the specified line.";
            return false;
        }

        position = textLine.Start + zeroBasedCharacter;
        return true;
    }
}

internal static class LspSymbolResolver
{
    public static async Task<ISymbol?> ResolveAsync(
        Document document,
        SemanticModel semanticModel,
        SyntaxNode root,
        int position,
        CancellationToken cancellationToken)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (symbol is not null)
        {
            return symbol;
        }

        var token = root.FindToken(position);
        var parent = token.Parent;
        if (parent is null)
        {
            return null;
        }

        return semanticModel.GetDeclaredSymbol(parent, cancellationToken) ??
               semanticModel.GetSymbolInfo(parent, cancellationToken).Symbol;
    }
}

internal static class LspSymbolCollector
{
    public static async Task<IReadOnlyList<string>> CollectDocumentSymbolsAsync(
        Document document,
        SemanticModel semanticModel,
        SyntaxNode root,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is not MemberDeclarationSyntax and not BaseTypeDeclarationSyntax and not DelegateDeclarationSyntax and not EnumMemberDeclarationSyntax and not VariableDeclaratorSyntax)
            {
                continue;
            }

            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is null || symbol.Kind == SymbolKind.Namespace)
            {
                continue;
            }

            var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
            if (location is null)
            {
                continue;
            }

            lines.Add(LspFormatting.FormatLocation(location, document.Project.Solution, workspaceRoot, includeSymbol: true, symbol));
        }

        return lines
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static line => line, StringComparer.Ordinal)
            .Take(200)
            .ToArray();
    }
}

internal static class LspCallHierarchyResolver
{
    public static ISymbol? NormalizeCallableSymbol(ISymbol? symbol)
    {
        return symbol switch
        {
            IMethodSymbol => symbol,
            IPropertySymbol propertySymbol => propertySymbol,
            IEventSymbol eventSymbol => eventSymbol,
            _ => null
        };
    }

    public static SyntaxNode? FindEnclosingCallableNode(SyntaxNode root, int position)
    {
        return root.FindToken(position).Parent?.AncestorsAndSelf().FirstOrDefault(static node =>
            node is MethodDeclarationSyntax or
                ConstructorDeclarationSyntax or
                LocalFunctionStatementSyntax or
                PropertyDeclarationSyntax or
                AccessorDeclarationSyntax);
    }
}

internal static class LspFormatting
{
    public static LspResultPayload FromLines(LspOperation operation, IEnumerable<string> lines, int resultCount, int? fileCount = null)
    {
        var materialized = lines.Where(static line => !string.IsNullOrWhiteSpace(line)).ToArray();
        var count = resultCount == 0 ? materialized.Length : resultCount;
        var files = fileCount ?? materialized
            .Select(static line => line.Split(':', 2, StringSplitOptions.TrimEntries)[0])
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new LspResultPayload(
            operation,
            materialized.Length == 0 ? "No results found." : string.Join(Environment.NewLine, materialized),
            count,
            files);
    }

    public static string FormatLocation(Location location, Solution solution, string workspaceRoot, bool includeSymbol, ISymbol? symbol)
    {
        var span = location.GetLineSpan();
        return FormatSourceSpan(span, location.SourceTree?.FilePath, workspaceRoot, includeSymbol ? symbol : null);
    }

    public static string FormatReferenceLocation(Location location, Solution solution, string workspaceRoot)
    {
        var span = location.GetLineSpan();
        return FormatSourceSpan(span, location.SourceTree?.FilePath, workspaceRoot, null);
    }

    public static string FormatSourceSpan(FileLinePositionSpan span, string? filePath, string workspaceRoot, ISymbol? symbol)
    {
        var displayPath = ToDisplayPath(filePath, workspaceRoot);
        var line = span.StartLinePosition.Line + 1;
        var character = span.StartLinePosition.Character + 1;
        var suffix = symbol is null ? string.Empty : $" {symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";
        return $"{displayPath}:{line}:{character}{suffix}";
    }

    public static string FormatCallerOrCallee(ISymbol symbol, string workspaceRoot)
    {
        var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
        if (location is null)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return FormatSourceSpan(location.GetLineSpan(), location.SourceTree?.FilePath, workspaceRoot, symbol);
    }

    private static string ToDisplayPath(string? filePath, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "<unknown>";
        }

        var fullPath = Path.GetFullPath(filePath);
        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        return fullPath.StartsWith(fullWorkspaceRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(fullWorkspaceRoot, fullPath).Replace('\\', '/')
            : fullPath.Replace('\\', '/');
    }
}

internal static class LspDocumentationFormatter
{
    public static string Format(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return string.Empty;
        }

        var text = xml
            .Replace("<summary>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</summary>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<para>", Environment.NewLine, StringComparison.OrdinalIgnoreCase)
            .Replace("</para>", Environment.NewLine, StringComparison.OrdinalIgnoreCase);

        return System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty).Trim();
    }
}

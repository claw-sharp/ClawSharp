using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class SearchToolTests
{
    [Fact]
    public void ToolRegistry_Registers_Glob_And_Grep_Tools_With_Ts_Shaped_Schemas()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());

        var glob = Assert.Single(registry.All, tool => tool.Name == "Glob");
        var grep = Assert.Single(registry.All, tool => tool.Name == "Grep");

        Assert.Equal("object", glob.InputSchema?["type"]?.GetValue<string>());
        Assert.Equal("object", glob.OutputSchema?["type"]?.GetValue<string>());
        Assert.Equal("find files by name pattern or wildcard", glob.SearchHint);
        Assert.Equal(1, glob.InputSchema?["properties"]?["pattern"]?["minLength"]?.GetValue<int>());

        Assert.Equal("object", grep.InputSchema?["type"]?.GetValue<string>());
        Assert.Equal("object", grep.OutputSchema?["type"]?.GetValue<string>());
        Assert.Equal("search file contents with a non-empty regex pattern (ripgrep); use Glob to list files", grep.SearchHint);
        Assert.Equal(1, grep.InputSchema?["properties"]?["pattern"]?["minLength"]?.GetValue<int>());
    }

    [Fact]
    public async Task GlobTool_Returns_Relative_Filenames_And_Truncation_Metadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-glob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "one.ts"), "export const one = 1;");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "two.ts"), "export const two = 2;");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "three.js"), "export const three = 3;");

            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Glob",
                """{"pattern":"src/**/*.ts"}""",
                session,
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            var data = Assert.IsType<JsonObject>(result.StructuredOutput);
            var filenames = Assert.IsType<JsonArray>(data["filenames"]);
            Assert.Equal(2, data["numFiles"]?.GetValue<int>());
            Assert.False(data["truncated"]?.GetValue<bool>() ?? true);
            Assert.All(filenames, item => Assert.DoesNotContain(tempDir, item!.GetValue<string>(), StringComparison.OrdinalIgnoreCase));
            Assert.Contains("src/one.ts", filenames.Select(item => item!.GetValue<string>()));
            Assert.Contains("src/two.ts", filenames.Select(item => item!.GetValue<string>()));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GlobTool_Accepts_Path_Only_When_Path_Contains_Glob_Wildcards()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-glob-path-only-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var grammarDir = Path.Combine(tempDir, "grammar", "topic");
            Directory.CreateDirectory(grammarDir);
            await File.WriteAllTextAsync(Path.Combine(grammarDir, "prefix.md"), "alpha");
            await File.WriteAllTextAsync(Path.Combine(grammarDir, "prelude.md"), "beta");
            await File.WriteAllTextAsync(Path.Combine(grammarDir, "other.txt"), "gamma");

            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Glob",
                $$"""{"path":"{{Path.Combine(tempDir, "grammar", "**", "pre*.md").Replace("\\", "\\\\")}}"}""",
                session,
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            var data = Assert.IsType<JsonObject>(result.StructuredOutput);
            var filenames = Assert.IsType<JsonArray>(data["filenames"]);
            Assert.Equal(2, data["numFiles"]?.GetValue<int>());
            Assert.Contains("grammar/topic/prefix.md", filenames.Select(item => item!.GetValue<string>()));
            Assert.Contains("grammar/topic/prelude.md", filenames.Select(item => item!.GetValue<string>()));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GlobTool_Defaults_To_Match_All_When_Path_Is_Provided_Without_A_Pattern()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-glob-default-pattern-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "src", "nested"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "one.ts"), "alpha");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "nested", "two.ts"), "beta");

            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Glob",
                """{"path":"src"}""",
                session,
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            var data = Assert.IsType<JsonObject>(result.StructuredOutput);
            var filenames = Assert.IsType<JsonArray>(data["filenames"]);
            Assert.Contains("src/one.ts", filenames.Select(item => item!.GetValue<string>()));
            Assert.Contains("src/nested/two.ts", filenames.Select(item => item!.GetValue<string>()));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GlobTool_Accepts_Backslash_Patterns_Across_Platforms()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-glob-backslash-pattern-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "src", "nested"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "one.ts"), "alpha");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "nested", "two.ts"), "beta");

            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Glob",
                """{"pattern":"src\\**\\*.ts"}""",
                session,
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            var data = Assert.IsType<JsonObject>(result.StructuredOutput);
            var filenames = Assert.IsType<JsonArray>(data["filenames"]);
            Assert.Contains("src/one.ts", filenames.Select(item => item!.GetValue<string>()));
            Assert.Contains("src/nested/two.ts", filenames.Select(item => item!.GetValue<string>()));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void GlobTool_RenderToolUseMessage_Parses_Windows_Drive_Path_Glob()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        Assert.True(registry.TryResolve("Glob", out var tool));

        var message = tool!.RenderToolUseMessage(
            """{"path":"C:\\Users\\hadoan\\Documents\\GitHub\\german-b2\\grammar\\**\\pre*.md"}""");

        Assert.Contains("pattern: \"**/pre*.md\"", message, StringComparison.Ordinal);
        Assert.Contains("path: \"C:\\\\Users\\\\hadoan\\\\Documents\\\\GitHub\\\\german-b2\\\\grammar\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobTool_RenderToolUseMessage_Parses_Unc_Path_Glob()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        Assert.True(registry.TryResolve("Glob", out var tool));

        var message = tool!.RenderToolUseMessage(
            """{"path":"\\\\server\\share\\german-b2\\grammar\\**\\pre*.md"}""");

        Assert.Contains("pattern: \"**/pre*.md\"", message, StringComparison.Ordinal);
        Assert.Contains("path: \"\\\\\\\\server\\\\share\\\\german-b2\\\\grammar\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrepTool_Supports_Content_Files_And_Count_Modes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-grep-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "alpha.txt"), "first hit\nsecond line\nthird hit\n");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "beta.txt"), "hit again\nnope\n");

            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();
            var settings = new ClawSharpSettings();

            var contentResult = await registry.ExecuteAsync(
                "Grep",
                """{"pattern":"hit","output_mode":"content","-n":true}""",
                session,
                settings);
            Assert.True(contentResult.Success, contentResult.Output);
            var contentData = Assert.IsType<JsonObject>(contentResult.StructuredOutput);
            Assert.Equal("content", contentData["mode"]?.GetValue<string>());
            Assert.Contains("alpha.txt:1:first hit", contentResult.Output, StringComparison.Ordinal);
            Assert.Contains("beta.txt:1:hit again", contentResult.Output, StringComparison.Ordinal);

            var filesResult = await registry.ExecuteAsync(
                "Grep",
                """{"pattern":"hit","output_mode":"files_with_matches"}""",
                session,
                settings);
            Assert.True(filesResult.Success, filesResult.Output);
            var filesData = Assert.IsType<JsonObject>(filesResult.StructuredOutput);
            var fileNames = Assert.IsType<JsonArray>(filesData["filenames"]);
            Assert.Equal("files_with_matches", filesData["mode"]?.GetValue<string>());
            Assert.Contains("alpha.txt", fileNames.Select(item => item!.GetValue<string>()));
            Assert.Contains("beta.txt", fileNames.Select(item => item!.GetValue<string>()));

            var countResult = await registry.ExecuteAsync(
                "Grep",
                """{"pattern":"hit","output_mode":"count"}""",
                session,
                settings);
            Assert.True(countResult.Success, countResult.Output);
            var countData = Assert.IsType<JsonObject>(countResult.StructuredOutput);
            Assert.Equal("count", countData["mode"]?.GetValue<string>());
            Assert.Equal(3, countData["numMatches"]?.GetValue<int>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GrepTool_Rejects_Empty_Pattern_With_Actionable_Message()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var session = new DefaultSessionFactory(Environment.CurrentDirectory).Create();

        var result = await registry.ExecuteAsync(
            "Grep",
            """{"pattern":""}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("Grep requires a non-empty pattern.", result.Output, StringComparison.Ordinal);
        Assert.Contains("Use Glob to list files", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrepTool_Defaults_To_Match_All_When_Content_Mode_Targets_A_Single_File_Without_A_Pattern()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-grep-fallback-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ThreadView.tsx"), "first line\nsecond line\n");

            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Grep",
                """{"path":"ThreadView.tsx","glob":"ThreadView.tsx","output_mode":"content","-n":true}""",
                session,
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            Assert.Contains("ThreadView.tsx:1:first line", result.Output, StringComparison.Ordinal);
            Assert.Contains("ThreadView.tsx:2:second line", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GrepTool_Defaults_To_Match_All_When_Content_Mode_Uses_Glob_Filter_Without_A_Pattern()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-grep-glob-fallback-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var grammarDir = Path.Combine(tempDir, "grammar");
            Directory.CreateDirectory(grammarDir);
            await File.WriteAllTextAsync(Path.Combine(grammarDir, "a.md"), "# Alpha\nfirst line\n");
            await File.WriteAllTextAsync(Path.Combine(grammarDir, "b.md"), "# Beta\nsecond line\n");
            await File.WriteAllTextAsync(Path.Combine(grammarDir, "notes.txt"), "ignore me\n");

            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Grep",
                """{"path":"grammar","glob":"**/*.md","output_mode":"content","-n":true,"head_limit":20}""",
                session,
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            Assert.Contains("grammar/a.md:1:# Alpha", result.Output, StringComparison.Ordinal);
            Assert.Contains("grammar/b.md:2:second line", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("notes.txt", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GlobTool_Rejects_Empty_Pattern_With_Actionable_Message()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var session = new DefaultSessionFactory(Environment.CurrentDirectory).Create();

        var result = await registry.ExecuteAsync(
            "Glob",
            """{"pattern":""}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("Glob requires a non-empty pattern.", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"**/*\"", result.Output, StringComparison.Ordinal);
    }
}

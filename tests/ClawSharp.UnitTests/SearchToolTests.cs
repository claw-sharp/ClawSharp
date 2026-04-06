// TS origin: ./tools/GlobTool/GlobTool.ts, ./tools/GrepTool/GrepTool.ts, ./utils/glob.ts, ./utils/ripgrep.ts
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

        Assert.Equal("object", grep.InputSchema?["type"]?.GetValue<string>());
        Assert.Equal("object", grep.OutputSchema?["type"]?.GetValue<string>());
        Assert.Equal("search file contents with regex (ripgrep)", grep.SearchHint);
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
}

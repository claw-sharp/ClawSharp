using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public class ParityMilestoneCoverageTests
{
    private static readonly SemaphoreSlim TaskOutputLengthEnvironmentLock = new(1, 1);

    [Fact]
    public void AppMetadata_Uses_ClawSharp_Name()
    {
        Assert.Equal("ClawSharp", AppMetadata.Name);
    }

    [Fact]
    public void ToolRegistry_Has_Default_Tools()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());

        Assert.NotEmpty(registry.All);
        Assert.Contains(registry.All, tool => tool.Name == "Agent");
    }

    [Fact]
    public void File_Tool_Descriptors_Publish_Ts_Shaped_Schema_Metadata()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());

        var read = Assert.Single(registry.All, tool => tool.Name == "Read");
        var edit = Assert.Single(registry.All, tool => tool.Name == "Edit");
        var write = Assert.Single(registry.All, tool => tool.Name == "Write");

        Assert.True(read.Strict);
        Assert.True(edit.Strict);
        Assert.True(write.Strict);
        Assert.Equal("object", read.InputSchema?["type"]?.GetValue<string>());
        Assert.NotNull(read.OutputSchema?["oneOf"]);
        Assert.Equal("object", edit.InputSchema?["type"]?.GetValue<string>());
        Assert.Equal("object", edit.OutputSchema?["type"]?.GetValue<string>());
        Assert.Equal("object", write.InputSchema?["type"]?.GetValue<string>());
        Assert.Equal("object", write.OutputSchema?["type"]?.GetValue<string>());
    }

    [Fact]
    public void ToolRegistry_Resolves_Aliases_And_Filters_Disabled_Tools_From_All()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        registry.Register(new AliasTestTool());
        registry.Register(new DisabledTestTool());

        Assert.True(registry.TryResolve("AliasPrimary", out var primary));
        Assert.True(registry.TryResolve("alias-secondary", out var alias));
        Assert.Same(primary, alias);
        Assert.Contains(registry.All, descriptor => descriptor.Name == "AliasPrimary");
        Assert.DoesNotContain(registry.All, descriptor => descriptor.Name == "DisabledTool");
        Assert.False(registry.TryResolve("DisabledTool", out _));
    }

    [Fact]
    public void FileStateCache_Normalizes_Path_Keys_And_Merges_By_Newer_Timestamp()
    {
        var root = Path.Combine(Path.GetTempPath(), "clawsharp-filestate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "nested", "..", "example.txt");
        var normalizedPath = Path.GetFullPath(filePath);

        var first = FileStateCache.CreateWithSizeLimit(10);
        first.Set(filePath, new FileState("first", 10, Offset: null, Limit: null));

        Assert.True(first.Has(normalizedPath));
        Assert.Equal("first", first.Get(normalizedPath)?.Content);

        var second = FileStateCache.CreateWithSizeLimit(10);
        second.Set(normalizedPath, new FileState("second", 20, Offset: null, Limit: null));

        var merged = FileStateCache.Merge(first, second);
        Assert.Equal("second", merged.Get(normalizedPath)?.Content);
    }

    [Fact]
    public void ToolRegistry_Carries_Empty_Tool_Permission_Context()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());

        Assert.Equal(PermissionMode.Default, registry.ToolPermissionContext.Mode);
        Assert.Empty(registry.ToolPermissionContext.AdditionalWorkingDirectories);
        Assert.Empty(registry.ToolPermissionContext.AlwaysAllowRules);
        Assert.Empty(registry.ToolPermissionContext.AlwaysDenyRules);
        Assert.Empty(registry.ToolPermissionContext.AlwaysAskRules);
        Assert.False(registry.ToolPermissionContext.IsBypassPermissionsModeAvailable);
    }

    [Fact]
    public async Task Read_Seeds_File_State_And_Write_Rejects_Stale_Content()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(filePath, "alpha");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var readResult = await registry.ExecuteAsync("Read", "sample.txt", session, settings);
        Assert.True(readResult.Success);
        Assert.Equal("alpha", registry.ReadFileState.Get(filePath)?.Content);

        await Task.Delay(20);
        await File.WriteAllTextAsync(filePath, "beta");

        var writeResult = await registry.ExecuteAsync("Write", "sample.txt|gamma", session, settings);
        Assert.False(writeResult.Success);
        Assert.Contains(
            "File has been unexpectedly modified. Read it again before attempting to write it.",
            writeResult.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_Marks_Truncated_Content_As_Partial_View()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "large.txt");
        var content = new string('a', 5000);
        await File.WriteAllTextAsync(filePath, content);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var readResult = await registry.ExecuteAsync("Read", "large.txt", session, settings);

        Assert.True(readResult.Success);
        Assert.Contains("[truncated]", readResult.Output, StringComparison.Ordinal);
        Assert.True(registry.ReadFileState.Get(filePath)?.IsPartialView);
        Assert.Equal(content, registry.ReadFileState.Get(filePath)?.Content);
    }

    [Fact]
    public async Task Read_Normalizes_Crlf_Content_In_Read_File_State()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-crlf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "crlf.txt");
        await File.WriteAllTextAsync(filePath, "a\r\nb\r\n");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "crlf.txt", session, settings);

        Assert.True(result.Success);
        Assert.Equal("a\nb\n", registry.ReadFileState.Get(filePath)?.Content);
        Assert.Equal("a\nb\n", result.Output);
    }

    [Fact]
    public async Task Read_Returns_Structured_Text_Output_Metadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-structured-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(filePath, "first\nsecond\n");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "sample.txt", session, settings);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.StructuredOutput);
        Assert.Equal("text", data["type"]?.GetValue<string>());

        var file = Assert.IsType<JsonObject>(data["file"]);
        Assert.Equal("sample.txt", file["filePath"]?.GetValue<string>());
        Assert.Equal("first\nsecond\n", file["content"]?.GetValue<string>());
        Assert.Equal(3, file["numLines"]?.GetValue<int>());
        Assert.Equal(1, file["startLine"]?.GetValue<int>());
        Assert.Equal(3, file["totalLines"]?.GetValue<int>());
    }

    [Fact]
    public async Task Read_Accepts_Json_Input_With_Offset_And_Limit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-json-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(filePath, "one\ntwo\nthree\nfour\n");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync(
            "Read",
            """{"file_path":"sample.txt","offset":2,"limit":2}""",
            session,
            settings);

        Assert.True(result.Success);
        Assert.Equal("two\nthree", result.Output);
        var state = registry.ReadFileState.Get(filePath);
        Assert.NotNull(state);
        Assert.True(state!.IsPartialView);
        Assert.Equal(2, state.Offset);
        Assert.Equal(2, state.Limit);

        var data = Assert.IsType<JsonObject>(result.StructuredOutput);
        var file = Assert.IsType<JsonObject>(data["file"]);
        Assert.Equal(2, file["startLine"]?.GetValue<int>());
        Assert.Equal(2, file["numLines"]?.GetValue<int>());
        Assert.Equal(5, file["totalLines"]?.GetValue<int>());
        Assert.Equal("two\nthree", file["content"]?.GetValue<string>());
    }

    [Fact]
    public async Task Read_Returns_File_Unchanged_For_Identical_Full_Read()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-unchanged-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(filePath, "same");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var first = await registry.ExecuteAsync("Read", "sample.txt", session, settings);
        var second = await registry.ExecuteAsync("Read", "sample.txt", session, settings);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(
            "File unchanged since last read. The content from the earlier Read tool_result in this conversation is still current — refer to that instead of re-reading.",
            second.Output);
        var data = Assert.IsType<JsonObject>(second.StructuredOutput);
        Assert.Equal("file_unchanged", data["type"]?.GetValue<string>());
        Assert.Equal("sample.txt", data["file"]?["filePath"]?.GetValue<string>());
    }

    [Fact]
    public async Task Read_Rejects_Oversized_File_Without_Explicit_Range()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-size-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "large.txt");
        await File.WriteAllTextAsync(filePath, new string('a', (256 * 1024) + 10));

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "large.txt", session, settings);

        Assert.False(result.Success);
        Assert.Contains("exceeds maximum allowed size", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_Rejects_Binary_File_Extensions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-binary-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "archive.zip");
        await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4]);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "archive.zip", session, settings);

        Assert.False(result.Success);
        Assert.Contains("cannot read binary files", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_Returns_Notebook_Structured_Output()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-notebook-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.ipynb");
        await File.WriteAllTextAsync(
            filePath,
            """
            {
              "metadata": { "language_info": { "name": "python" } },
              "cells": [
                {
                  "cell_type": "markdown",
                  "id": "intro",
                  "source": ["# Title\n", "Body"]
                },
                {
                  "cell_type": "code",
                  "id": "run-1",
                  "execution_count": 4,
                  "source": ["print('hi')"],
                  "outputs": [
                    {
                      "output_type": "stream",
                      "text": ["hi\n"]
                    }
                  ]
                }
              ]
            }
            """);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "sample.ipynb", session, settings);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.StructuredOutput);
        Assert.Equal("notebook", data["type"]?.GetValue<string>());
        var file = Assert.IsType<JsonObject>(data["file"]);
        Assert.Equal("sample.ipynb", file["filePath"]?.GetValue<string>());
        var cells = Assert.IsType<JsonArray>(file["cells"]);
        Assert.Equal(2, cells.Count);
        Assert.Equal("markdown", cells[0]?["cellType"]?.GetValue<string>());
        Assert.Equal("# Title\nBody", cells[0]?["source"]?.GetValue<string>());
        Assert.Equal("code", cells[1]?["cellType"]?.GetValue<string>());
        Assert.Equal("python", cells[1]?["language"]?.GetValue<string>());
        Assert.Equal(4, cells[1]?["execution_count"]?.GetValue<int>());
        Assert.Contains("run-1", result.Output, StringComparison.Ordinal);
        Assert.Equal(result.Output, registry.ReadFileState.Get(filePath)?.Content);
    }

    [Fact]
    public async Task Read_Returns_Pdf_Structured_Output_For_Valid_Pdf()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-pdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.pdf");
        var pdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF");
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "sample.pdf", session, settings);

        Assert.True(result.Success);
        Assert.Contains("PDF file read: sample.pdf", result.Output, StringComparison.Ordinal);
        var data = Assert.IsType<JsonObject>(result.StructuredOutput);
        Assert.Equal("pdf", data["type"]?.GetValue<string>());
        var file = Assert.IsType<JsonObject>(data["file"]);
        Assert.Equal("sample.pdf", file["filePath"]?.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(pdfBytes), file["base64"]?.GetValue<string>());
        Assert.Equal(pdfBytes.Length, file["originalSize"]?.GetValue<long>());
        Assert.Null(registry.ReadFileState.Get(filePath));
    }

    [Fact]
    public async Task Read_Rejects_Invalid_Pdf_Header()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-invalid-pdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "broken.pdf");
        await File.WriteAllTextAsync(filePath, "not-a-pdf");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "broken.pdf", session, settings);

        Assert.False(result.Success);
        Assert.Contains("not a valid PDF", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_Rejects_Invalid_Pdf_Page_Range_Syntax()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-pages-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.pdf");
        await File.WriteAllBytesAsync(filePath, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"));

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync(
            "Read",
            """{"file_path":"sample.pdf","pages":"zero-two"}""",
            session,
            settings);

        Assert.False(result.Success);
        Assert.Contains("Invalid pages parameter", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_Returns_Image_Structured_Output_For_Png()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-image-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.png");
        byte[] pngBytes =
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D
        ];
        await File.WriteAllBytesAsync(filePath, pngBytes);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "sample.png", session, settings);

        Assert.True(result.Success);
        Assert.Contains("Image file read: sample.png", result.Output, StringComparison.Ordinal);
        var data = Assert.IsType<JsonObject>(result.StructuredOutput);
        Assert.Equal("image", data["type"]?.GetValue<string>());
        var file = Assert.IsType<JsonObject>(data["file"]);
        Assert.Equal("image/png", file["type"]?.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(pngBytes), file["base64"]?.GetValue<string>());
        Assert.Equal(pngBytes.Length, file["originalSize"]?.GetValue<long>());
        Assert.Null(registry.ReadFileState.Get(filePath));
    }

    [Fact]
    public async Task Read_Rejects_Oversized_Image_Without_Resize_Runtime()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-large-image-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "large.png");
        var imageBytes = new byte[(5 * 1024 * 1024 * 3) / 4 + 1];
        imageBytes[0] = 0x89;
        imageBytes[1] = 0x50;
        imageBytes[2] = 0x4E;
        imageBytes[3] = 0x47;
        await File.WriteAllBytesAsync(filePath, imageBytes);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "large.png", session, settings);

        Assert.False(result.Success);
        Assert.Contains("resize/compression image-read path", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_Extracts_Pdf_Page_Range_As_Parts_Output()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-pdf-parts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.pdf");
        await File.WriteAllBytesAsync(filePath, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"));

        ReadToolPdfPageExtractor.SetProcessRunnerForTesting(
            async (fileName, arguments, _, _) =>
            {
                if (fileName == "pdftoppm" && arguments.Length == 1 && arguments[0] == "-v")
                {
                    return new ReadToolProcessResult(0, string.Empty, "pdftoppm version");
                }

                if (fileName == "pdftoppm")
                {
                    var outputPrefix = arguments[^1];
                    var outputDir = Path.GetDirectoryName(outputPrefix)!;
                    Directory.CreateDirectory(outputDir);
                    await File.WriteAllBytesAsync(Path.Combine(outputDir, "page-01.jpg"), [0xFF, 0xD8, 0xFF]);
                    await File.WriteAllBytesAsync(Path.Combine(outputDir, "page-02.jpg"), [0xFF, 0xD8, 0xFF]);
                    return new ReadToolProcessResult(0, string.Empty, string.Empty);
                }

                return new ReadToolProcessResult(-1, string.Empty, string.Empty);
            });

        try
        {
            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();
            var settings = new ClawSharpSettings();

            var result = await registry.ExecuteAsync(
                "Read",
                """{"file_path":"sample.pdf","pages":"1-2"}""",
                session,
                settings);

            Assert.True(result.Success);
            Assert.Contains("PDF pages extracted: 2 page(s)", result.Output, StringComparison.Ordinal);
            var data = Assert.IsType<JsonObject>(result.StructuredOutput);
            Assert.Equal("parts", data["type"]?.GetValue<string>());
            var file = Assert.IsType<JsonObject>(data["file"]);
            Assert.Equal(2, file["count"]?.GetValue<int>());
            var outputDir = file["outputDir"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(outputDir));
            Assert.True(File.Exists(Path.Combine(outputDir!, "page-01.jpg")));
            Assert.True(File.Exists(Path.Combine(outputDir!, "page-02.jpg")));
        }
        finally
        {
            ReadToolPdfPageExtractor.ResetForTesting();
        }
    }

    [Fact]
    public async Task Read_Rejects_Full_Pdf_When_Page_Count_Exceeds_Inline_Threshold()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-pdf-count-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.pdf");
        await File.WriteAllBytesAsync(filePath, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"));

        ReadToolPdfPageExtractor.SetProcessRunnerForTesting(
            (_, _, _, _) => Task.FromResult(new ReadToolProcessResult(0, "Pages: 11\n", string.Empty)));

        try
        {
            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();
            var settings = new ClawSharpSettings();

            var result = await registry.ExecuteAsync("Read", "sample.pdf", session, settings);

            Assert.False(result.Success);
            Assert.Contains("too many to read at once", result.Output, StringComparison.Ordinal);
            Assert.Contains("pages parameter", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            ReadToolPdfPageExtractor.ResetForTesting();
        }
    }

    [Fact]
    public async Task Read_Falls_Back_To_Alternate_Macos_Screenshot_Path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-screenshot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var thinSpace = (char)8239;
        var actualFileName = $"Screenshot 2026-04-01 at 10.00.00{thinSpace}AM.png";
        var requestedFileName = "Screenshot 2026-04-01 at 10.00.00 AM.png";
        await File.WriteAllTextAsync(Path.Combine(tempDir, actualFileName), "pixel");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", requestedFileName, session, settings);

        Assert.True(result.Success);
        Assert.Contains("Image file read: Screenshot 2026-04-01 at 10.00.00 AM.png", result.Output, StringComparison.Ordinal);
        var data = Assert.IsType<JsonObject>(result.StructuredOutput);
        Assert.Equal("image", data["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task Read_Suggests_Similar_File_When_Base_Name_Matches()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-similar-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "report.md"), "hello");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "report.txt", session, settings);

        Assert.False(result.Success);
        Assert.Contains("Note: your current working directory is", result.Output, StringComparison.Ordinal);
        Assert.Contains("Did you mean report.md?", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_Suggests_Path_Under_Workspace_Root_When_Repo_Segment_Was_Dropped()
    {
        var parentDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-cwd-suggestion-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(parentDir, "repo");
        Directory.CreateDirectory(workspaceRoot);
        var actualFilePath = Path.Combine(workspaceRoot, "src", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(actualFilePath)!);
        await File.WriteAllTextAsync(actualFilePath, "{}");

        var requestedAbsolutePath = Path.Combine(parentDir, "src", "appsettings.json");

        var registry = new ToolRegistry(
            workspaceRoot,
            new TaskRegistry(),
            toolPermissionContext: ToolPermissionContexts.CreateEmpty(PermissionMode.BypassPermissions));
        var session = new DefaultSessionFactory(workspaceRoot).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", requestedAbsolutePath, session, settings);

        Assert.False(result.Success);
        Assert.Contains("Note: your current working directory is", result.Output, StringComparison.Ordinal);
        Assert.Contains(actualFilePath, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_Rejects_Unc_Path_Before_Filesystem_Access()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-read-unc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Read", "\\\\server\\share\\secret.txt", session, settings);

        Assert.False(result.Success);
        Assert.Contains("UNC network paths require manual approval", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_Rejects_Glob_Path_In_Create_Mode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-glob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var registry = new ToolRegistry(
            tempDir,
            new TaskRegistry(),
            toolPermissionContext: ToolPermissionContexts.CreateEmpty(PermissionMode.BypassPermissions));
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Write", """{"file_path":"logs/*.txt","content":"x"}""", session, settings);

        Assert.False(result.Success);
        Assert.Contains("Glob patterns are not allowed in write operations", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_Rejects_Shell_Expansion_Syntax_In_Path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-edit-shell-expansion-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "sample.txt"), "before");

        var registry = new ToolRegistry(
            tempDir,
            new TaskRegistry(),
            toolPermissionContext: ToolPermissionContexts.CreateEmpty(PermissionMode.BypassPermissions));
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync(
            "Edit",
            """{"file_path":"$HOME/sample.txt","old_string":"before","new_string":"after"}""",
            session,
            settings);

        Assert.False(result.Success);
        Assert.Contains("Shell expansion syntax in paths requires manual approval", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_Rejects_Dangerous_Shell_Config_File_Path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-dangerous-file-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var registry = new ToolRegistry(
            tempDir,
            new TaskRegistry(),
            toolPermissionContext: ToolPermissionContexts.CreateEmpty(PermissionMode.BypassPermissions));
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Write", """{"file_path":".bashrc","content":"alias ll='ls -la'"}""", session, settings);

        Assert.False(result.Success);
        Assert.Contains("sensitive file", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Write_Rejects_Claude_Settings_Path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-claude-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, ".claude"));

        var registry = new ToolRegistry(
            tempDir,
            new TaskRegistry(),
            toolPermissionContext: ToolPermissionContexts.CreateEmpty(PermissionMode.BypassPermissions));
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync(
            "Write",
            """{"file_path":".claude/settings.json","content":"{}"}""",
            session,
            settings);

        Assert.False(result.Success);
        Assert.Contains("haven't granted it yet", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_Rejects_Suspicious_Windows_Path_Pattern()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-windows-pattern-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var registry = new ToolRegistry(
            tempDir,
            new TaskRegistry(),
            toolPermissionContext: ToolPermissionContexts.CreateEmpty(PermissionMode.BypassPermissions));
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync(
            "Write",
            """{"file_path":".../file.txt","content":"x"}""",
            session,
            settings);

        Assert.False(result.Success);
        Assert.Contains("suspicious Windows path pattern", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Write_Does_Not_Trim_Content_After_Separator()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-trim-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync("Write", "sample.txt|  keep trailing and leading  ", session, settings);

        Assert.True(result.Success);
        Assert.Equal("  keep trailing and leading  ", await File.ReadAllTextAsync(Path.Combine(tempDir, "sample.txt")));
    }

    [Fact]
    public async Task Write_Preserves_Encoding_And_Uses_Explicit_Lf_Content()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-encoding-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "encoded.txt");
        await File.WriteAllTextAsync(filePath, "old\r\nvalue\r\n", System.Text.Encoding.Unicode);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var readResult = await registry.ExecuteAsync("Read", "encoded.txt", session, settings);
        Assert.True(readResult.Success);

        var writeResult = await registry.ExecuteAsync("Write", "encoded.txt|new\nvalue\n", session, settings);
        Assert.True(writeResult.Success);

        var bytes = await File.ReadAllBytesAsync(filePath);
        Assert.True(bytes.Length >= 2);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);

        var written = await File.ReadAllTextAsync(filePath, System.Text.Encoding.Unicode);
        Assert.Equal("new\nvalue\n", written);
    }

    [Fact]
    public async Task Edit_Returns_Ts_Shaped_Structured_Diff_Output()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-edit-structured-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(filePath, "\talpha\nbeta\n");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var readResult = await registry.ExecuteAsync("Read", "sample.txt", session, settings);
        Assert.True(readResult.Success);

        var editResult = await registry.ExecuteAsync("Edit", "sample.txt|alpha|omega", session, settings);

        Assert.True(editResult.Success);
        Assert.Equal("Edited sample.txt successfully.", editResult.Output);

        var data = Assert.IsType<JsonObject>(editResult.StructuredOutput);
        Assert.Equal("sample.txt", data["filePath"]?.GetValue<string>());
        Assert.Equal("alpha", data["oldString"]?.GetValue<string>());
        Assert.Equal("omega", data["newString"]?.GetValue<string>());
        Assert.Equal("\talpha\nbeta\n", data["originalFile"]?.GetValue<string>());
        Assert.False(data["userModified"]?.GetValue<bool>());
        Assert.False(data["replaceAll"]?.GetValue<bool>());

        var patch = Assert.IsType<JsonArray>(data["structuredPatch"]);
        var hunk = Assert.IsType<JsonObject>(Assert.Single(patch));
        Assert.Equal(1, hunk["oldStart"]?.GetValue<int>());
        Assert.Equal(1, hunk["newStart"]?.GetValue<int>());
        Assert.True(hunk["oldLines"]?.GetValue<int>() >= 2);
        Assert.True(hunk["newLines"]?.GetValue<int>() >= 2);

        var lines = Assert.IsType<JsonArray>(hunk["lines"]);
        Assert.Contains(lines, line => line?.GetValue<string>() == "-  alpha");
        Assert.Contains(lines, line => line?.GetValue<string>() == "+  omega");
        Assert.Contains(lines, line => line?.GetValue<string>() == " beta");
    }

    [Fact]
    public async Task Edit_Json_Input_Replaces_Only_First_Match_By_Default()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-edit-json-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(filePath, "alpha\nalpha\n");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var readResult = await registry.ExecuteAsync("Read", "sample.txt", session, settings);
        Assert.True(readResult.Success);

        var editResult = await registry.ExecuteAsync(
            "Edit",
            """{"file_path":"sample.txt","old_string":"alpha","new_string":"omega"}""",
            session,
            settings);

        Assert.True(editResult.Success);
        Assert.Equal("omega\nalpha\n", await File.ReadAllTextAsync(filePath));

        var data = Assert.IsType<JsonObject>(editResult.StructuredOutput);
        Assert.False(data["replaceAll"]?.GetValue<bool>());
    }

    [Fact]
    public async Task Edit_Json_Input_Honors_ReplaceAll_True()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-edit-replace-all-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(filePath, "alpha\nalpha\n");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var readResult = await registry.ExecuteAsync("Read", "sample.txt", session, settings);
        Assert.True(readResult.Success);

        var editResult = await registry.ExecuteAsync(
            "Edit",
            """{"file_path":"sample.txt","old_string":"alpha","new_string":"omega","replace_all":true}""",
            session,
            settings);

        Assert.True(editResult.Success);
        Assert.Equal("omega\nomega\n", await File.ReadAllTextAsync(filePath));

        var data = Assert.IsType<JsonObject>(editResult.StructuredOutput);
        Assert.True(data["replaceAll"]?.GetValue<bool>());
    }

    [Fact]
    public async Task Write_Update_Returns_Ts_Shaped_Structured_Diff_Output()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-structured-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sample.txt");
        await File.WriteAllTextAsync(filePath, "alpha\nbeta\n");

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var readResult = await registry.ExecuteAsync("Read", "sample.txt", session, settings);
        Assert.True(readResult.Success);

        var writeResult = await registry.ExecuteAsync("Write", "sample.txt|gamma\nbeta\n", session, settings);

        Assert.True(writeResult.Success);
        Assert.Equal("Updated sample.txt.", writeResult.Output);

        var data = Assert.IsType<JsonObject>(writeResult.StructuredOutput);
        Assert.Equal("update", data["type"]?.GetValue<string>());
        Assert.Equal("sample.txt", data["filePath"]?.GetValue<string>());
        Assert.Equal("gamma\nbeta\n", data["content"]?.GetValue<string>());
        Assert.Equal("alpha\nbeta\n", data["originalFile"]?.GetValue<string>());

        var patch = Assert.IsType<JsonArray>(data["structuredPatch"]);
        var hunk = Assert.IsType<JsonObject>(Assert.Single(patch));
        var lines = Assert.IsType<JsonArray>(hunk["lines"]);
        Assert.Contains(lines, line => line?.GetValue<string>() == "-alpha");
        Assert.Contains(lines, line => line?.GetValue<string>() == "+gamma");
        Assert.Contains(lines, line => line?.GetValue<string>() == " beta");
    }

    [Fact]
    public async Task Write_Create_Returns_Empty_StructuredPatch_And_Null_OriginalFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-create-structured-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var writeResult = await registry.ExecuteAsync("Write", "newfile.txt|hello\n", session, settings);

        Assert.True(writeResult.Success);
        Assert.Equal("Created newfile.txt.", writeResult.Output);

        var data = Assert.IsType<JsonObject>(writeResult.StructuredOutput);
        Assert.Equal("create", data["type"]?.GetValue<string>());
        Assert.Equal("newfile.txt", data["filePath"]?.GetValue<string>());
        Assert.Equal("hello\n", data["content"]?.GetValue<string>());
        Assert.True(data["originalFile"] is null);

        var patch = Assert.IsType<JsonArray>(data["structuredPatch"]);
        Assert.Empty(patch);
    }

    [Fact]
    public async Task Write_Accepts_Json_Input()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-write-json-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var registry = new ToolRegistry(tempDir, new TaskRegistry());
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();

        var result = await registry.ExecuteAsync(
            "Write",
            """{"file_path":"json.txt","content":"hello\nworld\n"}""",
            session,
            settings);

        Assert.True(result.Success);
        Assert.Equal("hello\nworld\n", await File.ReadAllTextAsync(Path.Combine(tempDir, "json.txt")));

        var data = Assert.IsType<JsonObject>(result.StructuredOutput);
        Assert.Equal("create", data["type"]?.GetValue<string>());
        Assert.Equal("json.txt", data["filePath"]?.GetValue<string>());
    }

    [Fact]
    public async Task TaskRegistry_Creates_Session_Scoped_Output_Files_And_Reads_Deltas()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);

        var task = await tasks.CreateForSessionAsync(session.Id, "background shell", TaskType.LocalBash);
        await tasks.AppendOutputAsync(task.Id, "alpha\n");
        await tasks.AppendOutputAsync(task.Id, "beta\n");

        Assert.Equal(
            TaskOutputStoragePaths.GetTaskOutputPath(tempDir, session.Id, task.Id),
            task.OutputFile);
        Assert.True(File.Exists(task.OutputFile));

        var output = await tasks.GetOutputAsync(task.Id);
        var delta = await tasks.GetOutputDeltaAsync(task.Id, 0);
        var size = await tasks.GetOutputSizeAsync(task.Id);

        Assert.Equal("alpha\nbeta\n", output);
        Assert.Equal("alpha\nbeta\n", delta.Content);
        Assert.Equal(size, delta.NewOffset);
    }

    [Fact]
    public async Task TaskRegistry_Uses_Agent_Transcript_Link_Foundation_For_LocalAgent_Tasks()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-agent-task-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);

        var task = await tasks.CreateLocalAgentForSessionAsync(session.Id, "background agent", "solve it", "general-purpose");
        var expectedOutputPath = TaskOutputStoragePaths.GetTaskOutputPath(tempDir, session.Id, task.Id);
        var expectedTranscriptPath = TaskOutputStoragePaths.GetAgentTranscriptPath(tempDir, session.Id, task.Id);

        Assert.Equal(expectedOutputPath, task.OutputFile);
        Assert.True(File.Exists(task.OutputFile));

        var linkTarget = TryResolveLinkTarget(task.OutputFile);
        if (linkTarget is not null)
        {
            Assert.Equal(Path.GetFullPath(expectedTranscriptPath), linkTarget);
        }
        else
        {
            Assert.Empty(await File.ReadAllTextAsync(task.OutputFile));
        }
    }

    [Fact]
    public async Task TaskOutputTool_Uses_Task_Type_Specific_Output_Shaping()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-shaping-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var registry = new ToolRegistry(tempDir, tasks);
        var settings = new ClawSharpSettings();

        var bashTask = await tasks.CreateLocalBashForSessionAsync(session.Id, "bash task", "echo hi", ClawSharp.Tasks.TaskStatus.Completed);
        await tasks.AppendOutputAsync(bashTask.Id, "bash-output\n");

        var agentTask = await tasks.CreateLocalAgentForSessionAsync(session.Id, "agent task", "fix bug", "general-purpose", ClawSharp.Tasks.TaskStatus.Completed);
        var agentTranscriptPath = TaskOutputStoragePaths.GetAgentTranscriptPath(tempDir, session.Id, agentTask.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(agentTranscriptPath)!);
        await File.WriteAllTextAsync(agentTranscriptPath, "raw transcript\n");
        tasks.TryUpdate(
            agentTask.Id,
            current => current is LocalAgentTask localAgentTask
                ? localAgentTask with { Result = "clean final answer", Error = "agent-error" }
                : current);

        var remoteTask = await tasks.CreateRemoteAgentForSessionAsync(session.Id, "remote task", "remote-session-1", "review repo", "Remote review", ClawSharp.Tasks.TaskStatus.Completed);
        await tasks.AppendOutputAsync(remoteTask.Id, "remote-log\n");

        var bashResult = await registry.ExecuteAsync("TaskOutput", bashTask.Id, session, settings);
        var bashStructured = Assert.IsType<JsonObject>(bashResult.StructuredOutput);
        var bashData = Assert.IsType<JsonObject>(bashStructured["task"]);
        Assert.Equal("bash-output\n", bashData["output"]?.GetValue<string>());
        Assert.Null(bashData["prompt"]);
        Assert.Null(bashData["result"]);

        var agentResult = await registry.ExecuteAsync("TaskOutput", agentTask.Id, session, settings);
        var agentStructured = Assert.IsType<JsonObject>(agentResult.StructuredOutput);
        var agentData = Assert.IsType<JsonObject>(agentStructured["task"]);
        Assert.Equal("clean final answer", agentData["output"]?.GetValue<string>());
        Assert.Equal("fix bug", agentData["prompt"]?.GetValue<string>());
        Assert.Equal("clean final answer", agentData["result"]?.GetValue<string>());
        Assert.Equal("agent-error", agentData["error"]?.GetValue<string>());

        var remoteResult = await registry.ExecuteAsync("TaskOutput", remoteTask.Id, session, settings);
        var remoteStructured = Assert.IsType<JsonObject>(remoteResult.StructuredOutput);
        var remoteData = Assert.IsType<JsonObject>(remoteStructured["task"]);
        Assert.Equal("remote-log\n", remoteData["output"]?.GetValue<string>());
        Assert.Equal("review repo", remoteData["prompt"]?.GetValue<string>());
        Assert.Null(remoteData["result"]);
    }

    [Fact]
    public async Task TaskOutputTool_Prefers_Live_Local_Shell_TaskOutput_Before_Disk_Fallback()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-live-task-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var registry = new ToolRegistry(tempDir, tasks);
        var settings = new ClawSharpSettings();

        var bashTask = await tasks.CreateLocalBashForSessionAsync(session.Id, "bash task", "echo hi", ClawSharp.Tasks.TaskStatus.Completed);
        await tasks.AppendOutputAsync(bashTask.Id, "disk-output\n");

        var liveTaskOutput = new ClawSharp.Tasks.TaskOutput(bashTask.Id, bashTask.OutputFile);
        liveTaskOutput.WriteStdout("live-stdout");
        liveTaskOutput.WriteStderr("live-stderr");

        tasks.TryUpdate(
            bashTask.Id,
            current => current is LocalBashTask localBashTask
                ? localBashTask with { ShellCommand = new LocalShellCommand(liveTaskOutput, 30_000) }
                : current);

        var result = await registry.ExecuteAsync("TaskOutput", bashTask.Id, session, settings);

        Assert.True(result.Success);
        var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
        var taskData = Assert.IsType<JsonObject>(structured["task"]);
        Assert.Equal("live-stdout\nlive-stderr", taskData["output"]?.GetValue<string>());
        Assert.DoesNotContain("disk-output", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalShellCommand_Follows_Ts_Shaped_Background_And_Kill_Transitions()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-local-shell-command-tests", Guid.NewGuid().ToString("N"), "output.txt");
        var taskOutput = new ClawSharp.Tasks.TaskOutput("b12345678", outputPath);
        var shellCommand = new LocalShellCommand(taskOutput, 30_000);

        taskOutput.WriteStdout("alpha\n");
        taskOutput.WriteStderr("beta\n");

        Assert.Equal(LocalShellCommandStatus.Running, shellCommand.Status);
        Assert.True(shellCommand.Background("b12345678"));
        Assert.False(shellCommand.Background("b12345678"));
        Assert.Equal(LocalShellCommandStatus.Backgrounded, shellCommand.Status);
        Assert.True(taskOutput.IsOverflowed);

        taskOutput.WriteStdout("gamma\n");
        taskOutput.WriteStderr("delta\n");

        shellCommand.Kill();

        var result = await shellCommand.Result;
        Assert.Equal(LocalShellCommandStatus.Killed, shellCommand.Status);
        Assert.True(result.Interrupted);
        Assert.Equal(137, result.Code);
        Assert.Equal("b12345678", result.BackgroundTaskId);
        Assert.Equal($"Output truncated (0KB total). Full output saved to: {outputPath}", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal("alpha\n[stderr] beta\ngamma\n[stderr] delta\n", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task TaskRegistry_Tracks_LocalShellCommand_Completion_And_Queues_Background_Notification()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-shell-lifecycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var queue = new InMemoryQueuedCommandQueue();
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue);

        var bashTask = await tasks.CreateLocalBashForSessionAsync(session.Id, "run build", "echo hi", ClawSharp.Tasks.TaskStatus.Pending);
        var taskOutput = new ClawSharp.Tasks.TaskOutput(bashTask.Id, bashTask.OutputFile);
        var shellCommand = new LocalShellCommand(taskOutput, 30_000);

        Assert.True(tasks.TryAttachLocalShellCommand(bashTask.Id, shellCommand));
        Assert.True(tasks.TryBackgroundLocalBashTask(bashTask.Id));

        var trackingTask = tasks.TrackLocalBashCommandAsync(bashTask.Id, shellCommand);
        taskOutput.WriteStdout("build complete\n");
        shellCommand.Complete(0);
        await trackingTask;

        Assert.True(tasks.TryGet(bashTask.Id, out var updatedTask));
        var typedTask = Assert.IsType<LocalBashTask>(updatedTask);
        Assert.Equal(ClawSharp.Tasks.TaskStatus.Completed, typedTask.Status);
        Assert.Equal(0, typedTask.ExitCode);
        Assert.Null(typedTask.ShellCommand);
        Assert.True(typedTask.Notified);
        Assert.Equal("build complete\n", await tasks.GetOutputAsync(bashTask.Id));

        var notification = queue.Dequeue();
        Assert.NotNull(notification);
        Assert.Contains("Background command \"run build\" completed (exit code 0)", notification!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskOutput_Reports_Ts_Shaped_Progress_For_Pipe_Mode_Writes()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-progress-tests", Guid.NewGuid().ToString("N"), "output.txt");
        var progressUpdates = new List<TaskOutputProgressUpdate>();
        var taskOutput = new ClawSharp.Tasks.TaskOutput("bprogress1", outputPath, progressUpdates.Add);

        taskOutput.WriteStdout("first\nsecond\n");
        taskOutput.WriteStderr("third\n");

        Assert.Single(progressUpdates);
        Assert.Equal("second", progressUpdates[0].LastLines);
        Assert.Equal("second", progressUpdates[0].AllLines);
        Assert.Equal(2, progressUpdates[0].TotalLines);
        Assert.Equal("first\nsecond\n", await taskOutput.GetStdoutAsync());
        Assert.Equal("third\n", taskOutput.GetStderr());
        Assert.Equal(3, taskOutput.TotalLines);
        Assert.True(taskOutput.TotalBytes >= "first\nsecond\nthird\n".Length);
    }

    [Fact]
    public async Task TaskOutput_PipeMode_Spills_On_Overflow_And_Persists_Stderr_Prefix()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-overflow-tests", Guid.NewGuid().ToString("N"), "output.txt");
        var taskOutput = new ClawSharp.Tasks.TaskOutput("boverflow1", outputPath, maxMemory: 8);

        taskOutput.WriteStdout("alpha\n");
        taskOutput.WriteStderr("beta\n");

        Assert.True(taskOutput.IsOverflowed);
        Assert.Equal(string.Empty, taskOutput.GetStderr());

        var stdout = await taskOutput.GetStdoutAsync();

        Assert.Equal($"Output truncated (0KB total). Full output saved to: {outputPath}", stdout);
        Assert.Equal("alpha\n[stderr] beta\n", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task TaskRegistry_Can_Store_LocalBash_LastReportedTotalLines()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-last-reported-lines-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var bashTask = await tasks.CreateLocalBashForSessionAsync(session.Id, "bash task", "echo hi", ClawSharp.Tasks.TaskStatus.Running);

        Assert.True(tasks.TryUpdateLocalBashLastReportedTotalLines(bashTask.Id, 42));
        Assert.True(tasks.TryGet(bashTask.Id, out var updatedTask));
        var typedTask = Assert.IsType<LocalBashTask>(updatedTask);
        Assert.Equal(42, typedTask.LastReportedTotalLines);
    }

    [Fact]
    public async Task TaskOutput_FileMode_Poller_Reports_Ts_Shaped_Disk_Progress()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-file-progress-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var progressUpdates = new List<TaskOutputProgressUpdate>();
        var taskOutput = new ClawSharp.Tasks.TaskOutput("bfilepoll", outputPath, progressUpdates.Add, stdoutToFile: true);

        await File.WriteAllTextAsync(outputPath, "alpha\nbeta\ngamma\n");

        ClawSharp.Tasks.TaskOutput.StartPolling(taskOutput.TaskId);
        await Task.Delay(1300);
        ClawSharp.Tasks.TaskOutput.StopPolling(taskOutput.TaskId);

        Assert.NotEmpty(progressUpdates);
        var last = progressUpdates[^1];
        Assert.Equal("alpha\nbeta\ngamma\n", last.LastLines);
        Assert.Equal("alpha\nbeta\ngamma\n", last.AllLines);
        Assert.Equal(4, last.TotalLines);
        Assert.Equal(new FileInfo(outputPath).Length, last.TotalBytes);
        Assert.False(last.IsIncomplete);

        taskOutput.Clear();
    }

    [Fact]
    public async Task TaskOutput_FileMode_Reads_From_Disk_And_Sets_Output_Metadata()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-file-read-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "disk-stdout\n");
        var taskOutput = new ClawSharp.Tasks.TaskOutput("bfileread", outputPath, stdoutToFile: true);

        var stdout = await taskOutput.GetStdoutAsync();

        Assert.Equal("disk-stdout\n", stdout);
        Assert.Equal(string.Empty, taskOutput.GetStderr());
        Assert.True(taskOutput.OutputFileRedundant);
        Assert.Equal(new FileInfo(outputPath).Length, taskOutput.OutputFileSize);

        taskOutput.Clear();
    }

    [Fact]
    public async Task TaskOutput_FileMode_Returns_Ts_Shaped_Diagnostic_When_Output_File_Is_Missing()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-file-missing-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var taskOutput = new ClawSharp.Tasks.TaskOutput("bfilemissing", outputPath, stdoutToFile: true);

        var stdout = await taskOutput.GetStdoutAsync();

        Assert.Equal(
            $"<bash output unavailable: output file {outputPath} could not be read (ENOENT). This usually means another Claude Code process in the same project deleted it during startup cleanup.>",
            stdout);
        Assert.False(taskOutput.OutputFileRedundant);
        Assert.Equal(0, taskOutput.OutputFileSize);
    }

    [Fact]
    public async Task LocalShellCommand_FileMode_Deletes_Redundant_Output_File_On_Completion()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-local-shell-file-result-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "disk-stdout\n");
        var taskOutput = new ClawSharp.Tasks.TaskOutput("bfileresult1", outputPath, stdoutToFile: true);
        var shellCommand = new LocalShellCommand(taskOutput, 30_000);

        Assert.True(shellCommand.Complete(0));

        var result = await shellCommand.Result;

        Assert.Equal(LocalShellCommandStatus.Completed, shellCommand.Status);
        Assert.Equal("disk-stdout\n", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Null(result.OutputFilePath);
        Assert.Null(result.OutputFileSize);
        Assert.Null(result.OutputTaskId);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task LocalShellCommand_FileMode_Exposes_Large_Output_File_Metadata_On_Completion()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-local-shell-large-file-result-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var largeContent = new string('a', DiskTaskOutputStore.DefaultMaxReadBytes + 128);
        await File.WriteAllTextAsync(outputPath, largeContent);
        var taskOutput = new ClawSharp.Tasks.TaskOutput("bfileresult2", outputPath, stdoutToFile: true);
        var shellCommand = new LocalShellCommand(taskOutput, 30_000);

        Assert.True(shellCommand.Complete(0));

        var result = await shellCommand.Result;

        Assert.Equal(LocalShellCommandStatus.Completed, shellCommand.Status);
        Assert.Equal(DiskTaskOutputStore.DefaultMaxReadBytes, result.Stdout.Length);
        Assert.Equal(outputPath, result.OutputFilePath);
        Assert.Equal(new FileInfo(outputPath).Length, result.OutputFileSize);
        Assert.Equal("bfileresult2", result.OutputTaskId);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void QueuedCommandQueue_Uses_Ts_Defaults_And_Priority_Order()
    {
        var queue = new InMemoryQueuedCommandQueue();

        queue.EnqueuePendingNotification(new QueuedCommand("later", PromptInputMode.TaskNotification));
        queue.Enqueue(new QueuedCommand("next", PromptInputMode.Prompt));
        queue.Enqueue(new QueuedCommand("now", PromptInputMode.Prompt, QueuePriority.Now));

        Assert.Equal(3, queue.Snapshot().Count);
        Assert.Equal("now", queue.Peek()?.Value);
        Assert.Equal("later", queue.Peek(command => command.Mode == PromptInputMode.TaskNotification)?.Value);

        var first = queue.Dequeue();
        var second = queue.Dequeue();
        var third = queue.Dequeue();

        Assert.Equal("now", first?.Value);
        Assert.Equal(QueuePriority.Now, first?.Priority);
        Assert.Equal("next", second?.Value);
        Assert.Equal(QueuePriority.Next, second?.Priority);
        Assert.Equal("later", third?.Value);
        Assert.Equal(QueuePriority.Later, third?.Priority);
        Assert.Null(queue.Dequeue());

        queue.Enqueue(new QueuedCommand("prompt-1", PromptInputMode.Prompt));
        queue.EnqueuePendingNotification(new QueuedCommand("notification-1", PromptInputMode.TaskNotification));
        queue.Enqueue(new QueuedCommand("prompt-2", PromptInputMode.Prompt, QueuePriority.Now));
        queue.EnqueuePendingNotification(new QueuedCommand("notification-2", PromptInputMode.TaskNotification, QueuePriority.Now));

        var notifications = queue.DequeueAllMatching(command => command.Mode == PromptInputMode.TaskNotification);

        Assert.Equal(["notification-1", "notification-2"], notifications.Select(command => command.Value).ToArray());
        Assert.Equal(["prompt-1", "prompt-2"], queue.Snapshot().Select(command => command.Value).ToArray());
    }

    [Fact]
    public async Task TaskRegistry_Enqueues_Task_Notifications_With_Ts_Shaped_Xml_And_Dedupes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-task-notification-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var queue = new InMemoryQueuedCommandQueue();
        var eventSink = new InMemoryEventSink();
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, eventSink: eventSink);

        var bashTask = await tasks.CreateLocalBashForSessionAsync(session.Id, "run build", "dotnet build", ClawSharp.Tasks.TaskStatus.Completed);
        tasks.TryUpdate(
            bashTask.Id,
            current => current is LocalBashTask localBashTask
                ? localBashTask with { ExitCode = 0 }
                : current);

        Assert.True(tasks.TryEnqueueLocalBashNotification(bashTask.Id));
        Assert.False(tasks.TryEnqueueLocalBashNotification(bashTask.Id));

        var bashNotification = queue.Dequeue();
        Assert.NotNull(bashNotification);
        Assert.Equal(PromptInputMode.TaskNotification, bashNotification!.Mode);
        Assert.Equal(QueuePriority.Later, bashNotification.Priority);
        Assert.Contains("<task-notification>", bashNotification.Value, StringComparison.Ordinal);
        Assert.Contains("<output-file>", bashNotification.Value, StringComparison.Ordinal);
        Assert.Contains("Background command \"run build\" completed (exit code 0)", bashNotification.Value, StringComparison.Ordinal);

        var agentTask = await tasks.CreateLocalAgentForSessionAsync(session.Id, "agent task", "fix bug", "general-purpose", ClawSharp.Tasks.TaskStatus.Completed);
        Assert.True(tasks.TryEnqueueLocalAgentNotification(
            agentTask.Id,
            finalMessage: "done",
            usage: new TaskNotificationUsage(42, 3, 1200),
            worktreePath: "D:\\repo",
            worktreeBranch: "feature/test",
            priority: QueuePriority.Next));

        var agentNotification = queue.Dequeue();
        Assert.NotNull(agentNotification);
        Assert.Equal(QueuePriority.Next, agentNotification!.Priority);
        Assert.Contains("<result>done</result>", agentNotification.Value, StringComparison.Ordinal);
        Assert.Contains("<usage><total_tokens>42</total_tokens><tool_uses>3</tool_uses><duration_ms>1200</duration_ms></usage>", agentNotification.Value, StringComparison.Ordinal);
        Assert.Contains("<worktree><worktreePath>D:\\repo</worktreePath><worktreeBranch>feature/test</worktreeBranch></worktree>", agentNotification.Value, StringComparison.Ordinal);

        var failedAgentTask = await tasks.CreateLocalAgentForSessionAsync(session.Id, "failed agent", "fix bug", "general-purpose", ClawSharp.Tasks.TaskStatus.Failed);
        Assert.True(tasks.TryEnqueueLocalAgentNotification(
            failedAgentTask.Id,
            error: "tool crashed"));

        var failedAgentNotification = queue.Dequeue();
        Assert.NotNull(failedAgentNotification);
        Assert.Contains("<summary>Agent \"failed agent\" failed: tool crashed</summary>", failedAgentNotification!.Value, StringComparison.Ordinal);

        var remoteTask = await tasks.CreateRemoteAgentForSessionAsync(session.Id, "remote task", "session-1", "review repo", "Remote review", ClawSharp.Tasks.TaskStatus.Failed);
        Assert.True(tasks.TryEnqueueRemoteAgentNotification(remoteTask.Id));

        var remoteNotification = queue.Dequeue();
        Assert.NotNull(remoteNotification);
        Assert.Contains("<task-type>remote_agent</task-type>", remoteNotification!.Value, StringComparison.Ordinal);
        Assert.Contains("Remote task \"Remote review\" failed", remoteNotification.Value, StringComparison.Ordinal);

        Assert.Contains(eventSink.Events, appEvent => appEvent.Type == AppEventType.NotificationRaised);
    }

    [Fact]
    public async Task TaskRegistry_Exposes_Task_App_State_Slice_With_Ts_Shaped_Get_Set_Semantics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-task-app-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var task = await tasks.CreateForSessionAsync(session.Id, "state-backed task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Completed);

        var state = tasks.GetAppState();
        Assert.True(state.Tasks.TryGetValue(task.Id, out var currentTask));
        Assert.False(currentTask?.Notified);

        tasks.SetAppState(
            previousState =>
            {
                var updatedTasks = new Dictionary<string, ClawSharpTask>(previousState.Tasks, StringComparer.Ordinal)
                {
                    [task.Id] = currentTask! with { Notified = true }
                };

                return new TaskAppState(updatedTasks);
            });

        Assert.True(tasks.TryGet(task.Id, out var updatedTask));
        Assert.True(updatedTask?.Notified);
    }

    [Fact]
    public async Task TaskOutputTool_Supports_NonBlocking_And_Blocking_Polling()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-task-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var registry = new ToolRegistry(tempDir, tasks);
        var settings = new ClawSharpSettings();

        var runningTask = await tasks.CreateForSessionAsync(session.Id, "running task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Running);
        await tasks.AppendOutputAsync(runningTask.Id, "still running\n");

        var nonBlocking = await registry.ExecuteAsync(
            "TaskOutput",
            $$"""{"task_id":"{{runningTask.Id}}","block":false}""",
            session,
            settings);

        Assert.True(nonBlocking.Success);
        var nonBlockingStructured = Assert.IsType<JsonObject>(nonBlocking.StructuredOutput);
        Assert.Equal("not_ready", nonBlockingStructured["retrieval_status"]?.GetValue<string>());

        var completedTask = await tasks.CreateForSessionAsync(session.Id, "completed task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Running);
        await tasks.AppendOutputAsync(completedTask.Id, "line 1\n");
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(150);
                await tasks.AppendOutputAsync(completedTask.Id, "line 2\n");
                tasks.TryUpdateStatus(completedTask.Id, ClawSharp.Tasks.TaskStatus.Completed);
            });

        var blocking = await registry.ExecuteAsync(
            "TaskOutput",
            $$"""{"task_id":"{{completedTask.Id}}","timeout":1000}""",
            session,
            settings);

        Assert.True(blocking.Success);
        Assert.Contains("<retrieval_status>success</retrieval_status>", blocking.Output, StringComparison.Ordinal);
        Assert.Contains("line 1", blocking.Output, StringComparison.Ordinal);
        Assert.Contains("line 2", blocking.Output, StringComparison.Ordinal);

        var blockingStructured = Assert.IsType<JsonObject>(blocking.StructuredOutput);
        Assert.Equal("success", blockingStructured["retrieval_status"]?.GetValue<string>());
        Assert.True(tasks.TryGet(completedTask.Id, out var refreshedTask));
        Assert.True(refreshedTask?.Notified);
    }

    [Fact]
    public async Task TaskOutputTool_Emits_Ts_Shaped_Waiting_For_Task_Progress()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-task-progress-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var registry = new ToolRegistry(tempDir, tasks);
        var settings = new ClawSharpSettings();
        var task = await tasks.CreateForSessionAsync(session.Id, "progress task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Running);
        var progressUpdates = new List<ToolProgressUpdate>();
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(150);
                tasks.TryUpdateStatus(task.Id, ClawSharp.Tasks.TaskStatus.Completed);
            });

        var result = await registry.ExecuteAsync(
            "TaskOutput",
            $$"""{"task_id":"{{task.Id}}","timeout":1000}""",
            session,
            settings,
            progressUpdates.Add);

        Assert.True(result.Success);
        var progressUpdate = Assert.Single(progressUpdates);
        Assert.StartsWith("task-output-waiting-", progressUpdate.ToolUseId, StringComparison.Ordinal);
        Assert.Equal("waiting_for_task", progressUpdate.Data["type"]?.GetValue<string>());
        Assert.Equal("progress task", progressUpdate.Data["taskDescription"]?.GetValue<string>());
        Assert.Equal("local_bash", progressUpdate.Data["taskType"]?.GetValue<string>());
    }

    [Fact]
    public void TaskOutputTool_Renders_Ts_Shaped_Waiting_For_Task_Progress_Message()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());

        Assert.True(registry.TryResolve("TaskOutput", out var tool));
        Assert.NotNull(tool);

        var rendered = tool!.RenderToolUseProgressMessage(
            [
                new ToolProgressUpdate(
                    "progress-1",
                    new JsonObject
                    {
                        ["type"] = "waiting_for_task",
                        ["taskDescription"] = "progress task",
                        ["taskType"] = "local_bash"
                    })
            ]);

        Assert.Equal(
            $"  progress task{Environment.NewLine}     Waiting for task (esc to give additional instructions)",
            rendered);
    }

    [Fact]
    public void ToolProgressMessageRenderer_Renders_TaskOutput_Progress_From_Inline_Message()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var renderer = new ToolProgressMessageRenderer(registry);
        var toolNamesByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        var progressMessagesByParentToolUseId = new Dictionary<string, List<ToolProgressUpdate>>(StringComparer.Ordinal);
        var toolUseMessage = ChatMessageFactory.CreateToolUse(
            [
                ("tooluse-task-output", "TaskOutput", """{"task_id":"task-123"}""")
            ]);
        renderer.TrackToolUses(toolUseMessage, toolNamesByToolUseId);

        var progressMessage = ChatMessageFactory.CreateProgress(
            "progress-1",
            "tooluse-task-output",
            new JsonObject
            {
                ["type"] = "waiting_for_task",
                ["taskDescription"] = "tracked task",
                ["taskType"] = "local_bash"
            });

        var rendered = renderer.TryRender(
            progressMessage,
            toolNamesByToolUseId,
            progressMessagesByParentToolUseId);

        Assert.NotNull(rendered);
        Assert.Equal("tooluse-task-output", rendered!.ParentToolUseId);
        Assert.Equal(
            $"  tracked task{Environment.NewLine}     Waiting for task (esc to give additional instructions)",
            rendered.Content);
        var storedProgress = Assert.Single(progressMessagesByParentToolUseId["tooluse-task-output"]);
        Assert.Equal("waiting_for_task", storedProgress.Data["type"]?.GetValue<string>());
    }

    [Fact]
    public void TaskOutputTool_Renders_Structured_Local_Agent_Result_Message()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());

        Assert.True(registry.TryResolve("TaskOutput", out var tool));
        Assert.NotNull(tool);

        var rendered = tool!.RenderToolResultMessage(
            "<retrieval_status>success</retrieval_status>",
            new JsonObject
            {
                ["retrieval_status"] = "success",
                ["task"] = new JsonObject
                {
                    ["task_id"] = "task-123",
                    ["task_type"] = "local_agent",
                    ["status"] = "completed",
                    ["description"] = "agent task",
                    ["output"] = "raw transcript",
                    ["result"] = "clean final answer"
                }
            },
            []);

        Assert.Equal("clean final answer", rendered);
    }

    [Fact]
    public void ToolResultMessageRenderer_Renders_TaskOutput_Result_From_Structured_Metadata()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var renderer = new ToolResultMessageRenderer(registry);
        var toolNamesByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tooluse-task-output"] = "TaskOutput"
        };
        var progressMessagesByParentToolUseId = new Dictionary<string, List<ToolProgressUpdate>>(StringComparer.Ordinal);

        var resultMessage = ChatMessageFactory.CreateToolResult(
            "tooluse-task-output",
            "TaskOutput",
            "<retrieval_status>success</retrieval_status>",
            new JsonObject
            {
                ["retrieval_status"] = "success",
                ["task"] = new JsonObject
                {
                    ["task_id"] = "task-123",
                    ["task_type"] = "local_bash",
                    ["status"] = "completed",
                    ["description"] = "bash task",
                    ["output"] = "line 1\nline 2\n"
                }
            });

        var rendered = renderer.TryRender(
            resultMessage,
            toolNamesByToolUseId,
            progressMessagesByParentToolUseId);

        Assert.NotNull(rendered);
        Assert.Equal("tooluse-task-output", rendered!.ToolUseId);
        Assert.Equal("line 1\nline 2", rendered.Content);
    }

    [Fact]
    public async Task TaskOutputTool_Truncates_Rendered_Output_But_Preserves_Full_Structured_Output()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-format-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var registry = new ToolRegistry(tempDir, tasks);
        var settings = new ClawSharpSettings();
        var task = await tasks.CreateForSessionAsync(session.Id, "long task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Completed);
        var output = new string('x', 40_000);
        await tasks.AppendOutputAsync(task.Id, output);

        var result = await registry.ExecuteAsync("TaskOutput", task.Id, session, settings);

        Assert.True(result.Success);
        Assert.Contains($"[Truncated. Full output: {task.OutputFile}]", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"<output>{output}</output>", result.Output, StringComparison.Ordinal);

        var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
        var taskData = Assert.IsType<JsonObject>(structured["task"]);
        Assert.Equal(output, taskData["output"]?.GetValue<string>());
    }

    [Fact]
    public async Task TaskOutputTool_Uses_Bounded_Task_Max_Output_Length_From_Environment()
    {
        await TaskOutputLengthEnvironmentLock.WaitAsync();
        try
        {
            var previousValue = Environment.GetEnvironmentVariable("TASK_MAX_OUTPUT_LENGTH");
            Environment.SetEnvironmentVariable("TASK_MAX_OUTPUT_LENGTH", "10");

            try
            {
                await TaskOutputTool_Uses_Bounded_Task_Max_Output_Length_From_Environment_Core();
            }
            finally
            {
                Environment.SetEnvironmentVariable("TASK_MAX_OUTPUT_LENGTH", previousValue);
            }
        }
        finally
        {
            TaskOutputLengthEnvironmentLock.Release();
        }
    }

    private static async Task TaskOutputTool_Uses_Bounded_Task_Max_Output_Length_From_Environment_Core()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-task-output-env-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var registry = new ToolRegistry(tempDir, tasks);
        var settings = new ClawSharpSettings();
        var task = await tasks.CreateForSessionAsync(session.Id, "env bounded task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Completed);
        var output = "abcdefghijklmnopqrstuvwxyz";
        await tasks.AppendOutputAsync(task.Id, output);

        var result = await registry.ExecuteAsync("TaskOutput", task.Id, session, settings);

        Assert.True(result.Success);
        Assert.Contains("[Truncated. Full output:", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(output, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryEngine_Uses_Tool_Orchestration_Flow_With_Tool_Result_Messages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileA = Path.Combine(tempDir, "a.txt");
        var fileB = Path.Combine(tempDir, "b.txt");
        await File.WriteAllTextAsync(fileA, "alpha");
        await File.WriteAllTextAsync(fileB, "beta");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, queuedTaskNotificationDrainer);
        var session = new DefaultSessionFactory(tempDir).Create();
        var chunks = new List<string>();
        var request = QueryTurnRequest.Create(
            session,
            "Read a.txt and b.txt",
            [
                new ToolCallRequest("tooluse-a", "Read", "a.txt"),
                new ToolCallRequest("tooluse-b", "Read", "b.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(
            session,
            request,
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            });

        Assert.True(result.UsedTool);
        Assert.Equal(new[] { "Read", "Read" }, result.ToolNames);
        Assert.NotEmpty(chunks);
        Assert.Equal(QueryTerminalReason.Completed, result.Terminal.Reason);
        Assert.Equal(1, result.State.TurnCount);
        Assert.Equal(session.Messages.Count, result.State.Messages.Count);
        Assert.Collection(
            session.Messages,
            message => Assert.Equal(MessageRole.User, message.Role),
            message =>
            {
                Assert.Equal(MessageRole.Assistant, message.Role);
                Assert.Equal(2, message.ContentBlocks.Count(block => block.Kind == MessageContentKind.ToolUse));
            },
            message => Assert.Equal(MessageRole.User, message.Role),
            message => Assert.Equal(MessageRole.User, message.Role),
            message => Assert.Equal(MessageRole.Assistant, message.Role));
        Assert.NotNull(result.AssistantMessage);
        Assert.Contains("alpha", result.AssistantMessage!.Content);
        Assert.Contains("beta", result.AssistantMessage.Content);
        Assert.Contains(eventSink.Events, appEvent => appEvent.Type == AppEventType.ToolExecutionStarted);
        Assert.Contains(eventSink.Events, appEvent => appEvent.Type == AppEventType.ToolExecutionCompleted);
    }

    [Fact]
    public async Task ToolOrchestrator_Publishes_TaskOutput_Progress_Events()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-tool-progress-event-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var tasks = new TaskRegistry(tempDir);
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, tasks);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var task = await tasks.CreateForSessionAsync(session.Id, "evented task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Running);
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(150);
                tasks.TryUpdateStatus(task.Id, ClawSharp.Tasks.TaskStatus.Completed);
            });

        var records = await orchestrator.RunAsync(
            [
                new ToolCallRequest("tooluse-task-output", "TaskOutput", $$"""{"task_id":"{{task.Id}}","timeout":1000}""")
            ],
            session,
            settings);

        Assert.Single(records);
        var progressEvent = Assert.Single(eventSink.Events, appEvent => appEvent.Type == AppEventType.ToolExecutionProgress);
        Assert.Equal("TaskOutput", progressEvent.Metadata?["toolName"]);
        Assert.Equal("tooluse-task-output", progressEvent.Metadata?["toolUseId"]);
        Assert.Equal("waiting_for_task", progressEvent.Metadata?["type"]);
        Assert.Equal("evented task", progressEvent.Metadata?["taskDescription"]);
        Assert.Equal("local_bash", progressEvent.Metadata?["taskType"]);
        Assert.StartsWith("task-output-waiting-", progressEvent.Metadata?["progressToolUseId"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryEngine_Records_TaskOutput_Progress_Inline_In_Session_And_Transcript()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-progress-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var taskRegistry = new TaskRegistry(tempDir);
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, queuedTaskNotificationDrainer);
        var session = new DefaultSessionFactory(tempDir).Create();
        var task = await taskRegistry.CreateForSessionAsync(session.Id, "tracked task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Running);

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(150);
                taskRegistry.TryUpdateStatus(task.Id, ClawSharp.Tasks.TaskStatus.Completed);
            });

        var request = QueryTurnRequest.Create(
            session,
            "Wait for tracked task",
            [
                new ToolCallRequest("tooluse-task-output", "TaskOutput", $$"""{"task_id":"{{task.Id}}","timeout":1000}""")
            ]);

        await queryEngine.RunTurnAsync(session, request);

        Assert.Equal(5, session.Messages.Count);
        Assert.Equal(MessageRole.User, session.Messages[0].Role);
        Assert.Equal(MessageRole.Assistant, session.Messages[1].Role);
        Assert.Equal(MessageRole.System, session.Messages[2].Role);
        Assert.Equal(MessageContentKind.Progress, Assert.Single(session.Messages[2].ContentBlocks).Kind);
        Assert.Equal(MessageRole.User, session.Messages[3].Role);
        Assert.Equal(MessageRole.Assistant, session.Messages[4].Role);

        var progressBlock = session.Messages[2].ContentBlocks[0];
        var progressData = Assert.IsType<JsonObject>(JsonNode.Parse(progressBlock.Value));
        Assert.Equal("waiting_for_task", progressData["type"]?.GetValue<string>());
        Assert.Equal("tracked task", progressData["taskDescription"]?.GetValue<string>());
        Assert.Equal("local_bash", progressData["taskType"]?.GetValue<string>());
        Assert.Equal("tooluse-task-output", progressBlock.Metadata?["parentToolUseId"]);
        Assert.StartsWith("task-output-waiting-", progressBlock.Metadata?["toolUseId"], StringComparison.Ordinal);

        var transcriptLines = await File.ReadAllLinesAsync(session.TranscriptPath);
        Assert.Equal(6, transcriptLines.Length);
        Assert.Contains("\"type\":\"file-history-snapshot\"", transcriptLines[0], StringComparison.Ordinal);
        Assert.Contains("\"type\":\"progress\"", transcriptLines[3], StringComparison.Ordinal);
        Assert.Contains("\"toolUseID\":\"task-output-waiting-", transcriptLines[3], StringComparison.Ordinal);
        Assert.Contains("\"parentToolUseID\":\"tooluse-task-output\"", transcriptLines[3], StringComparison.Ordinal);
        Assert.Contains("\"waiting_for_task\"", transcriptLines[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryEngine_OnMessage_Callback_Receives_Inline_TaskOutput_Progress_Message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-progress-callback-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var taskRegistry = new TaskRegistry(tempDir);
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, queuedTaskNotificationDrainer);
        var session = new DefaultSessionFactory(tempDir).Create();
        var task = await taskRegistry.CreateForSessionAsync(session.Id, "callback task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Running);
        var observedMessages = new List<ChatMessage>();

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(150);
                taskRegistry.TryUpdateStatus(task.Id, ClawSharp.Tasks.TaskStatus.Completed);
            });

        var request = QueryTurnRequest.Create(
            session,
            "Wait for callback task",
            [
                new ToolCallRequest("tooluse-task-output", "TaskOutput", $$"""{"task_id":"{{task.Id}}","timeout":1000}""")
            ]);

        await queryEngine.RunTurnAsync(
            session,
            request,
            onTextDelta: null,
            onMessage: (message, _) =>
            {
                observedMessages.Add(message);
                return Task.CompletedTask;
            });

        var progressMessage = Assert.Single(
            observedMessages,
            message =>
                message.Role == MessageRole.System &&
                message.ContentBlocks.Count == 1 &&
                message.ContentBlocks[0].Kind == MessageContentKind.Progress);
        var progressBlock = progressMessage.ContentBlocks[0];
        var progressData = Assert.IsType<JsonObject>(JsonNode.Parse(progressBlock.Value));

        Assert.Equal("waiting_for_task", progressData["type"]?.GetValue<string>());
        Assert.Equal("callback task", progressData["taskDescription"]?.GetValue<string>());
        Assert.Equal("tooluse-task-output", progressBlock.Metadata?["parentToolUseId"]);
    }

    [Fact]
    public async Task QueryEngine_OnMessage_Callback_Receives_Structured_TaskOutput_Result_Message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-result-callback-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var taskRegistry = new TaskRegistry(tempDir);
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, queuedTaskNotificationDrainer);
        var session = new DefaultSessionFactory(tempDir).Create();
        var task = await taskRegistry.CreateForSessionAsync(session.Id, "finished task", TaskType.LocalBash, ClawSharp.Tasks.TaskStatus.Completed);
        await taskRegistry.AppendOutputAsync(task.Id, "done\n");
        var observedMessages = new List<ChatMessage>();

        var request = QueryTurnRequest.Create(
            session,
            "Read finished task output",
            [
                new ToolCallRequest("tooluse-task-output", "TaskOutput", $$"""{"task_id":"{{task.Id}}","timeout":1000}""")
            ]);

        await queryEngine.RunTurnAsync(
            session,
            request,
            onTextDelta: null,
            onMessage: (message, _) =>
            {
                observedMessages.Add(message);
                return Task.CompletedTask;
            });

        var resultMessage = Assert.Single(
            observedMessages,
            message =>
                message.Role == MessageRole.User &&
                message.ContentBlocks.Count == 1 &&
                message.ContentBlocks[0].Kind == MessageContentKind.ToolResult);
        var resultBlock = resultMessage.ContentBlocks[0];

        Assert.Equal("tooluse-task-output", resultBlock.Metadata?["toolUseId"]);
        Assert.True(resultBlock.Metadata?.ContainsKey("structuredOutput"));

        var structuredOutput = Assert.IsType<JsonObject>(JsonNode.Parse(resultBlock.Metadata!["structuredOutput"]));
        Assert.Equal("success", structuredOutput["retrieval_status"]?.GetValue<string>());
        var taskData = Assert.IsType<JsonObject>(structuredOutput["task"]);
        Assert.Equal("local_bash", taskData["task_type"]?.GetValue<string>());
        Assert.Equal("done\n", taskData["output"]?.GetValue<string>());
    }

    [Fact]
    public async Task QueryEngine_OnEvent_Callback_Receives_Query_Consumer_Events_In_Runtime_Order()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-consumer-event-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "note.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, queuedTaskNotificationDrainer);
        var session = new DefaultSessionFactory(tempDir).Create();
        var observedEvents = new List<QueryConsumerEvent>();

        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(
            session,
            request,
            onTextDelta: null,
            onMessage: null,
            onEvent: (queryEvent, _) =>
            {
                observedEvents.Add(queryEvent);
                return Task.CompletedTask;
            });

        Assert.True(observedEvents.Count >= 8);
        Assert.IsType<QueryRequestStartConsumerEvent>(observedEvents[0]);
        Assert.IsType<QueryMessageConsumerEvent>(observedEvents[1]);
        Assert.IsType<QueryMessageConsumerEvent>(observedEvents[2]);
        Assert.IsType<QueryMessageConsumerEvent>(observedEvents[3]);
        Assert.IsType<QueryStreamDeltaConsumerEvent>(observedEvents[4]);
        Assert.IsType<QueryLoopTerminalConsumerEvent>(observedEvents[^2]);

        var resultEvent = Assert.IsType<QueryResultConsumerEvent>(observedEvents[^1]);
        Assert.NotNull(result.AssistantMessage);
        Assert.NotNull(resultEvent.Result.AssistantMessage);
        Assert.Equal(result.AssistantMessage!.Id, resultEvent.Result.AssistantMessage!.Id);
        Assert.Equal(QueryTerminalReason.Completed, resultEvent.Result.Terminal.Reason);

        var firstMessageEvent = Assert.IsType<QueryMessageConsumerEvent>(observedEvents[1]);
        Assert.Equal(MessageRole.Assistant, firstMessageEvent.Message.Role);
        Assert.Equal(MessageContentKind.ToolUse, Assert.Single(firstMessageEvent.Message.ContentBlocks).Kind);

        var toolResultEvent = Assert.IsType<QueryMessageConsumerEvent>(observedEvents[2]);
        Assert.Equal(MessageRole.User, toolResultEvent.Message.Role);
        Assert.Equal(MessageContentKind.ToolResult, Assert.Single(toolResultEvent.Message.ContentBlocks).Kind);

        var assistantEvent = Assert.IsType<QueryMessageConsumerEvent>(observedEvents[3]);
        Assert.Equal(MessageRole.Assistant, assistantEvent.Message.Role);
        Assert.Equal(MessageContentKind.Text, Assert.Single(assistantEvent.Message.ContentBlocks).Kind);
    }

    [Fact]
    public async Task QueryEngine_Drains_Queued_Task_Notifications_Before_Explicit_Tool_Turns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-notification-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "note.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, queuedTaskNotificationDrainer);
        var session = new DefaultSessionFactory(tempDir).Create();
        queue.EnqueuePendingNotification(new QueuedCommand(
            "<task-notification>\n<task-id>task-123</task-id>\n<status>completed</status>\n<summary>Background command completed</summary>\n</task-notification>",
            PromptInputMode.TaskNotification));

        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        await queryEngine.RunTurnAsync(session, request);

        Assert.Equal(5, session.Messages.Count);
        Assert.Equal(MessageRole.User, session.Messages[0].Role);
        Assert.Contains("<task-notification>", session.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("Read note.txt", session.Messages[1].Content);
        Assert.Empty(queue.Snapshot());
    }

    [Fact]
    public async Task QueryEngine_Rejects_Plain_Prompt_Until_Query_Loop_Is_Ported()
    {
        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(Environment.CurrentDirectory, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, queuedTaskNotificationDrainer);
        var session = new DefaultSessionFactory(Environment.CurrentDirectory).Create();

        await Assert.ThrowsAsync<QueryExecutionNotImplementedException>(
            () => queryEngine.RunTurnAsync(session, "plain prompt"));

        Assert.Single(session.Messages);
        Assert.Equal(MessageRole.User, session.Messages[0].Role);
    }

    private sealed class AliasTestTool : IClawSharpTool
    {
        public AliasTestTool()
        {
            Descriptor = new ToolDescriptor(
                "AliasPrimary",
                "Alias test tool",
                Aliases:
                [
                    "alias-secondary"
                ]);
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => true;

        public bool IsConcurrencySafe(string arguments) => false;

        public bool IsReadOnly(string arguments) => false;

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolValidationResult.Valid());
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolExecutionResult(true, "ok"));
        }
    }

    private sealed class DisabledTestTool : IClawSharpTool
    {
        public DisabledTestTool()
        {
            Descriptor = new ToolDescriptor("DisabledTool", "Disabled test tool");
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => false;

        public bool IsConcurrencySafe(string arguments) => false;

        public bool IsReadOnly(string arguments) => false;

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolValidationResult.Valid());
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolExecutionResult(true, "disabled"));
        }
    }

    private static string? TryResolveLinkTarget(string path)
    {
        try
        {
            var target = File.ResolveLinkTarget(path, returnFinalTarget: false);
            return target is null
                ? null
                : Path.GetFullPath(Path.IsPathRooted(target.FullName)
                    ? target.FullName
                    : Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, target.FullName));
        }
        catch
        {
            return null;
        }
    }
}

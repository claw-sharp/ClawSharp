using System;
using System.IO;
using System.Threading.Tasks;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using Xunit;
using Moq;

namespace ClawSharp.UnitTests;

public sealed class MemoryStorageServiceTests
{
    private readonly Mock<IMemoryPathResolver> _pathResolverMock = new();
    private readonly MemoryStorageService _service;

    public MemoryStorageServiceTests()
    {
        _service = new MemoryStorageService(_pathResolverMock.Object);
    }

    [Fact]
    public void IsEnabled_Default_ReturnsTrue()
    {
        var environment = new StartupEnvironment("", false, false, false);
        var settings = new RuntimeSettings();

        var result = _service.IsEnabled(environment, settings);

        Assert.True(result);
    }

    [Fact]
    public void IsEnabled_BareMode_ReturnsFalse()
    {
        var environment = new StartupEnvironment("", true, false, false);
        var settings = new RuntimeSettings();

        var result = _service.IsEnabled(environment, settings);

        Assert.False(result);
    }

    [Fact]
    public void IsEnabled_SettingsDisabled_ReturnsFalse()
    {
        var environment = new StartupEnvironment("", false, false, false);
        var settings = new RuntimeSettings { AutoMemoryEnabled = false };

        var result = _service.IsEnabled(environment, settings);

        Assert.False(result);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    public void IsEnabled_EnvironmentDisabled_ReturnsFalse(string envVal)
    {
        Environment.SetEnvironmentVariable("CLAUDE_CODE_DISABLE_AUTO_MEMORY", envVal);
        try
        {
            var environment = new StartupEnvironment("", false, false, false);
            var settings = new RuntimeSettings();

            var result = _service.IsEnabled(environment, settings);

            Assert.False(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_DISABLE_AUTO_MEMORY", null);
        }
    }

    [Fact]
    public async Task LoadMemoryPromptAsync_LoadsFileContent()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(projectDir);
        var memoryFile = Path.Combine(projectDir, "MEMORY.md");
        var content = "This is memory.";
        await File.WriteAllTextAsync(memoryFile, content);

        try
        {
            _pathResolverMock.Setup(r => r.GetMemoryDir(projectDir))
                .Returns(projectDir);
            _pathResolverMock.Setup(r => r.GetMemoryEntrypoint(projectDir))
                .Returns(memoryFile);

            var result = await _service.LoadMemoryPromptAsync(projectDir);

            Assert.Contains(content, result);
        }
        finally
        {
            Directory.Delete(projectDir, true);
        }
    }

    [Fact]
    public async Task LoadMemoryPromptAsync_NoFile_ReturnsEmpty()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var memoryFile = Path.Combine(projectDir, "MEMORY.md");
        _pathResolverMock.Setup(r => r.GetMemoryDir(projectDir))
            .Returns(projectDir);
        _pathResolverMock.Setup(r => r.GetMemoryEntrypoint(projectDir))
            .Returns(memoryFile);

        var result = await _service.LoadMemoryPromptAsync(projectDir);

        Assert.Contains("is currently empty", result);
    }
}

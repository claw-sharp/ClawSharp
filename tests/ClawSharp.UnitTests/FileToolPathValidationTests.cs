// TS origin: ./utils/permissions/pathValidation.ts, ./utils/path.ts
using System.Reflection;

namespace ClawSharp.UnitTests;

public sealed class FileToolPathValidationTests
{
    [Fact]
    public void Validate_Converts_Msys_Windows_Paths_Before_Resolving_File_Tool_Input()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = InvokeValidate("/c/repo/example.txt", @"D:\workspace", "Read");

        Assert.True((bool)result.GetType().GetProperty("Allowed")!.GetValue(result)!);
        Assert.Equal(@"C:\repo\example.txt", result.GetType().GetProperty("ResolvedPath")!.GetValue(result));
    }

    private static object InvokeValidate(string inputPath, string workspaceRoot, string operationType)
    {
        var toolsAssembly = typeof(ClawSharp.Tools.ToolRegistry).Assembly;
        var validationType = toolsAssembly.GetType("ClawSharp.Tools.FileToolPathValidation", throwOnError: true)!;
        var operationTypeEnum = toolsAssembly.GetType("ClawSharp.Tools.FileToolOperationType", throwOnError: true)!;
        var validateMethod = validationType.GetMethod(
            "Validate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        var operationValue = Enum.Parse(operationTypeEnum, operationType);
        return validateMethod.Invoke(null, [inputPath, workspaceRoot, operationValue])!;
    }
}

namespace ClawSharp.Core;

public interface IMemoryPathResolver
{
    string GetMemoryDir(string workspaceRoot);
    string GetMemoryEntrypoint(string workspaceRoot);
}

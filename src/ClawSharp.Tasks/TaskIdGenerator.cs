// TS origin: ./Task.ts
using System.Security.Cryptography;

namespace ClawSharp.Tasks;

internal static class TaskIdGenerator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string Generate(TaskType type)
    {
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);

        var chars = new char[9];
        chars[0] = GetPrefix(type);
        for (var index = 0; index < buffer.Length; index++)
        {
            chars[index + 1] = Alphabet[buffer[index] % Alphabet.Length];
        }

        return new string(chars);
    }

    public static string GenerateMainSession()
    {
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);

        var chars = new char[9];
        chars[0] = 's';
        for (var index = 0; index < buffer.Length; index++)
        {
            chars[index + 1] = Alphabet[buffer[index] % Alphabet.Length];
        }

        return new string(chars);
    }

    private static char GetPrefix(TaskType type)
    {
        return type switch
        {
            TaskType.LocalBash => 'b',
            TaskType.LocalAgent => 'a',
            TaskType.RemoteAgent => 'r',
            TaskType.InProcessTeammate => 't',
            TaskType.LocalWorkflow => 'w',
            TaskType.MonitorMcp => 'm',
            TaskType.Dream => 'd',
            _ => 'x'
        };
    }
}

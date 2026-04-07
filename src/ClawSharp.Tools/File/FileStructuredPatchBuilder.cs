using System.Text.Json.Nodes;
using DiffPlex;
using DiffPlex.Model;

namespace ClawSharp.Tools;

internal static class FileStructuredPatchBuilder
{
    private const int ContextLines = 3;

    public static JsonArray Build(string oldContent, string newContent)
    {
        var displayOldContent = ConvertLeadingTabsToSpaces(oldContent);
        var displayNewContent = ConvertLeadingTabsToSpaces(newContent);
        var diffResult = Differ.Instance.CreateLineDiffs(displayOldContent, displayNewContent, false);
        if (diffResult.DiffBlocks.Count == 0)
        {
            return [];
        }

        var oldLines = SplitLines(displayOldContent);
        var newLines = SplitLines(displayNewContent);
        var patch = new JsonArray();

        foreach (var group in GroupDiffBlocks(diffResult.DiffBlocks))
        {
            patch.Add(BuildHunk(group, oldLines, newLines));
        }

        return patch;
    }

    private static JsonObject BuildHunk(
        IReadOnlyList<DiffBlock> group,
        IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines)
    {
        var first = group[0];
        var last = group[^1];

        var oldStartIndex = Math.Max(0, first.DeleteStartA - ContextLines);
        var newStartIndex = Math.Max(0, first.InsertStartB - ContextLines);
        var oldEndIndex = Math.Min(oldLines.Count, GetChangeEndOld(last) + ContextLines);
        var newEndIndex = Math.Min(newLines.Count, GetChangeEndNew(last) + ContextLines);

        var lines = new JsonArray();
        var oldIndex = oldStartIndex;
        var newIndex = newStartIndex;

        foreach (var block in group)
        {
            for (; oldIndex < block.DeleteStartA && newIndex < block.InsertStartB; oldIndex++, newIndex++)
            {
                lines.Add($" {oldLines[oldIndex]}");
            }

            for (var index = 0; index < block.DeleteCountA; index++)
            {
                lines.Add($"-{oldLines[block.DeleteStartA + index]}");
            }

            for (var index = 0; index < block.InsertCountB; index++)
            {
                lines.Add($"+{newLines[block.InsertStartB + index]}");
            }

            oldIndex = block.DeleteStartA + block.DeleteCountA;
            newIndex = block.InsertStartB + block.InsertCountB;
        }

        for (; oldIndex < oldEndIndex && newIndex < newEndIndex; oldIndex++, newIndex++)
        {
            lines.Add($" {oldLines[oldIndex]}");
        }

        return new JsonObject
        {
            ["oldStart"] = oldStartIndex + 1,
            ["oldLines"] = oldEndIndex - oldStartIndex,
            ["newStart"] = newStartIndex + 1,
            ["newLines"] = newEndIndex - newStartIndex,
            ["lines"] = lines
        };
    }

    private static List<IReadOnlyList<DiffBlock>> GroupDiffBlocks(IList<DiffBlock> blocks)
    {
        var groups = new List<IReadOnlyList<DiffBlock>>();
        if (blocks.Count == 0)
        {
            return groups;
        }

        var currentGroup = new List<DiffBlock> { blocks[0] };
        var currentOldEnd = GetChangeEndOld(blocks[0]);
        var currentNewEnd = GetChangeEndNew(blocks[0]);

        for (var index = 1; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var joinsCurrentGroup =
                block.DeleteStartA <= currentOldEnd + (ContextLines * 2) ||
                block.InsertStartB <= currentNewEnd + (ContextLines * 2);
            if (!joinsCurrentGroup)
            {
                groups.Add(currentGroup.ToArray());
                currentGroup = [];
            }

            currentGroup.Add(block);
            currentOldEnd = GetChangeEndOld(block);
            currentNewEnd = GetChangeEndNew(block);
        }

        groups.Add(currentGroup.ToArray());
        return groups;
    }

    private static int GetChangeEndOld(DiffBlock block)
    {
        return block.DeleteStartA + block.DeleteCountA;
    }

    private static int GetChangeEndNew(DiffBlock block)
    {
        return block.InsertStartB + block.InsertCountB;
    }

    private static string ConvertLeadingTabsToSpaces(string content)
    {
        if (!content.Contains('\t', StringComparison.Ordinal))
        {
            return content;
        }

        var lines = SplitLines(content);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var tabCount = 0;
            while (tabCount < line.Length && line[tabCount] == '\t')
            {
                tabCount++;
            }

            if (tabCount == 0)
            {
                continue;
            }

            lines[index] = string.Concat(string.Concat(Enumerable.Repeat("  ", tabCount)), line[tabCount..]);
        }

        return string.Join('\n', lines);
    }

    private static List<string> SplitLines(string content)
    {
        return [.. content.Split('\n', StringSplitOptions.None)];
    }
}

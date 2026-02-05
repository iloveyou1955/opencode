using System.Text.RegularExpressions;

namespace OpenCode.Core.Utilities;

public class PatchParser
{
    public class Hunk
    {
        public string Type { get; set; } = ""; // add, delete, update
        public string Path { get; set; } = "";
        public string? MovePath { get; set; }
        public string Contents { get; set; } = "";
        public List<UpdateChunk> Chunks { get; set; } = new();
    }

    public class UpdateChunk
    {
        public List<string> OldLines { get; set; } = new();
        public List<string> NewLines { get; set; } = new();
        public string? Context { get; set; }
        public bool IsEndOfFile { get; set; }
    }

    public static List<Hunk> Parse(string patchText)
    {
        var lines = patchText.Split('\n');
        var hunks = new List<Hunk>();

        int beginIdx = -1;
        int endIdx = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "*** Begin Patch") beginIdx = i;
            if (lines[i].Trim() == "*** End Patch") endIdx = i;
        }

        if (beginIdx == -1 || endIdx == -1 || beginIdx >= endIdx)
        {
            throw new Exception("无效的补丁格式：缺少 Begin/End 标记");
        }

        int current = beginIdx + 1;
        while (current < endIdx)
        {
            var line = lines[current].Trim();
            if (line.StartsWith("*** Add File:"))
            {
                var hunk = new Hunk { Type = "add", Path = line.Substring(13).Trim() };
                current++;
                var contentLines = new List<string>();
                while (current < endIdx && !lines[current].StartsWith("***"))
                {
                    if (lines[current].StartsWith("+")) contentLines.Add(lines[current].Substring(1));
                    current++;
                }
                hunk.Contents = string.Join("\n", contentLines);
                hunks.Add(hunk);
            }
            else if (line.StartsWith("*** Delete File:"))
            {
                hunks.Add(new Hunk { Type = "delete", Path = line.Substring(16).Trim() });
                current++;
            }
            else if (line.StartsWith("*** Update File:"))
            {
                var hunk = new Hunk { Type = "update", Path = line.Substring(16).Trim() };
                current++;
                if (current < endIdx && lines[current].StartsWith("*** Move to:"))
                {
                    hunk.MovePath = lines[current].Substring(12).Trim();
                    current++;
                }

                while (current < endIdx && !lines[current].StartsWith("***"))
                {
                    if (lines[current].StartsWith("@@"))
                    {
                        var chunk = new UpdateChunk { Context = lines[current].Substring(2).Trim() };
                        current++;
                        while (current < endIdx && !lines[current].StartsWith("@@") && !lines[current].StartsWith("***"))
                        {
                            var cLine = lines[current];
                            if (cLine == "*** End of File")
                            {
                                chunk.IsEndOfFile = true;
                                current++;
                                break;
                            }
                            if (cLine.StartsWith(" "))
                            {
                                chunk.OldLines.Add(cLine.Substring(1));
                                chunk.NewLines.Add(cLine.Substring(1));
                            }
                            else if (cLine.StartsWith("-"))
                            {
                                chunk.OldLines.Add(cLine.Substring(1));
                            }
                            else if (cLine.StartsWith("+"))
                            {
                                chunk.NewLines.Add(cLine.Substring(1));
                            }
                            current++;
                        }
                        hunk.Chunks.Add(chunk);
                    }
                    else
                    {
                        current++;
                    }
                }
                hunks.Add(hunk);
            }
            else
            {
                current++;
            }
        }

        return hunks;
    }
}

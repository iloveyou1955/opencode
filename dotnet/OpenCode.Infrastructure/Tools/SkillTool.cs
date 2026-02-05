using System.Text;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 加载和查看可用技能的工具。
/// </summary>
public class SkillTool : ITool
{
    private readonly SkillService _skillService;

    public SkillTool(SkillService skillService)
    {
        _skillService = skillService;
    }

    public string Name => "skill";
    public string Description => "加载一个技能以获取特定任务的详细指令。技能提供专业知识和逐步指导。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "name": { "type": "string", "description": "要加载的技能名称" }
      },
      "required": ["name"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        string name = args["name"]?.ToString() ?? "";

        if (string.IsNullOrEmpty(name))
        {
            var skills = await _skillService.GetAllSkillsAsync();
            if (skills.Count == 0) return "当前没有可用的技能。";

            var sb = new StringBuilder();
            sb.AppendLine("可用的技能：");
            foreach (var skill in skills)
            {
                sb.AppendLine($"- {skill.Name}: {skill.Description}");
            }
            return sb.ToString();
        }

        var selectedSkill = await _skillService.GetSkillAsync(name);
        if (selectedSkill == null)
        {
            return $"错误: 未找到名为 '{name}' 的技能。";
        }

        if (!await context.RequestPermissionAsync("skill", name))
        {
            return $"错误: 加载技能 {name} 的权限被拒绝";
        }

        var result = new StringBuilder();
        result.AppendLine($"## Skill: {selectedSkill.Name}");
        result.AppendLine($"**Base directory**: {Path.GetDirectoryName(selectedSkill.Location)}");
        result.AppendLine();
        result.AppendLine(selectedSkill.Content.Trim());

        return result.ToString();
    }
}

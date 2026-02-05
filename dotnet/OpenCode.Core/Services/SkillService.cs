using System.Text.Json;
using OpenCode.Core.Models;

namespace OpenCode.Core.Services;

public class SkillService
{
    private readonly string _skillsDirectory;

    public SkillService(string skillsDirectory)
    {
        _skillsDirectory = skillsDirectory;
        if (!Directory.Exists(_skillsDirectory))
        {
            Directory.CreateDirectory(_skillsDirectory);
        }
    }

    public async Task<List<SkillInfo>> GetAllSkillsAsync()
    {
        var skills = new List<SkillInfo>();
        if (!Directory.Exists(_skillsDirectory)) return skills;

        var skillFiles = Directory.GetFiles(_skillsDirectory, "*.md", SearchOption.AllDirectories);
        foreach (var file in skillFiles)
        {
            var content = await File.ReadAllTextAsync(file);
            var name = Path.GetFileNameWithoutExtension(file);
            
            // 简单的元数据解析 (假设第一行是描述，或者使用特定的前缀)
            string description = "加载该技能以获取特定任务的详细指令。";
            var lines = content.Split('\n', 2);
            if (lines.Length > 0 && lines[0].StartsWith("Description:"))
            {
                description = lines[0].Replace("Description:", "").Trim();
                content = lines.Length > 1 ? lines[1] : "";
            }

            skills.Add(new SkillInfo
            {
                Name = name,
                Description = description,
                Content = content,
                Location = file
            });
        }

        return skills;
    }

    public async Task<SkillInfo?> GetSkillAsync(string name)
    {
        var skills = await GetAllSkillsAsync();
        return skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

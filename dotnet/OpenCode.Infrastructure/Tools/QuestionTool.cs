using System.Text;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 提问工具，允许 AI 向用户询问信息。
/// </summary>
public class QuestionTool : ITool
{
    public string Name => "question";
    public string Description => "Ask the user one or more questions to clarify requirements or get missing information.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "questions": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "question": { "type": "string", "description": "The question to ask the user" }
            },
            "required": ["question"]
          },
          "description": "List of questions to ask"
        }
      },
      "required": ["questions"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        var questionsArray = args["questions"] as JsonArray;
        if (questionsArray == null || questionsArray.Count == 0)
        {
            return "Error: No questions provided.";
        }

        var results = new StringBuilder();
        results.AppendLine("User has answered your questions:");

        foreach (var qNode in questionsArray)
        {
            if (qNode is JsonObject qObj)
            {
                string question = qObj["question"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(question)) continue;

                // 在 TUI 中直接询问
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[AI 问题]: {question}");
                Console.ResetColor();
                Console.Write("您的回答: ");

                string? answer = await Task.Run(() => Console.ReadLine(), cancellationToken);
                answer = string.IsNullOrWhiteSpace(answer) ? "Unanswered" : answer.Trim();

                results.AppendLine($"- \"{question}\" = \"{answer}\"");
            }
        }

        results.AppendLine("\nYou can now continue with the user's answers in mind.");
        return results.ToString();
    }
}

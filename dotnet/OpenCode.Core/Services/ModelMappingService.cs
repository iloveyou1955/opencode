using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class ModelMappingService
{
    public string? GetSdkKey(string npm)
    {
        return npm switch
        {
            "@ai-sdk/github-copilot" => "copilot",
            "@ai-sdk/openai" or "@ai-sdk/azure" => "openai",
            "@ai-sdk/amazon-bedrock" => "bedrock",
            "@ai-sdk/anthropic" or "@ai-sdk/google-vertex/anthropic" => "anthropic",
            "@ai-sdk/google-vertex" or "@ai-sdk/google" => "google",
            "@ai-sdk/gateway" => "gateway",
            "@openrouter/ai-sdk-provider" => "openrouter",
            _ => null
        };
    }

    public double? GetTemperature(string modelId)
    {
        var id = modelId.ToLower();
        if (id.Contains("qwen")) return 0.55;
        if (id.Contains("claude")) return null;
        if (id.Contains("gemini")) return 1.0;
        if (id.Contains("glm-4.6") || id.Contains("glm-4.7")) return 1.0;
        if (id.Contains("minimax-m2")) return 1.0;
        if (id.Contains("kimi-k2"))
        {
            if (id.Contains("thinking") || id.Contains("k2.") || id.Contains("k2p")) return 1.0;
            return 0.6;
        }
        return null;
    }

    public double? GetTopP(string modelId)
    {
        var id = modelId.ToLower();
        if (id.Contains("qwen")) return 1.0;
        if (id.Contains("minimax-m2") || id.Contains("kimi-k2.5") || id.Contains("kimi-k2p5") || id.Contains("gemini"))
            return 0.95;
        return null;
    }

    public int? GetTopK(string modelId)
    {
        var id = modelId.ToLower();
        if (id.Contains("minimax-m2")) return id.Contains("m2.1") ? 40 : 20;
        if (id.Contains("gemini")) return 64;
        return null;
    }

    public JsonObject GetOptions(string modelId, string providerId, string npm, string sessionId)
    {
        var result = new JsonObject();
        if (providerId == "openai" || npm == "@ai-sdk/openai" || npm == "@ai-sdk/github-copilot")
        {
            result["store"] = false;
        }

        if (npm == "@openrouter/ai-sdk-provider")
        {
            result["usage"] = new JsonObject { ["include"] = true };
            if (modelId.Contains("gemini-3")) result["reasoning"] = new JsonObject { ["effort"] = "high" };
        }

        if (npm == "@ai-sdk/google" || npm == "@ai-sdk/google-vertex")
        {
            var thinkingConfig = new JsonObject { ["includeThoughts"] = true };
            if (modelId.Contains("gemini-3"))
            {
                thinkingConfig["thinkingLevel"] = "high";
            }
            result["thinkingConfig"] = thinkingConfig;
        }

        if (modelId.Contains("gpt-5") && !modelId.Contains("gpt-5-chat"))
        {
            if (!modelId.Contains("gpt-5-pro")) result["reasoningEffort"] = "medium";
            if (modelId.Contains("gpt-5.") && !modelId.Contains("codex") && !modelId.Contains("-chat") && providerId != "azure")
            {
                result["textVerbosity"] = "low";
            }
        }

        return result;
    }

    public List<ChatMessage> NormalizeMessages(List<ChatMessage> messages, string modelId, string? npm = null)
    {
        var id = modelId.ToLower();
        var result = new List<ChatMessage>();

        foreach (var msg in messages)
        {
            // Anthropic/Claude specific normalization
            if (npm == "@ai-sdk/anthropic" || id.Contains("claude"))
            {
                if (string.IsNullOrEmpty(msg.Text) && (msg.Contents == null || msg.Contents.Count == 0)) continue;
                
                // Normalize tool call IDs for Claude (alphanumeric + underscores)
                if (msg.Role == ChatRole.Assistant || msg.Role == ChatRole.Tool)
                {
                    if (msg.Contents != null)
                    {
                        foreach (var part in msg.Contents)
                        {
                            if (part is FunctionCallContent tc)
                            {
                                // tc.CallId = Regex.Replace(tc.CallId, @"[^a-zA-Z0-9_-]", "_");
                            }
                        }
                    }
                }
            }

            // Mistral specific normalization (Tool IDs must be 9 chars)
            if (id.Contains("mistral"))
            {
                // ... logic to fix tool sequence and IDs
            }

            result.Add(msg);
        }

        return result;
    }
}

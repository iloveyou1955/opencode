using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Core.Attributes;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 将带有 [Tool] 属性的 C# 方法转换为 ITool
/// </summary>
public class NativeTool : ITool
{
    private readonly object _target;
    private readonly MethodInfo _method;

    public string Name { get; }
    public string Description { get; }
    public string InputSchema { get; }

    public NativeTool(object target, MethodInfo method, ToolAttribute attr)
    {
        _target = target;
        _method = method;
        Name = attr.Name;
        Description = attr.Description;
        InputSchema = GenerateSchema(method);
    }

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        var parameters = _method.GetParameters();
        var arguments = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            
            // 处理 CancellationToken
            if (param.ParameterType == typeof(CancellationToken))
            {
                arguments[i] = cancellationToken;
                continue;
            }

            // 处理 IToolContext
            if (param.ParameterType == typeof(IToolContext))
            {
                arguments[i] = context;
                continue;
            }

            if (args.TryGetPropertyValue(param.Name!, out var node))
            {
                // 反序列化参数
                try 
                {
                    // 处理 Nullable
                    var targetType = param.ParameterType;
                    // 使用 System.Text.Json 进行转换
                    arguments[i] = node?.Deserialize(targetType);
                }
                catch (Exception ex)
                {
                    return $"Error converting argument '{param.Name}': {ex.Message}";
                }
            }
            else
            {
                if (param.HasDefaultValue)
                {
                    arguments[i] = param.DefaultValue;
                }
                else
                {
                    return $"Error: Missing required argument '{param.Name}'.";
                }
            }
        }

        try
        {
            var result = _method.Invoke(_target, arguments);

            if (result is Task<string> taskString) return await taskString;
            if (result is ValueTask<string> valueTaskString) return await valueTaskString;
            if (result is Task<object> taskObj) return (await taskObj)?.ToString() ?? "null";
            if (result is Task task)
            {
                await task;
                return "Success";
            }
            
            return result?.ToString() ?? "null";
        }
        catch (TargetInvocationException ex)
        {
            return $"Error executing tool: {ex.InnerException?.Message ?? ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error executing tool: {ex.Message}";
        }
    }

    private string GenerateSchema(MethodInfo method)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in method.GetParameters())
        {
            if (param.ParameterType == typeof(CancellationToken) || param.ParameterType == typeof(IToolContext)) continue;

            var paramSchema = new JsonObject();
            
            // 类型映射 (简化版)
            var type = param.ParameterType;
            if (type == typeof(string)) paramSchema["type"] = "string";
            else if (type == typeof(int) || type == typeof(long)) paramSchema["type"] = "integer";
            else if (type == typeof(bool)) paramSchema["type"] = "boolean";
            else if (type.IsEnum)
            {
                paramSchema["type"] = "string";
                var enumValues = new JsonArray();
                foreach (var name in Enum.GetNames(type)) enumValues.Add(name);
                paramSchema["enum"] = enumValues;
            }
            else paramSchema["type"] = "string"; // 默认 fallback

            // 描述
            var descAttr = param.GetCustomAttribute<DescriptionAttribute>();
            if (descAttr != null)
            {
                paramSchema["description"] = descAttr.Description;
            }

            properties[param.Name!] = paramSchema;

            if (!param.HasDefaultValue && !IsNullable(param.ParameterType))
            {
                required.Add(param.Name!);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };

        return schema.ToJsonString();
    }

    private bool IsNullable(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }
}

/// <summary>
/// 辅助类：扫描对象并创建工具
/// </summary>
public static class ToolFactory
{
    public static IEnumerable<ITool> CreateTools(object target)
    {
        var methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<ToolAttribute>();
            if (attr != null)
            {
                yield return new NativeTool(target, method, attr);
            }
        }
    }
}

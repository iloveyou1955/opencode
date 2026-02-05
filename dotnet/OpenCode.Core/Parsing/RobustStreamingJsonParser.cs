using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCode.Core.Parsing;

/// <summary>
/// 鲁棒性流式 JSON 解析器，用于处理 LLM 输出的破碎或混合格式 JSON
/// </summary>
public class RobustStreamingJsonParser
{
    private readonly StringBuilder _buffer = new();
    private bool _insideCodeBlock = false;
    
    /// <summary>
    /// 解析单个文本片段，尝试提取有效的 JsonNode
    /// </summary>
    public IEnumerable<JsonNode?> ParseChunk(string chunk)
    {
        _buffer.Append(chunk);
        var currentText = _buffer.ToString();
        
        // 1. 处理 Markdown 代码块
        if (!_insideCodeBlock && currentText.Contains("```json"))
        {
            _insideCodeBlock = true;
            var index = currentText.IndexOf("```json");
            _buffer.Remove(0, index + 7);
            currentText = _buffer.ToString();
        }

        if (_insideCodeBlock)
        {
            var endIndex = currentText.IndexOf("```");
            if (endIndex != -1)
            {
                var jsonContent = currentText.Substring(0, endIndex);
                foreach (var node in ExtractCompleteJsonObjectsSync(jsonContent))
                {
                    yield return node;
                }
                
                _buffer.Remove(0, endIndex + 3);
                _insideCodeBlock = false;
            }
        }
        else
        {
            // 2. 尝试提取完整的 JSON 对象
            foreach (var node in ExtractCompleteJsonObjectsSync(currentText))
            {
                yield return node;
            }
        }
    }

    private IEnumerable<JsonNode?> ExtractCompleteJsonObjectsSync(string text)
    {
        var textToProcess = text;
        
        while (!string.IsNullOrWhiteSpace(textToProcess))
        {
            var startIndex = FindJsonStart(textToProcess);
            if (startIndex == -1) break;
            
            var remainingText = textToProcess.Substring(startIndex);
            var jsonObject = FindCompleteJsonObject(remainingText);
            
            if (jsonObject != null)
            {
                yield return jsonObject;
                
                var jsonString = jsonObject.ToJsonString();
                var processedLength = startIndex + jsonString.Length;
                textToProcess = textToProcess.Substring(processedLength);
            }
            else
            {
                textToProcess = textToProcess.Substring(startIndex);
                break;
            }
        }
        
        if (textToProcess != text)
        {
            _buffer.Clear();
            if (!string.IsNullOrEmpty(textToProcess))
            {
                _buffer.Append(textToProcess);
            }
        }
    }

    /// <summary>
    /// 解析流式文本，尝试提取有效的 JsonNode
    /// </summary>
    /// <param name="tokenStream">文本流</param>
    /// <returns>JsonNode 异步流</returns>
    public async IAsyncEnumerable<JsonNode?> ParseStreamAsync(IAsyncEnumerable<string> tokenStream)
    {
        await foreach (var token in tokenStream)
        {
            _buffer.Append(token);
            
            // 1. 处理 Markdown 代码块，提取 JSON 内容
            var currentText = _buffer.ToString();
            
            if (!_insideCodeBlock && currentText.Contains("```json"))
            {
                _insideCodeBlock = true;
                var index = currentText.IndexOf("```json");
                _buffer.Remove(0, index + 7); // 移除 ```json 之前的内容
                currentText = _buffer.ToString();
            }
            
            // 如果在代码块内，查找结束标记
            if (_insideCodeBlock)
            {
                var endIndex = currentText.IndexOf("```");
                if (endIndex != -1)
                {
                    // 提取代码块内的内容
                    var jsonContent = currentText.Substring(0, endIndex);
                    
                    // 尝试解析代码块内的所有完整 JSON
                    await foreach (var node in ExtractCompleteJsonObjects(jsonContent))
                    {
                        if (node != null) yield return node;
                    }
                    
                    // 保留代码块之后的内容继续处理
                    _buffer.Remove(0, endIndex + 3);
                    _insideCodeBlock = false;
                    continue;
                }
            }
            
            // 2. 在非代码块模式下，尝试提取完整的 JSON 对象
            await foreach (var node in ExtractCompleteJsonObjects(currentText))
            {
                if (node != null) yield return node;
            }
        }
        
        // 3. 流结束后，处理缓冲区中剩余的内容
        var remainingText = _buffer.ToString();
        if (!string.IsNullOrWhiteSpace(remainingText))
        {
            await foreach (var node in ExtractCompleteJsonObjects(remainingText))
            {
                if (node != null) yield return node;
            }
        }
    }
    
    /// <summary>
    /// 从文本中提取所有完整的 JSON 对象
    /// </summary>
    private async IAsyncEnumerable<JsonNode?> ExtractCompleteJsonObjects(string text)
    {
        var textToProcess = text;
        
        while (!string.IsNullOrWhiteSpace(textToProcess))
        {
            // 查找可能的 JSON 起始位置
            var startIndex = FindJsonStart(textToProcess);
            if (startIndex == -1) break;
            
            // 从起始位置开始，尝试不同长度的子字符串
            var remainingText = textToProcess.Substring(startIndex);
            var jsonObject = FindCompleteJsonObject(remainingText);
            
            if (jsonObject != null)
            {
                yield return jsonObject;
                
                // 跳过已处理的 JSON 对象
                var jsonString = jsonObject.ToJsonString();
                var processedLength = startIndex + jsonString.Length;
                textToProcess = textToProcess.Substring(processedLength);
            }
            else
            {
                // 没有找到完整的 JSON，保留当前位置之后的文本
                textToProcess = textToProcess.Substring(startIndex);
                break;
            }
        }
        
        // 更新缓冲区，保留未处理的部分
        if (textToProcess != text)
        {
            _buffer.Clear();
            if (!string.IsNullOrEmpty(textToProcess))
            {
                _buffer.Append(textToProcess);
            }
        }
    }
    
    /// <summary>
    /// 查找 JSON 对象的起始位置
    /// </summary>
    private int FindJsonStart(string text)
    {
        // 查找第一个 { 或 [
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{' || c == '[') return i;
            if (!char.IsWhiteSpace(c) && c != '\n' && c != '\r') break;
        }
        return -1;
    }
    
    /// <summary>
    /// 查找文本中第一个完整的 JSON 对象
    /// </summary>
    private JsonNode? FindCompleteJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.TrimStart();
        if (string.IsNullOrEmpty(trimmed)) return null;

        char startChar = trimmed[0];
        if (startChar != '{' && startChar != '[') return null;

        // 使用括号计数法查找 JSON 对象的结束位置
        // 这种方法比尝试解析所有子字符串要高效得多 (O(N) vs O(N^2))
        int openBraces = 0;
        bool insideString = false;
        bool escaped = false;
        
        // 计算 trimmed 之前的空白字符长度，以便正确索引 text
        int offset = text.Length - trimmed.Length;

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                insideString = !insideString;
                continue;
            }

            if (!insideString)
            {
                if (c == '{' || c == '[')
                {
                    openBraces++;
                }
                else if (c == '}' || c == ']')
                {
                    openBraces--;

                    if (openBraces == 0)
                    {
                        // 找到潜在的 JSON 对象结束位置
                        // i 是相对于 trimmed 的索引，所以长度是 i + 1
                        string candidate = trimmed.Substring(0, i + 1);
                        try
                        {
                            return JsonNode.Parse(candidate);
                        }
                        catch
                        {
                            // 解析失败，可能是无效的 JSON
                            return null;
                        }
                    }
                }
            }
        }

        return null;
    }
    
    /// <summary>
    /// 尝试修复破碎的 JSON 字符串（保持向后兼容）
    /// </summary>
    public static string FixJson(string brokenJson)
    {
        if (string.IsNullOrWhiteSpace(brokenJson)) return "{}";

        var sb = new StringBuilder(brokenJson);
        var stack = new Stack<char>();
        bool inString = false;
        bool inEscape = false;

        for (int i = 0; i < sb.Length; i++)
        {
            char c = sb[i];

            if (inEscape)
            {
                inEscape = false;
                continue;
            }

            if (c == '\\')
            {
                inEscape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '{') stack.Push('}');
                else if (c == '[') stack.Push(']');
                else if (c == '}' || c == ']')
                {
                    if (stack.Count > 0 && stack.Peek() == c)
                    {
                        stack.Pop();
                    }
                }
            }
        }

        // 补全字符串引号
        if (inString)
        {
            sb.Append('"');
        }

        // 补全缺失的括号
        while (stack.Count > 0)
        {
            sb.Append(stack.Pop());
        }

        return sb.ToString();
    }
}
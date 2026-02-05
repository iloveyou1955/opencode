// dotnet/OpenCode.Core/Services/ICodeMapService.cs
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenCode.Core.Models;

namespace OpenCode.Core.Services
{
    /// <summary>
    /// 定义代码图谱服务的接口。
    /// </summary>
    public interface ICodeMapService
    {
        /// <summary>
        /// 异步获取代码图谱数据。
        /// </summary>
        /// <returns>表示代码图谱数据的字符串。</returns>
        Task<string> GetCodeMapAsync();

        /// <summary>
        /// 异步获取所有代码图谱的列表。
        /// </summary>
        /// <returns>表示代码图谱列表的字符串。</returns>
        Task<List<CodeMapEntry>> GetMapsAsync();

        /// <summary>
        /// 异步生成代码图谱。
        /// </summary>
        /// <param name="input">生成图谱所需的输入。</param>
        /// <returns>表示生成结果的字符串。</returns>
        Task<string> GenerateMapAsync(string input);
    }
}

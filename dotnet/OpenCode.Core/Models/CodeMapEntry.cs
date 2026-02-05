using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCode.Core.Models
{
    /// <summary>
    /// 表示代码地图中的一个条目。
    /// </summary>
    public class CodeMapEntry
    {
        /// <summary>
        /// 获取或设置代码地图条目的名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置代码地图条目的类型。
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置代码地图条目中的节点数量。
        /// </summary>
        public int NodeCount { get; set; }
    }
}
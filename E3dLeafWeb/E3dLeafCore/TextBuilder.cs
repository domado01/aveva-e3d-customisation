using System;
using System.Collections.Generic;
using System.Text;

namespace E3dLeafCore
{
    /// <summary>추출 결과를 텍스트(탭 구분)로 직렬화. 콘솔/Addin 공통.</summary>
    public static class TextBuilder
    {
        public static string Build(string mode, string project, string mdb, string start, List<LeafRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AVEVA Leaf Export (Web)");
            sb.AppendLine("# Generated : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("# Mode      : " + mode);
            sb.AppendLine("# Project   : " + project);
            sb.AppendLine("# MDB       : " + mdb);
            sb.AppendLine("# Start     : " + (string.IsNullOrEmpty(start) ? "(all)" : start));
            sb.AppendLine("# Count     : " + rows.Count);
            sb.AppendLine("#");
            sb.AppendLine("Type\tName\tReference");
            foreach (var r in rows) sb.AppendLine(r.Type + "\t" + r.Name + "\t" + r.Reference);
            return sb.ToString();
        }
    }
}

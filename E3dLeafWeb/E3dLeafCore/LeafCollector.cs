using System.Collections.Generic;
using Aveva.Pdms.Database;

namespace E3dLeafCore
{
    /// <summary>DbElement 트리를 재귀 순회하여 멤버가 없는 leaf 요소를 수집.</summary>
    public static class LeafCollector
    {
        public static void Collect(DbElement element, List<LeafRow> sink)
        {
            if (element == null || !element.IsValid) return;

            DbElement[] members = element.Members();
            if (members == null || members.Length == 0)
            {
                sink.Add(new LeafRow
                {
                    Type = SafeType(element),
                    Name = SafeName(element),
                    Reference = SafeRef(element)
                });
                return;
            }
            foreach (DbElement child in members) Collect(child, sink);
        }

        private static string SafeName(DbElement el)
        {
            try { string n = el.GetString(DbAttributeInstance.NAME); return string.IsNullOrEmpty(n) ? "" : n; }
            catch { return ""; }
        }
        private static string SafeRef(DbElement el)
        {
            try { return el.ToString(); } catch { return ""; }   // ToString() = 참조값(=12345/678)
        }
        private static string SafeType(DbElement el)
        {
            try { DbElementType t = el.GetElementType(); return t != null ? t.Name : ""; }
            catch { return ""; }
        }
    }
}

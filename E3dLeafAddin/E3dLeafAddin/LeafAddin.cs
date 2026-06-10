using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Aveva.ApplicationFramework;
using Aveva.Pdms.Database;

namespace E3dLeafAddin
{
    /// <summary>
    /// AM 내부에서 도는 애드인. 살아있는 세션 API 로 직접:
    ///   session : 현재 프로젝트/USER/MDB/현재요소(CE) 읽기
    ///   extract : 시작요소(또는 CE/전체) 하위 leaf 추출 → 표 + txt
    ///   add     : ADD CE / ADD &lt;ref&gt; 실행 (3D 뷰에 추가)
    /// Streamlit 과는 파일 브리지로 통신(요청 leaf_req.txt → 응답 leaf_resp.json).
    /// 타이머는 AM UI 스레드에서 돌아 DB 접근이 안전하다.
    /// </summary>
    public class LeafAddin : IAddin
    {
        private const string Dir = @"C:\Users\Public\Documents";
        private static readonly string ReqFile = Path.Combine(Dir, "leaf_req.txt");
        private static readonly string RespFile = Path.Combine(Dir, "leaf_resp.json");
        private static readonly string StatusFile = Path.Combine(Dir, "leaf_addin_status.txt");

        private Timer _timer;
        private string _lastId = "";

        public string Name { get { return "E3D Leaf Addin"; } }
        public string Description { get { return "Streamlit 연동 (세션/추출/ADD) via API"; } }

        public void Start(ServiceManager serviceManager)
        {
            _timer = new Timer();
            _timer.Interval = 300;
            _timer.Tick += OnTick;
            _timer.Start();
            SafeWrite(StatusFile, "E3dLeafAddin started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        public void Stop()
        {
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(ReqFile)) return;
                string body = SafeRead(ReqFile);
                try { File.Delete(ReqFile); } catch { }
                if (string.IsNullOrEmpty(body)) return;

                Dictionary<string, string> req = ParseKv(body);
                string id = Val(req, "id");
                if (id != "" && id == _lastId) return;
                _lastId = id;

                string cmd = Val(req, "cmd");
                string resp;
                if (cmd == "session") resp = DoSession(id);
                else if (cmd == "extract") resp = DoExtract(id, Val(req, "start"), Val(req, "output"));
                else if (cmd == "add") resp = DoAdd(id, Val(req, "arg"));
                else resp = "{\"id\":" + J(id) + ",\"ok\":false,\"error\":\"unknown cmd\"}";

                SafeWrite(RespFile, resp);
            }
            catch (Exception ex)
            {
                SafeWrite(RespFile, "{\"ok\":false,\"error\":" + J(ex.GetType().Name + ": " + ex.Message) + "}");
            }
        }

        // ---- session: 현재 세션 정보 ----
        private string DoSession(string id)
        {
            string ceRef = "", ceName = "";
            DbElement ce = GetCurrent();
            if (ce != null && ce.IsValid)
            {
                ceRef = SafeRef(ce);
                ceName = SafeName(ce);
            }
            string project = ReflectStr(new[] { "CurrentProject", "Project" }, new[] { "Name", "Code", "Number" });
            string mdb = ReflectStr(new[] { "CurrentMdb", "CurrentMDB", "Mdb" }, new[] { "Name" });
            string user = ReflectStr(new[] { "CurrentUser", "User" }, new[] { "Name" });

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"id\":").Append(J(id)).Append(",\"ok\":true");
            sb.Append(",\"project\":").Append(J(project));
            sb.Append(",\"user\":").Append(J(user));
            sb.Append(",\"mdb\":").Append(J(mdb));
            sb.Append(",\"ce\":").Append(J(ceRef));
            sb.Append(",\"cename\":").Append(J(ceName));
            sb.Append("}");
            return sb.ToString();
        }

        // ---- extract: leaf 추출 ----
        private string DoExtract(string id, string start, string output)
        {
            List<DbElement> roots = new List<DbElement>();
            if (IsAll(start))
            {
                DbElement ce = GetCurrent();
                if (ce != null && ce.IsValid) roots.Add(ce);
            }
            else
            {
                DbElement s = null;
                try { s = DbElement.GetElement(start); } catch { }
                if (s != null && s.IsValid) roots.Add(s);
            }
            if (roots.Count == 0)
                return "{\"id\":" + J(id) + ",\"ok\":false,\"error\":\"시작요소 없음(또는 CE 없음). AM 에서 요소 선택 후 다시.\"}";

            List<string[]> rows = new List<string[]>();
            foreach (DbElement r in roots) Collect(r, rows);
            if (string.IsNullOrEmpty(output))
                output = Path.Combine(Dir, "E3D_Leaf_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            try { WriteOut(output, rows); } catch { }

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"id\":").Append(J(id)).Append(",\"ok\":true,\"count\":").Append(rows.Count);
            sb.Append(",\"file\":").Append(J(output)).Append(",\"rows\":[");
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"type\":").Append(J(rows[i][0])).Append(",\"name\":").Append(J(rows[i][1])).Append(",\"reference\":").Append(J(rows[i][2])).Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ---- add: ADD CE / ADD <ref> ----
        private string DoAdd(string id, string arg)
        {
            string command = string.IsNullOrEmpty(arg) ? "ADD CE" : ("ADD " + arg);
            string err;
            bool ok = RunCommand(command, out err);
            if (ok) return "{\"id\":" + J(id) + ",\"ok\":true,\"sent\":" + J(command) + "}";
            return "{\"id\":" + J(id) + ",\"ok\":false,\"error\":" + J("명령 실행 실패: " + err + " (command=" + command + ")") + "}";
        }

        // ---- leaf 로직 ----
        private static void Collect(DbElement el, List<string[]> sink)
        {
            if (el == null || !el.IsValid) return;
            DbElement[] mem = null;
            try { mem = el.Members(); } catch { }
            if (mem == null || mem.Length == 0) { sink.Add(new[] { SafeType(el), SafeName(el), SafeRef(el) }); return; }
            foreach (DbElement c in mem) Collect(c, sink);
        }
        private static string SafeName(DbElement el) { try { string n = el.GetString(DbAttributeInstance.NAME); return n ?? ""; } catch { return ""; } }
        private static string SafeRef(DbElement el) { try { return el.ToString(); } catch { return ""; } }
        private static string SafeType(DbElement el) { try { DbElementType t = el.GetElementType(); return t != null ? t.Name : ""; } catch { return ""; } }
        private static bool IsAll(string s) { if (string.IsNullOrEmpty(s)) return true; s = s.Trim(); return s == "전체" || s == "*" || s == "CE" || s.Equals("ALL", StringComparison.OrdinalIgnoreCase); }

        private static void WriteOut(string path, List<string[]> rows)
        {
            using (StreamWriter sw = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                sw.WriteLine("# AVEVA Marine Leaf Export (addin)");
                sw.WriteLine("# Generated : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sw.WriteLine("# Count     : " + rows.Count);
                sw.WriteLine("Type\tName\tReference");
                foreach (string[] r in rows) sw.WriteLine(r[0] + "\t" + r[1] + "\t" + r[2]);
            }
        }

        // ---- 현재 요소 (여러 API 후보를 리플렉션으로) ----
        private static DbElement GetCurrent()
        {
            // 1) 직접 시도: CurrentElement.Element
            try
            {
                Assembly asm = typeof(DbElement).Assembly;
                Type t = asm.GetType("Aveva.Pdms.Database.CurrentElement");
                if (t != null)
                {
                    foreach (string m in new[] { "Element", "CurrentElement", "Current" })
                    {
                        PropertyInfo p = t.GetProperty(m, BindingFlags.Public | BindingFlags.Static);
                        if (p != null) { object v = p.GetValue(null, null); if (v is DbElement) return (DbElement)v; }
                        MethodInfo mi = t.GetMethod(m, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                        if (mi != null) { object v = mi.Invoke(null, null); if (v is DbElement) return (DbElement)v; }
                    }
                }
            }
            catch { }
            return null;
        }

        // 세션 정보용: 어셈블리에서 타입 → 정적값 → .Name 같은 멤버 추출
        private static string ReflectStr(string[] staticHolders, string[] valueMembers)
        {
            try
            {
                Assembly asm = typeof(DbElement).Assembly;
                foreach (Type t in asm.GetTypes())
                {
                    foreach (string holder in staticHolders)
                    {
                        object obj = null;
                        PropertyInfo p = t.GetProperty(holder, BindingFlags.Public | BindingFlags.Static);
                        if (p != null) { try { obj = p.GetValue(null, null); } catch { } }
                        if (obj == null)
                        {
                            MethodInfo mi = t.GetMethod(holder, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                            if (mi != null) { try { obj = mi.Invoke(null, null); } catch { } }
                        }
                        if (obj == null) continue;
                        if (obj is string) return (string)obj;
                        foreach (string vm in valueMembers)
                        {
                            PropertyInfo vp = obj.GetType().GetProperty(vm);
                            if (vp != null) { object vv = vp.GetValue(obj, null); if (vv != null) return vv.ToString(); }
                        }
                        return obj.ToString();
                    }
                }
            }
            catch { }
            return "";
        }

        // 명령 실행: AVEVA 의 command runner 를 리플렉션으로 탐색 (버전마다 다름)
        private static bool RunCommand(string command, out string err)
        {
            err = "";
            string[][] cands = new[]
            {
                new[] { "Aveva.ApplicationFramework.CommandManager", "Execute" },
                new[] { "Aveva.ApplicationFramework.CommandManager", "RunCommand" },
                new[] { "Aveva.Pdms.Shared.Command", "Run" },
                new[] { "Aveva.Pdms.Utilities.CommandLine.Command", "Run" },
                new[] { "Aveva.ApplicationFramework.PMLNet.PMLProxy", "RunCommand" },
            };
            List<string> tried = new List<string>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string[] c in cands)
                {
                    try
                    {
                        Type t = asm.GetType(c[0]);
                        if (t == null) continue;
                        MethodInfo mi = t.GetMethod(c[1], BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                        if (mi != null) { mi.Invoke(null, new object[] { command }); return true; }
                        // 인스턴스: Current/Instance 싱글톤
                        foreach (string sng in new[] { "Current", "Instance" })
                        {
                            PropertyInfo sp = t.GetProperty(sng, BindingFlags.Public | BindingFlags.Static);
                            object inst = (sp != null) ? sp.GetValue(null, null) : null;
                            if (inst == null) continue;
                            MethodInfo im = t.GetMethod(c[1], BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                            if (im != null) { im.Invoke(inst, new object[] { command }); return true; }
                        }
                        tried.Add(c[0] + "." + c[1]);
                    }
                    catch (Exception ex) { tried.Add(c[0] + "." + c[1] + "(" + ex.Message + ")"); }
                }
            }
            err = "command runner 못 찾음. 시도: " + string.Join(", ", tried.ToArray());
            return false;
        }

        // ---- 유틸 ----
        private static Dictionary<string, string> ParseKv(string body)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in body.Replace("\r", "").Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }
        private static string Val(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : ""; }
        private static string SafeRead(string p) { try { return File.ReadAllText(p); } catch { return ""; } }
        private static void SafeWrite(string p, string s) { try { File.WriteAllText(p, s, new UTF8Encoding(false)); } catch { } }

        private static string J(string s)
        {
            if (s == null) return "\"\"";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }
    }
}

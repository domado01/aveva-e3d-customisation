using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Aveva.Pdms.Database;
using Aveva.Pdms.Standalone;

namespace E3dLeafCli
{
    /// <summary>
    /// AVEVA Marine leaf 추출 CLI (Streamlit 등에서 호출).
    /// 결과는 --result 로 지정한 JSON 파일에 쓴다. (PDMS 가 stdout 에 배너를 찍으므로 stdout 은 신뢰 못 함)
    ///   detect-env --result out.json
    ///   extract --project X --user Y --password Z --mdb M --module 78 --start /SITE.. [--output txt] --result out.json
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string resultPath = null;
            try
            {
                Dictionary<string, string> a = ParseArgs(args);
                resultPath = Get(a, "result");
                if (resultPath == "") resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "leaf-result.json");
                string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";

                if (mode == "detect-env") return DetectEnv(resultPath);
                if (mode == "extract") return Extract(a, resultPath);
                Write(resultPath, "{\"ok\":false,\"error\":\"unknown mode. use detect-env | extract\"}");
                return 1;
            }
            catch (Exception ex)
            {
                if (resultPath != null) Write(resultPath, "{\"ok\":false,\"error\":" + J(ex.Message) + "}");
                return 10;
            }
        }

        private static int DetectEnv(string resultPath)
        {
            string proc;
            Dictionary<string, string> env = ProcessEnv.FindAvevaEnv(out proc);
            List<string> codes = ProcessEnv.ProjectCodes(env);

            // USER / MDB 후보를 환경에서 추출 (AM 이 설정한 변수명이 다양하므로 여러 후보 + 부분일치)
            string user = FirstEnv(env, new[] { "PDMSUSER", "AVEVA_USER", "LOGIN_USER", "CURRENT_USER", "USER", "USERNAME" });
            string mdb = FirstEnv(env, new[] { "MDB", "CURRENTMDB", "CURRENT_MDB", "PDMSMDB", "MDBNAME" });
            if (mdb == "") mdb = FirstEnvContains(env, "MDB");
            string curProj = FirstEnv(env, new[] { "PROJ", "PROJECT", "CURRENTPROJECT", "CURRENT_PROJECT", "PDMSPROJ" });

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(env.Count > 0 ? "true" : "false");
            sb.Append(",\"proc\":").Append(J(proc));
            sb.Append(",\"projectsDir\":").Append(J(env.ContainsKey("projects_dir") ? env["projects_dir"] : ""));
            sb.Append(",\"project\":").Append(J(curProj));
            sb.Append(",\"user\":").Append(J(user));
            sb.Append(",\"mdb\":").Append(J(mdb));
            sb.Append(",\"envCount\":").Append(env.Count);
            sb.Append(",\"projects\":[");
            for (int i = 0; i < codes.Count; i++) { if (i > 0) sb.Append(","); sb.Append(J(codes[i])); }
            sb.Append("],\"env\":{");
            bool first = true;
            foreach (KeyValuePair<string, string> kv in env)
            { if (!first) sb.Append(","); first = false; sb.Append(J(kv.Key)).Append(":").Append(J(kv.Value)); }
            sb.Append("}}");
            Write(resultPath, sb.ToString());
            return 0;
        }

        private static string FirstEnv(Dictionary<string, string> env, string[] keys)
        {
            foreach (string k in keys) { string v; if (env.TryGetValue(k, out v) && v != "") return v; }
            return "";
        }
        private static string FirstEnvContains(Dictionary<string, string> env, string part)
        {
            foreach (KeyValuePair<string, string> kv in env)
                if (kv.Key.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0 && kv.Value != "" && kv.Value.Length < 40) return kv.Value;
            return "";
        }

        private static int Extract(Dictionary<string, string> a, string resultPath)
        {
            string project = Get(a, "project"), user = Get(a, "user"), pass = Get(a, "password"),
                   mdb = Get(a, "mdb"), start = Get(a, "start"), output = Get(a, "output");
            int module; if (!int.TryParse(Get(a, "module"), out module)) module = 78;
            if (project == "" || user == "" || mdb == "")
            { Write(resultPath, "{\"ok\":false,\"error\":\"project/user/mdb 필요\"}"); return 1; }

            string proc;
            Dictionary<string, string> denv = ProcessEnv.FindAvevaEnv(out proc);
            Hashtable env = new Hashtable();
            foreach (KeyValuePair<string, string> kv in denv) { env[kv.Key] = kv.Value; Environment.SetEnvironmentVariable(kv.Key, kv.Value); }
            SetupPdms(env);

            bool started = false, opened = false;
            try
            {
                PdmsStandalone.Start(module, env); started = true;
                if (!PdmsStandalone.Open(project, user, pass, mdb))
                { Write(resultPath, "{\"ok\":false,\"error\":\"로그인 실패(Open=false). 자격증명/환경 확인\"}"); return 2; }
                opened = true;

                bool all = IsAll(start);
                List<DbElement> roots = new List<DbElement>();
                if (all)
                {
                    List<string> diag = new List<string>();
                    roots = AllWorlds(diag);
                    if (roots.Count == 0)
                    {
                        string d = string.Join(" || ", diag.ToArray());
                        Write(resultPath, "{\"ok\":false,\"error\":" + J("전체(World) 자동탐색 실패. 시작 요소를 직접 입력하세요. [진단] " + d) + "}");
                        return 4;
                    }
                }
                else
                {
                    DbElement s = DbElement.GetElement(start);
                    if (s == null || !s.IsValid)
                    { Write(resultPath, "{\"ok\":false,\"error\":" + J("시작 요소 없음: " + start) + "}"); return 3; }
                    roots.Add(s);
                }

                List<string[]> rows = new List<string[]>();
                foreach (DbElement r in roots) Collect(r, rows);
                if (output == "") output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "E3D_Leaf_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                WriteOut(output, project, mdb, all ? "(전체/All)" : start, rows);

                StringBuilder sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"count\":").Append(rows.Count).Append(",\"file\":").Append(J(output)).Append(",\"rows\":[");
                for (int i = 0; i < rows.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("{\"type\":").Append(J(rows[i][0])).Append(",\"name\":").Append(J(rows[i][1])).Append(",\"reference\":").Append(J(rows[i][2])).Append("}");
                }
                sb.Append("]}");
                Write(resultPath, sb.ToString());
                return 0;
            }
            finally
            {
                try { if (opened && Project.CurrentProject != null) Project.CurrentProject.Close(); } catch { }
                try { if (started) PdmsStandalone.Finish(); } catch { }
            }
        }

        // ---- 전체(World) 자동탐색 ----
        // start 가 빈칸/전체/ALL/* 이면 현재 MDB 의 모든 DB World 를 대상으로 한다.
        private static bool IsAll(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            s = s.Trim();
            return s == "전체" || s == "*" || s == "/*" || s.Equals("ALL", StringComparison.OrdinalIgnoreCase);
        }

        // AVEVA 버전마다 MDB/World API 명이 달라서 리플렉션으로 여러 후보를 시도.
        // 실패 시 diag 에 후보 타입/멤버를 담아 다음 수정에 활용.
        private static List<DbElement> AllWorlds(List<string> diag)
        {
            List<DbElement> worlds = new List<DbElement>();
            Assembly asm = typeof(DbElement).Assembly;

            // 시도1: MDB.Current -> Databases -> World
            object mdb = StaticGet(asm,
                new[] { "Aveva.Pdms.Database.Mdb", "Aveva.Pdms.Database.MDB", "Aveva.Pdms.Database.DbMdb" },
                new[] { "CurrentMdb", "CurrentMDB", "Current", "GetCurrentMdb" });
            if (mdb != null)
                foreach (object db in AsEnum(InstGet(mdb, new[] { "Databases", "Dbs", "DbsInMdb", "Members", "CurrentDbs" })))
                    AddWorld(InstGet(db, new[] { "World", "WorldElement", "GetWorld", "TopElement", "Element" }), worlds);

            // 시도2: DbDatabase 정적 컬렉션 -> World
            if (worlds.Count == 0)
                foreach (object db in AsEnum(StaticGet(asm,
                    new[] { "Aveva.Pdms.Database.DbDatabase", "Aveva.Pdms.Database.Database" },
                    new[] { "CurrentDbs", "Databases", "AllDbs", "Current" })))
                    AddWorld(InstGet(db, new[] { "World", "WorldElement", "GetWorld", "TopElement", "Element" }), worlds);

            // 실패 진단: Mdb/World/Database 관련 타입+무인자 멤버 덤프
            if (worlds.Count == 0)
            {
                try
                {
                    foreach (Type t in asm.GetTypes())
                    {
                        string n = t.Name;
                        if (n.IndexOf("Mdb", StringComparison.OrdinalIgnoreCase) < 0 &&
                            n.IndexOf("World", StringComparison.OrdinalIgnoreCase) < 0 &&
                            n != "DbDatabase") continue;
                        List<string> mem = new List<string>();
                        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)) mem.Add(p.Name);
                        foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                            if (!m.IsSpecialName && m.GetParameters().Length == 0) mem.Add(m.Name + "()");
                        diag.Add(t.FullName + ": " + string.Join(",", mem.GetRange(0, Math.Min(mem.Count, 14)).ToArray()));
                    }
                }
                catch (Exception ex) { diag.Add("dump err:" + ex.Message); }
            }
            return worlds;
        }

        private static object StaticGet(Assembly asm, string[] typeNames, string[] members)
        {
            foreach (string tn in typeNames)
            {
                Type t = asm.GetType(tn); if (t == null) continue;
                foreach (string mn in members)
                {
                    try
                    {
                        PropertyInfo p = t.GetProperty(mn, BindingFlags.Public | BindingFlags.Static);
                        if (p != null) { object v = p.GetValue(null, null); if (v != null) return v; }
                        MethodInfo m = t.GetMethod(mn, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                        if (m != null) { object v = m.Invoke(null, null); if (v != null) return v; }
                    }
                    catch { }
                }
            }
            return null;
        }
        private static object InstGet(object obj, string[] members)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            foreach (string mn in members)
            {
                try
                {
                    PropertyInfo p = t.GetProperty(mn, BindingFlags.Public | BindingFlags.Instance);
                    if (p != null) { object v = p.GetValue(obj, null); if (v != null) return v; }
                    MethodInfo m = t.GetMethod(mn, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (m != null) { object v = m.Invoke(obj, null); if (v != null) return v; }
                }
                catch { }
            }
            return null;
        }
        private static IEnumerable AsEnum(object o)
        {
            if (o == null) return new object[0];
            if (o is string) return new object[0];
            IEnumerable e = o as IEnumerable;
            if (e != null) return e;
            return new object[] { o };
        }
        private static void AddWorld(object w, List<DbElement> worlds)
        {
            if (!(w is DbElement)) return;
            DbElement de = (DbElement)w;
            try { if (de.IsValid) worlds.Add(de); } catch { }
        }

        // ---- 추출 로직 ----
        private static void Collect(DbElement el, List<string[]> sink)
        {
            if (el == null || !el.IsValid) return;
            DbElement[] mem = el.Members();
            if (mem == null || mem.Length == 0) { sink.Add(new[] { SafeType(el), SafeName(el), SafeRef(el) }); return; }
            foreach (DbElement c in mem) Collect(c, sink);
        }
        private static string SafeName(DbElement el) { try { string n = el.GetString(DbAttributeInstance.NAME); return string.IsNullOrEmpty(n) ? "" : n; } catch { return ""; } }
        private static string SafeRef(DbElement el) { try { return el.ToString(); } catch { return ""; } }
        private static string SafeType(DbElement el) { try { DbElementType t = el.GetElementType(); return t != null ? t.Name : ""; } catch { return ""; } }

        private static void WriteOut(string path, string project, string mdb, string start, List<string[]> rows)
        {
            using (StreamWriter sw = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                sw.WriteLine("# AVEVA Marine Leaf Export");
                sw.WriteLine("# Generated : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sw.WriteLine("# Project   : " + project);
                sw.WriteLine("# MDB       : " + mdb);
                sw.WriteLine("# Start     : " + start);
                sw.WriteLine("# Count     : " + rows.Count);
                sw.WriteLine("#");
                sw.WriteLine("Type\tName\tReference");
                foreach (string[] r in rows) sw.WriteLine(r[0] + "\t" + r[1] + "\t" + r[2]);
            }
        }

        private static void SetupPdms(Hashtable env)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string pdmsExe = exeDir;
            try
            {
                if (!File.Exists(Path.Combine(exeDir, "attlib.dat")))
                {
                    string[] hits = Directory.GetFiles(exeDir, "attlib.dat", SearchOption.AllDirectories);
                    if (hits.Length > 0) pdmsExe = Path.GetDirectoryName(hits[0]);
                }
            }
            catch { }
            if (!env.ContainsKey("PDMSEXE")) { Environment.SetEnvironmentVariable("PDMSEXE", pdmsExe); env["PDMSEXE"] = pdmsExe; }
        }

        // ---- 유틸 ----
        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string k = args[i].Substring(2);
                    string v = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : "";
                    d[k] = v;
                }
            }
            return d;
        }
        private static string Get(Dictionary<string, string> a, string k) { string v; return a.TryGetValue(k, out v) ? v : ""; }

        private static void Write(string path, string json)
        {
            try { File.WriteAllText(path, json, new UTF8Encoding(false)); } catch { }
        }

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

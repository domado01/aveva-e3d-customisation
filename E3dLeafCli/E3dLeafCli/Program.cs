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

                if (mode == "list-am") return ListAmMode(resultPath);
                if (mode == "detect-env") return DetectEnv(a, resultPath);
                if (mode == "extract") return Extract(a, resultPath);
                if (mode == "am-exec") return AmExecMode(a, resultPath);
                Write(resultPath, "{\"ok\":false,\"error\":\"unknown mode. use list-am | detect-env | extract | am-exec\"}");
                return 1;
            }
            catch (Exception ex)
            {
                if (resultPath != null) Write(resultPath, "{\"ok\":false,\"error\":" + J(ex.Message) + "}");
                return 10;
            }
        }

        private static int DetectEnv(Dictionary<string, string> a, string resultPath)
        {
            int pid; int.TryParse(Get(a, "pid"), out pid);
            string proc = ""; string cmd = "";
            Dictionary<string, string> env;
            if (pid > 0) { env = ProcessEnv.Read(pid); proc = ProcessEnv.ProcName(pid); cmd = ProcessEnv.ReadCommandLine(pid); }
            else { env = ProcessEnv.FindAvevaEnv(out proc); }
            List<string> codes = ProcessEnv.ProjectCodes(env);

            // USER / MDB 후보 (경로/공백/과길이 값은 제외 — 짧은 식별자만)
            string user = CleanFirstEnv(env, UserKeys);
            string mdb = CleanFirstEnv(env, MdbKeys);
            if (mdb == "") mdb = CleanFirstContains(env, "MDB");
            // 현재 프로젝트: 명령줄/환경 기반 추정 (AAA 같은 템플릿 코드가 앞서지 않도록)
            string curProj = GuessProject(env, cmd, codes);

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(env.Count > 0 ? "true" : "false");
            sb.Append(",\"proc\":").Append(J(proc));
            sb.Append(",\"projectsDir\":").Append(J(env.ContainsKey("projects_dir") ? env["projects_dir"] : ""));
            sb.Append(",\"project\":").Append(J(curProj));
            sb.Append(",\"user\":").Append(J(user));
            sb.Append(",\"mdb\":").Append(J(mdb));
            sb.Append(",\"cmdline\":").Append(J(cmd));
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

        // USER/MDB/PROJECT 는 짧은 식별자여야 한다. 폴더경로(\ / :)·공백·과길이 값은 거른다.
        // (AM 이 USER/MDB 를 환경변수로 깔끔히 노출하지 않아, 경로값이 잘못 잡히던 문제 방지)
        private static bool IsCleanId(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            if (v.Length > 31) return false;
            if (v.IndexOf('\\') >= 0 || v.IndexOf('/') >= 0 || v.IndexOf(':') >= 0 || v.IndexOf(' ') >= 0) return false;
            return true;
        }
        private static string CleanFirstEnv(Dictionary<string, string> env, string[] keys)
        {
            foreach (string k in keys) { string v; if (env.TryGetValue(k, out v) && IsCleanId(v)) return v; }
            return "";
        }
        private static string CleanFirstContains(Dictionary<string, string> env, string part)
        {
            foreach (KeyValuePair<string, string> kv in env)
                if (kv.Key.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0 && IsCleanId(kv.Value)) return kv.Value;
            return "";
        }
        private static readonly string[] UserKeys = { "PDMSUSER", "AVEVA_USER", "AVEVA_DESIGN_USER", "LOGIN_USER", "CURRENT_USER", "PDMS_USER" };
        private static readonly string[] MdbKeys = { "MDB", "CURRENTMDB", "CURRENT_MDB", "PDMSMDB", "MDBNAME" };

        // 현재 프로젝트 코드 추정: ① 명령줄에 등장하는 코드 ② PROJ/PROJECT 환경값이 코드목록에 있으면
        // ③ 템플릿스러운 AAA 회피 후 첫 코드.  (AAA000 같은 샘플 evar 가 앞서는 문제 방지)
        private static string GuessProject(Dictionary<string, string> env, string cmd, List<string> codes)
        {
            if (!string.IsNullOrEmpty(cmd))
                foreach (string c in codes)
                    if (c.Length >= 2 && cmd.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0) return c;
            string h = FirstEnv(env, new[] { "PROJ", "PROJECT", "CURRENTPROJECT", "CURRENT_PROJECT", "PDMSPROJ" });
            if (h != "" && codes.Contains(h)) return h;
            foreach (string c in codes) if (!c.Equals("AAA", StringComparison.OrdinalIgnoreCase)) return c;
            return codes.Count > 0 ? codes[0] : "";
        }

        // 실행 중인 AM 목록 (pid/이름/경로/프로젝트코드/추정프로젝트/USER/MDB)
        private static int ListAmMode(string resultPath)
        {
            List<AmProc> procs = ProcessEnv.ListAm();
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(procs.Count > 0 ? "true" : "false").Append(",\"items\":[");
            for (int i = 0; i < procs.Count; i++)
            {
                AmProc pr = procs[i];
                List<string> codes = ProcessEnv.ProjectCodes(pr.Env);
                string guess = GuessProject(pr.Env, pr.CmdLine, codes);
                string user = CleanFirstEnv(pr.Env, UserKeys);
                string mdb = CleanFirstEnv(pr.Env, MdbKeys);
                if (mdb == "") mdb = CleanFirstContains(pr.Env, "MDB");
                if (i > 0) sb.Append(",");
                sb.Append("{\"pid\":").Append(pr.Pid)
                  .Append(",\"name\":").Append(J(pr.Name))
                  .Append(",\"path\":").Append(J(pr.Path))
                  .Append(",\"project\":").Append(J(guess))
                  .Append(",\"projectsDir\":").Append(J(pr.Env.ContainsKey("projects_dir") ? pr.Env["projects_dir"] : ""))
                  .Append(",\"user\":").Append(J(user))
                  .Append(",\"mdb\":").Append(J(mdb))
                  .Append(",\"cmdline\":").Append(J(pr.CmdLine))
                  .Append(",\"projects\":[");
                for (int k = 0; k < codes.Count; k++) { if (k > 0) sb.Append(","); sb.Append(J(codes[k])); }
                sb.Append("]}");
            }
            sb.Append("]}");
            Write(resultPath, sb.ToString());
            return 0;
        }

        // 선택한 AM 창에 명령을 실제로 전송 (예: ADD CE → 3D 뷰에 추가)
        private static int AmExecMode(Dictionary<string, string> a, string resultPath)
        {
            int pid; int.TryParse(Get(a, "pid"), out pid);
            string cmd = Get(a, "cmd");
            if (pid <= 0 || cmd == "")
            { Write(resultPath, "{\"ok\":false,\"error\":\"pid/cmd 필요\"}"); return 1; }
            string err;
            bool ok = AmExec.Exec(pid, cmd, out err);
            Write(resultPath, ok ? ("{\"ok\":true,\"sent\":" + J(cmd) + "}") : ("{\"ok\":false,\"error\":" + J(err) + "}"));
            return ok ? 0 : 2;
        }

        private static int Extract(Dictionary<string, string> a, string resultPath)
        {
            string project = Get(a, "project"), user = Get(a, "user"), pass = Get(a, "password"),
                   mdb = Get(a, "mdb"), start = Get(a, "start"), output = Get(a, "output");
            int module; if (!int.TryParse(Get(a, "module"), out module)) module = 78;
            if (project == "" || user == "" || mdb == "")
            { Write(resultPath, "{\"ok\":false,\"error\":\"project/user/mdb 필요\"}"); return 1; }

            int pid; int.TryParse(Get(a, "pid"), out pid);
            string proc = "";
            Dictionary<string, string> denv = (pid > 0) ? ProcessEnv.Read(pid) : ProcessEnv.FindAvevaEnv(out proc);
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

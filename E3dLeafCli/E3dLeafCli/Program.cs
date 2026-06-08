using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            if (project == "" || user == "" || mdb == "" || start == "")
            { Write(resultPath, "{\"ok\":false,\"error\":\"project/user/mdb/start 필요\"}"); return 1; }

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

                DbElement s = DbElement.GetElement(start);
                if (s == null || !s.IsValid)
                { Write(resultPath, "{\"ok\":false,\"error\":" + J("시작 요소 없음: " + start) + "}"); return 3; }

                List<string[]> rows = new List<string[]>();
                Collect(s, rows);
                if (output == "") output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "E3D_Leaf_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                WriteOut(output, project, mdb, start, rows);

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

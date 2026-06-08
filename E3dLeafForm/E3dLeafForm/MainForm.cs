using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Aveva.Pdms.Database;
using Aveva.Pdms.Standalone;

namespace E3dLeafForm
{
    /// <summary>
    /// AVEVA Marine(PDMS) 최하위(leaf) 요소 추출 — 값 입력 폼.
    /// [AM 환경 자동 감지] 로 실행중 AM/PDMS 의 환경을 가져오고, 값을 입력해 [추출].
    /// 여러 프로젝트는 PROJECT 드롭다운(프로필)으로 전환.
    /// </summary>
    public class MainForm : Form
    {
        private ComboBox cmbProject;
        private TextBox txtUser, txtPass, txtMdb, txtModule, txtStart, txtOutput, txtLog;
        private Button btnDetect, btnExtract, btnSave, btnOpen;
        private bool _started;
        private string _lastOutput;
        private Dictionary<string, string> _detectedEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string ProfilesPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "leaf-profiles.txt"); } }

        public MainForm()
        {
            Text = "AVEVA Marine - Leaf Export (최하위 요소 추출)";
            ClientSize = new Size(700, 660);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BuildUi();
            LoadProfilesIntoCombo();
            LoadDefaults();
            FormClosing += delegate { try { if (_started) PdmsStandalone.Finish(); } catch { } };
        }

        private void BuildUi()
        {
            int y = 15, lblX = 15, boxX = 160, boxW = 380;

            Controls.Add(new Label { Text = "PROJECT (코드)", Left = lblX, Top = y + 4, Width = 140 });
            cmbProject = new ComboBox { Left = boxX, Top = y, Width = boxW, DropDownStyle = ComboBoxStyle.DropDown };
            cmbProject.SelectedIndexChanged += delegate { LoadProfile(cmbProject.Text); };
            Controls.Add(cmbProject);
            btnDetect = new Button { Text = "AM 환경 자동 감지", Left = boxX + boxW + 10, Top = y - 1, Width = 130, Height = 25 };
            btnDetect.Click += BtnDetect_Click;
            Controls.Add(btnDetect);
            y += 34;

            txtUser   = Row("USER", ref y, lblX, boxX, boxW);
            txtPass   = Row("PASSWORD", ref y, lblX, boxX, boxW); txtPass.UseSystemPasswordChar = true;
            txtMdb    = Row("MDB", ref y, lblX, boxX, boxW);
            txtModule = Row("MODULE_NUMBER", ref y, lblX, boxX, boxW);
            txtStart  = Row("START ELEMENT (SITE/ZONE)", ref y, lblX, boxX, boxW);
            txtOutput = Row("출력 파일 (비우면 자동)", ref y, lblX, boxX, boxW);

            btnExtract = new Button { Text = "추출", Left = boxX, Top = y, Width = 100, Height = 32 };
            btnExtract.Click += BtnExtract_Click;
            btnSave = new Button { Text = "설정 저장", Left = boxX + 110, Top = y, Width = 100, Height = 32 };
            btnSave.Click += BtnSave_Click;
            btnOpen = new Button { Text = "결과 파일 열기", Left = boxX + 220, Top = y, Width = 120, Height = 32, Enabled = false };
            btnOpen.Click += delegate { try { if (_lastOutput != null) System.Diagnostics.Process.Start(_lastOutput); } catch { } };
            Controls.Add(btnExtract); Controls.Add(btnSave); Controls.Add(btnOpen);
            y += 44;

            Controls.Add(new Label { Text = "진행/결과 로그", Left = lblX, Top = y, Width = 200 }); y += 22;
            txtLog = new TextBox
            {
                Left = lblX, Top = y, Width = 670, Height = 250,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F), BackColor = Color.FromArgb(20, 22, 26), ForeColor = Color.Gainsboro
            };
            Controls.Add(txtLog);
        }

        private TextBox Row(string label, ref int y, int lblX, int boxX, int boxW)
        {
            Controls.Add(new Label { Text = label, Left = lblX, Top = y + 4, Width = 140 });
            TextBox tb = new TextBox { Left = boxX, Top = y, Width = boxW };
            Controls.Add(tb); y += 33; return tb;
        }

        private void LoadDefaults()
        {
            if (cmbProject.Text == "") cmbProject.Text = Clean(Cfg("PROJECT"));
            txtUser.Text   = Clean(Cfg("USER"));
            txtPass.Text   = Clean(Cfg("PASSWORD"));
            txtMdb.Text    = Clean(Cfg("MDB"));
            txtModule.Text = Clean(Cfg("MODULE_NUMBER"));
            txtStart.Text  = Clean(Cfg("START_ELEMENT"));
            txtOutput.Text = Cfg("OUTPUT_FILE");
            Log("[AM 환경 자동 감지] 로 환경을 가져온 뒤, 값을 입력하고 [추출] 하세요.");
            Log("여러 프로젝트는 PROJECT 드롭다운으로 전환하고 [설정 저장] 으로 저장하세요.");
        }

        private static string Cfg(string k) { string v = ConfigurationManager.AppSettings[k]; return v ?? ""; }
        private static string Clean(string v) { return (v == "AAA" || v == "AAAA") ? "" : v; }
        private void Log(string m) { txtLog.AppendText(m + Environment.NewLine); txtLog.Update(); }

        // ---- AM 환경 자동 감지 -------------------------------------------------
        private void BtnDetect_Click(object sender, EventArgs e)
        {
            btnDetect.Enabled = false;
            try
            {
                Log("실행 중인 AM/PDMS 환경을 찾는 중...");
                string proc;
                Dictionary<string, string> env = ProcessEnv.FindAvevaEnv(out proc);
                if (env.Count == 0)
                {
                    Log("[감지 실패] 실행 중인 AVEVA 프로세스를 못 찾았거나 환경을 못 읽었습니다.");
                    Log("  - AM 을 실행하고 프로젝트에 로그인한 상태에서 다시 누르세요.");
                    Log("  - 이 폼을 관리자 권한으로 실행해야 다른 프로세스 환경을 읽을 수 있습니다.");
                    return;
                }
                _detectedEnv = env;
                Log("환경 감지 성공 (프로세스: " + proc + "). 항목 " + env.Count + "개.");
                string pd = env.ContainsKey("projects_dir") ? env["projects_dir"] : "(없음)";
                Log("  projects_dir = " + pd);

                List<string> codes = ProcessEnv.ProjectCodes(env);
                if (codes.Count > 0)
                {
                    Log("  프로젝트 코드: " + string.Join(", ", codes.ToArray()));
                    string cur = cmbProject.Text;
                    cmbProject.Items.Clear();
                    foreach (string c in codes) cmbProject.Items.Add(c);
                    // 저장된 프로필도 합치기
                    foreach (string p in LoadProfileNames()) if (!cmbProject.Items.Contains(p)) cmbProject.Items.Add(p);
                    cmbProject.Text = (cur != "" ? cur : codes[0]);
                }
                Log("→ PROJECT/USER/PASSWORD/MDB/START 입력 후 [추출] 하세요. (환경은 자동 적용됨)");
            }
            catch (Exception ex) { Log("[예외] " + ex.Message); }
            finally { btnDetect.Enabled = true; }
        }

        // ---- 추출 -------------------------------------------------------------
        private void BtnExtract_Click(object sender, EventArgs e)
        {
            btnExtract.Enabled = false; btnOpen.Enabled = false;
            try
            {
                string project = cmbProject.Text.Trim();
                string user = txtUser.Text.Trim();
                string pass = txtPass.Text;
                string mdb = txtMdb.Text.Trim();
                string startEl = txtStart.Text.Trim();

                if (project == "" || user == "" || mdb == "") { Log("[오류] PROJECT / USER / MDB 를 입력하세요. (PASSWORD 없으면 비움)"); return; }
                if (startEl == "") { Log("[오류] START ELEMENT 를 입력하세요 (예: SITE/ZONE 이름)."); return; }
                int module; if (!int.TryParse(txtModule.Text.Trim(), out module)) module = 78;

                Hashtable env = new Hashtable();
                foreach (string key in ConfigurationManager.AppSettings.AllKeys) env[key] = ConfigurationManager.AppSettings[key];
                foreach (KeyValuePair<string, string> kv in _detectedEnv) { env[kv.Key] = kv.Value; Environment.SetEnvironmentVariable(kv.Key, kv.Value); }
                SetupPdmsEnvironment(env);

                if (!_started) { Log("PDMS 세션 시작 (module " + module + ") ..."); PdmsStandalone.Start(module, env); _started = true; }

                Log("프로젝트 열기: " + project + " / MDB " + mdb + " (user " + user + ") ...");
                if (!PdmsStandalone.Open(project, user, pass, mdb))
                {
                    Log("[오류] 로그인 실패 (Open=false). PROJECT/USER/PASSWORD/MDB 또는 환경(projects_dir/프로젝트경로) 확인.");
                    string plog = ReadPdmsLog();
                    if (plog != "") { Log("---- PDMS 로그 ----"); Log(plog); }
                    return;
                }
                Log("로그인 성공. 탐색 시작: " + startEl);

                DbElement start = DbElement.GetElement(startEl);
                if (start == null || !start.IsValid) { Log("[오류] 시작 요소를 찾을 수 없습니다: " + startEl); SafeClose(); return; }

                List<string[]> rows = new List<string[]>();
                Collect(start, rows);
                Log("최하위(leaf) 요소 " + rows.Count + " 개 수집 완료.");

                string outPath = txtOutput.Text.Trim();
                if (outPath == "") outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "E3D_Leaf_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                WriteOut(outPath, project, mdb, startEl, rows);
                _lastOutput = outPath; btnOpen.Enabled = true;
                Log("저장 완료: " + outPath);
                SafeClose();
            }
            catch (Exception ex) { Log("[예외] " + ex.Message); }
            finally { btnExtract.Enabled = true; }
        }

        // ---- 설정/프로필 저장 --------------------------------------------------
        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                SaveProfile();
                SaveLeafSettings();
                LoadProfilesIntoCombo();
                Log("설정 저장 완료. (프로필: leaf-profiles.txt, 공용: leaf-settings.config)");
            }
            catch (Exception ex) { Log("[저장 오류] " + ex.Message); }
        }

        private void SaveProfile()
        {
            string project = cmbProject.Text.Trim();
            if (project == "") return;
            Dictionary<string, string[]> all = LoadProfiles();
            all[project] = new string[] { txtUser.Text.Trim(), txtMdb.Text.Trim(), txtModule.Text.Trim(), txtStart.Text.Trim(), txtOutput.Text.Trim() };
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string[]> kv in all)
                sb.AppendLine(kv.Key + "\t" + string.Join("\t", kv.Value));
            File.WriteAllText(ProfilesPath, sb.ToString(), new UTF8Encoding(true));
        }

        private Dictionary<string, string[]> LoadProfiles()
        {
            Dictionary<string, string[]> all = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(ProfilesPath)) return all;
                foreach (string line in File.ReadAllLines(ProfilesPath))
                {
                    if (line.Trim() == "") continue;
                    string[] p = line.Split('\t');
                    if (p.Length >= 1) all[p[0]] = new string[] {
                        p.Length > 1 ? p[1] : "", p.Length > 2 ? p[2] : "",
                        p.Length > 3 ? p[3] : "", p.Length > 4 ? p[4] : "", p.Length > 5 ? p[5] : "" };
                }
            }
            catch { }
            return all;
        }

        private List<string> LoadProfileNames() { return new List<string>(LoadProfiles().Keys); }

        private void LoadProfilesIntoCombo()
        {
            string cur = cmbProject.Text;
            cmbProject.Items.Clear();
            foreach (string n in LoadProfileNames()) cmbProject.Items.Add(n);
            cmbProject.Text = cur;
        }

        private void LoadProfile(string project)
        {
            if (string.IsNullOrEmpty(project)) return;
            Dictionary<string, string[]> all = LoadProfiles();
            string[] v;
            if (all.TryGetValue(project, out v))
            {
                txtUser.Text = v[0]; txtMdb.Text = v[1]; txtModule.Text = v[2]; txtStart.Text = v[3]; txtOutput.Text = v[4];
                Log("프로필 불러옴: " + project);
            }
        }

        private void SaveLeafSettings()
        {
            // exe 옆 leaf-settings.config 를 현재 값 + 감지된 환경으로 갱신 (콘솔판도 사용)
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "leaf-settings.config");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<appSettings>");
            sb.AppendLine("  <add key=\"PROJECT\" value=\"" + Esc(cmbProject.Text.Trim()) + "\" />");
            sb.AppendLine("  <add key=\"USER\" value=\"" + Esc(txtUser.Text.Trim()) + "\" />");
            sb.AppendLine("  <add key=\"PASSWORD\" value=\"" + Esc(txtPass.Text) + "\" />");
            sb.AppendLine("  <add key=\"MDB\" value=\"" + Esc(txtMdb.Text.Trim()) + "\" />");
            sb.AppendLine("  <add key=\"MODULE_NUMBER\" value=\"" + Esc(txtModule.Text.Trim()) + "\" />");
            sb.AppendLine("  <add key=\"START_ELEMENT\" value=\"" + Esc(txtStart.Text.Trim()) + "\" />");
            sb.AppendLine("  <add key=\"OUTPUT_FILE\" value=\"" + Esc(txtOutput.Text.Trim()) + "\" />");
            sb.AppendLine("  <add key=\"PORT\" value=\"8731\" />");
            sb.AppendLine("  <add key=\"BIND\" value=\"localhost\" />");
            foreach (KeyValuePair<string, string> kv in _detectedEnv)
                sb.AppendLine("  <add key=\"" + Esc(kv.Key) + "\" value=\"" + Esc(kv.Value) + "\" />");
            sb.AppendLine("</appSettings>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        // ---- AVEVA 추출 로직 ---------------------------------------------------
        private static void SafeClose() { try { if (Project.CurrentProject != null) Project.CurrentProject.Close(); } catch { } }

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

        private static string ReadPdmsLog()
        {
            try
            {
                string[] dirs = new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory };
                foreach (string dir in dirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    string[] cands = Directory.GetFiles(dir, "*tandalone*log*.txt");
                    if (cands.Length == 0) cands = Directory.GetFiles(dir, "*log*.txt");
                    foreach (string p in cands)
                    {
                        try
                        {
                            string[] lines = File.ReadAllLines(p);
                            int n = Math.Min(lines.Length, 20);
                            string[] tail = new string[n];
                            Array.Copy(lines, lines.Length - n, tail, 0, n);
                            return p + "\r\n" + string.Join("\r\n", tail);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return "";
        }

        private static void SetupPdmsEnvironment(Hashtable env)
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
    }
}

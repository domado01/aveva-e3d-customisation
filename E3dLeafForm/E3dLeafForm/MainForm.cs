using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Aveva.Pdms.Database;
using Aveva.Pdms.Standalone;

namespace E3dLeafForm
{
    /// <summary>
    /// AVEVA Marine(PDMS) 최하위(leaf) 요소 추출 — 값 입력 폼.
    /// 화면에서 PROJECT/USER/PASSWORD/MDB/MODULE/START 를 입력하고 [추출] 클릭.
    /// </summary>
    public class MainForm : Form
    {
        private TextBox txtProject, txtUser, txtPass, txtMdb, txtModule, txtStart, txtOutput, txtLog;
        private Button btnExtract, btnOpen;
        private bool _started;
        private string _lastOutput;

        public MainForm()
        {
            Text = "AVEVA Marine - Leaf Export (최하위 요소 추출)";
            ClientSize = new Size(680, 620);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BuildUi();
            LoadDefaults();
            FormClosing += delegate { try { if (_started) PdmsStandalone.Finish(); } catch { } };
        }

        private void BuildUi()
        {
            int y = 15, lblX = 15, boxX = 160, boxW = 490;
            Func<string, TextBox> row = delegate(string label)
            {
                Controls.Add(new Label { Text = label, Left = lblX, Top = y + 4, Width = 140 });
                TextBox tb = new TextBox { Left = boxX, Top = y, Width = boxW };
                Controls.Add(tb); y += 33; return tb;
            };
            txtProject = row("PROJECT (코드)");
            txtUser    = row("USER");
            txtPass    = row("PASSWORD"); txtPass.UseSystemPasswordChar = true;
            txtMdb     = row("MDB");
            txtModule  = row("MODULE_NUMBER");
            txtStart   = row("START ELEMENT (SITE/ZONE 이름)");
            txtOutput  = row("출력 파일 (비우면 자동)");

            btnExtract = new Button { Text = "추출", Left = boxX, Top = y, Width = 110, Height = 32 };
            btnExtract.Click += BtnExtract_Click;
            btnOpen = new Button { Text = "결과 파일 열기", Left = boxX + 120, Top = y, Width = 130, Height = 32, Enabled = false };
            btnOpen.Click += delegate { try { if (_lastOutput != null) System.Diagnostics.Process.Start(_lastOutput); } catch { } };
            Controls.Add(btnExtract); Controls.Add(btnOpen);
            y += 44;

            Controls.Add(new Label { Text = "진행/결과 로그", Left = lblX, Top = y, Width = 200 }); y += 22;
            txtLog = new TextBox
            {
                Left = lblX, Top = y, Width = 650, Height = 230,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F), BackColor = Color.FromArgb(20, 22, 26), ForeColor = Color.Gainsboro
            };
            Controls.Add(txtLog);
        }

        private void LoadDefaults()
        {
            txtProject.Text = Cfg("PROJECT");
            txtUser.Text    = Cfg("USER");
            txtPass.Text    = Cfg("PASSWORD");
            txtMdb.Text     = Cfg("MDB");
            txtModule.Text  = Cfg("MODULE_NUMBER");
            txtStart.Text   = Cfg("START_ELEMENT");
            txtOutput.Text  = Cfg("OUTPUT_FILE");
            // AAA 자리표시자는 비워서 보여줌
            foreach (TextBox t in new[] { txtProject, txtUser, txtPass, txtMdb, txtModule, txtStart })
                if (t.Text == "AAA" || t.Text == "AAAA") t.Text = "";
            Log("값을 입력하고 [추출] 을 누르세요. (leaf-settings.config 의 값이 기본으로 채워집니다)");
        }

        private static string Cfg(string k) { string v = ConfigurationManager.AppSettings[k]; return v ?? ""; }
        private void Log(string m) { txtLog.AppendText(m + Environment.NewLine); txtLog.Update(); }

        private void BtnExtract_Click(object sender, EventArgs e)
        {
            btnExtract.Enabled = false; btnOpen.Enabled = false;
            try
            {
                string project = txtProject.Text.Trim();
                string user    = txtUser.Text.Trim();
                string pass    = txtPass.Text;
                string mdb     = txtMdb.Text.Trim();
                string startEl = txtStart.Text.Trim();

                if (project == "" || user == "" || mdb == "") { Log("[오류] PROJECT / USER / MDB 를 입력하세요. (PASSWORD 는 없으면 비움)"); return; }
                if (startEl == "") { Log("[오류] START ELEMENT 를 입력하세요 (예: SITE 또는 ZONE 이름)."); return; }
                int module; if (!int.TryParse(txtModule.Text.Trim(), out module)) module = 78;

                Hashtable env = new Hashtable();
                foreach (string key in ConfigurationManager.AppSettings.AllKeys) env[key] = ConfigurationManager.AppSettings[key];
                SetupPdmsEnvironment(env);

                if (!_started)
                {
                    Log("PDMS 세션 시작 (module " + module + ") ...");
                    PdmsStandalone.Start(module, env);
                    _started = true;
                }

                Log("프로젝트 열기: " + project + " / MDB " + mdb + " (user " + user + ") ...");
                if (!PdmsStandalone.Open(project, user, pass, mdb))
                {
                    Log("[오류] 로그인 실패 (Open=false). PROJECT/USER/PASSWORD/MDB 를 확인하세요.");
                    return;
                }
                Log("로그인 성공. 탐색 시작: " + startEl);

                DbElement start = DbElement.GetElement(startEl);
                if (start == null || !start.IsValid) { Log("[오류] 시작 요소를 찾을 수 없습니다: " + startEl); SafeClose(); return; }

                List<string[]> rows = new List<string[]>();
                Collect(start, rows);
                Log("최하위(leaf) 요소 " + rows.Count + " 개 수집 완료.");

                string outPath = txtOutput.Text.Trim();
                if (outPath == "")
                    outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                           "E3D_Leaf_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                WriteOut(outPath, project, mdb, startEl, rows);
                _lastOutput = outPath; btnOpen.Enabled = true;
                Log("저장 완료: " + outPath);
                SafeClose();
            }
            catch (Exception ex) { Log("[예외] " + ex.Message); }
            finally { btnExtract.Enabled = true; }
        }

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
            using (StreamWriter sw = new StreamWriter(path, false, new System.Text.UTF8Encoding(true)))
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
            Environment.SetEnvironmentVariable("PDMSEXE", pdmsExe); env["PDMSEXE"] = pdmsExe;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PDMSUI")) && !env.ContainsKey("PDMSUI")) { Environment.SetEnvironmentVariable("PDMSUI", exeDir); env["PDMSUI"] = exeDir; }
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PDMSWK")) && !env.ContainsKey("PDMSWK")) { Environment.SetEnvironmentVariable("PDMSWK", exeDir); env["PDMSWK"] = exeDir; }
        }
    }
}

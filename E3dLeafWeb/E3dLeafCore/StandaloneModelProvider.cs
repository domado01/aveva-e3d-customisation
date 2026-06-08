using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Aveva.Pdms.Database;             // DbElement, DbElementType, DbAttributeInstance, Project
using Aveva.Pdms.Standalone;           // PdmsStandalone (Start/Open/Finish)

namespace E3dLeafCore
{
    /// <summary>① 프로젝트 정보 입력 모드 — 직접 프로젝트/MDB 를 열어 추출.</summary>
    public class StandaloneModelProvider : IModelProvider
    {
        private readonly Hashtable _env;
        private readonly int _defaultModule;
        private bool _started;

        public StandaloneModelProvider(Hashtable env, int defaultModule)
        {
            _env = env; _defaultModule = defaultModule;
        }

        public string Host { get { return "standalone"; } }
        public string[] Capabilities { get { return new[] { "standalone" }; } }

        public ExtractResponse Extract(ExtractRequest req)
        {
            var res = new ExtractResponse { Mode = "standalone", Rows = new List<LeafRow>() };

            // PASSWORD 는 없을 수 있으므로(비번 없는 프로젝트) 필수 검사에서 제외
            if (string.IsNullOrEmpty(req.Project) || string.IsNullOrEmpty(req.User) || string.IsNullOrEmpty(req.Mdb))
            {
                res.Ok = false; res.Error = "PROJECT / USER / MDB 를 입력하세요. (PASSWORD 는 없으면 비워두세요)"; return res;
            }
            if (req.Password == null) req.Password = "";

            int module = req.ModuleNumber > 0 ? req.ModuleNumber : _defaultModule;
            bool opened = false;
            try
            {
                if (!_started) { SetupPdmsEnvironment(_env); PdmsStandalone.Start(module, _env); _started = true; }

                // PdmsStandalone.Open(sProject, sUser, sPass, sMdbName) -> bool
                if (!PdmsStandalone.Open(req.Project, req.User, req.Password, req.Mdb))
                {
                    res.Ok = false; res.Error = "프로젝트 로그인 실패 (Open 이 false 반환). PROJECT/USER/PASSWORD/MDB 확인."; return res;
                }
                opened = true;

                // START_ELEMENT 필수 (필터/컬렉션 API 미사용 — 시작 요소부터 Members() 재귀)
                if (string.IsNullOrEmpty(req.StartElement))
                {
                    res.Ok = false; res.Error = "시작 요소(START ELEMENT)를 입력하세요. (예: /SITE-... 또는 ZONE 이름)"; return res;
                }
                var roots = new List<DbElement>();
                DbElement s = DbElement.GetElement(req.StartElement);
                if (s == null || !s.IsValid)
                {
                    res.Ok = false; res.Error = "시작 요소를 찾을 수 없습니다: " + req.StartElement; return res;
                }
                roots.Add(s);

                foreach (DbElement r in roots) LeafCollector.Collect(r, res.Rows);
                res.Count = res.Rows.Count;
                res.Text = TextBuilder.Build("standalone", req.Project, req.Mdb, req.StartElement, res.Rows);
                res.Ok = true;
                return res;
            }
            catch (Exception ex) { res.Ok = false; res.Error = ex.Message; return res; }
            finally
            {
                // 요청마다 프로젝트는 닫되, 세션(PdmsStandalone.Finish)은 프로세스 종료 시 1회만.
                try { if (opened && Project.CurrentProject != null) Project.CurrentProject.Close(); }
                catch { }
            }
        }

        /// <summary>프로세스 종료 시 호출 — Standalone 세션 종료.</summary>
        public void Shutdown()
        {
            try { if (_started) PdmsStandalone.Finish(); } catch { }
        }

        /// <summary>PDMS 데이터 파일(attlib.dat 등)을 찾도록 PDMSEXE 등 환경변수를 보정.</summary>
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

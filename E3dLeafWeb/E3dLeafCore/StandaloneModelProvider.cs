using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Aveva.Pdms.Database;             // DbElement, DbElementType, DbAttributeInstance, Project (확인됨)
using Aveva.Pdms.Standalone;           // PdmsStandalone (확인됨)

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
                if (!_started) { PdmsStandalone.Start(module, _env); _started = true; }

                string openErr;
                if (!OpenSession(req.Project, req.User, req.Password, req.Mdb, out openErr))
                {
                    res.Ok = false; res.Error = "프로젝트 로그인 실패. " + openErr; return res;
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

        /// <summary>
        /// PdmsStandalone.Open 을 리플렉션으로 호출 (버전별 인수 차이 자동 대응).
        /// </summary>
        private static bool OpenSession(string project, string user, string pass, string mdb, out string err)
        {
            err = "";
            Type t = typeof(PdmsStandalone);
            MethodInfo open = null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                if (m.Name != "Open") continue;
                if (open == null || m.GetParameters().Length > open.GetParameters().Length) open = m;
            }
            if (open == null) { err = "Open 메서드를 찾을 수 없음"; return false; }

            ParameterInfo[] ps = open.GetParameters();
            string[] supplied = new string[] { project, user, pass, mdb };
            object[] args = new object[ps.Length];
            int si = 0, outIdx = -1;
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                if (ps[i].IsOut || pt.IsByRef) { args[i] = null; outIdx = i; }
                else if (pt == typeof(string)) { args[i] = (si < supplied.Length) ? supplied[si++] : ""; }
                else if (pt == typeof(int)) args[i] = 0;
                else if (pt == typeof(bool)) args[i] = false;
                else args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            object target = open.IsStatic ? null : Activator.CreateInstance(t);
            object res = open.Invoke(target, args);
            if (outIdx >= 0 && args[outIdx] != null) err = "응답: " + args[outIdx];
            if (res is bool) return (bool)res;
            return true;
        }
    }
}

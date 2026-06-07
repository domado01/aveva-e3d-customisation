using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;

// ============================================================================
//  AVEVA Marine (PDMS, OH12.1) (.NET) — Standalone Leaf Export
//  TG1031-CC3-C01 ".Net Customisation" 매뉴얼 기반 (Marine PDMS API 로 포팅)
//
//  필요 어셈블리 (Marine 설치 폴더 C:\AVEVA\Marine\OH12.1.SP5 에서 참조):
//    - Aveva.Pdms.Database.dll      (DbElement, DbElementType, DbAttributeInstance,
//                                     DbElementTypeInstance, TypeFilter, DBElementCollection, Project)
//    - Aveva.Pdms.Utilities.dll     (PdmsMessage)
//    - Aveva.Pdms.Standalone.dll    (Standalone: Start / Open / Finish)
//
//  ※ 아래 using 네임스페이스가 설치 버전과 다르면(예: PdmsMessage 가
//    Aveva.Pdms.Utilities.Messages 에 있는 경우 등) Visual Studio 에서 해당
//    타입에 커서 → [빠른 작업(Ctrl+.)] → using 추가로 자동 해결하세요.
// ============================================================================
using Aveva.Pdms.Database;              // DbElement, DbElementType, DbAttributeInstance, DbElementTypeInstance, Project
using Aveva.PDMS.Database.Filters;      // TypeFilter, DBElementCollection  (주의: 대문자 PDMS)
using Aveva.Pdms.Utilities.Messaging;   // PdmsMessage
using Aveva.PDMS.Standalone;            // Standalone  (대문자 PDMS — 미확정 시 check-types.cmd 로 확인)

namespace E3dLeafExport
{
    /// <summary>
    /// 샘플 모델의 "제일 하위 단위"(멤버가 없는 leaf 요소)의
    /// 이름(NAME)과 참조값(REF)을 텍스트 파일로 추출하는 Standalone 콘솔 프로그램.
    /// </summary>
    internal static class Program
    {
        /// <summary>추출된 leaf 한 줄을 표현하는 작은 구조체.</summary>
        private sealed class LeafRow
        {
            public string Type;   // 요소 타입 (예: BOX, CYLI, ELBO ...)
            public string Name;   // NAME 속성 (이름 없는 요소는 빈 값)
            public string Reference; // 참조값 (예: =12345/678)
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== AVEVA E3D Standalone Leaf Export ===");

            // ----------------------------------------------------------------
            // 1) 설정 읽기 (App.config / 커맨드라인)
            //    - App.config 의 모든 appSettings 를 Hashtable(env) 로 적재해
            //      Standalone.Start 에 그대로 전달한다(매뉴얼 ConfigHelper 와 동일 방식).
            // ----------------------------------------------------------------
            Hashtable env = ReadAppSettingsToHashtable();

            string project     = Cfg(env, "PROJECT");
            string user        = Cfg(env, "USER");
            string password    = Cfg(env, "PASSWORD");
            string mdb         = Cfg(env, "MDB");
            string startElement = Cfg(env, "START_ELEMENT");
            string outputFile  = Cfg(env, "OUTPUT_FILE");
            int moduleNumber   = ParseIntOr(Cfg(env, "MODULE_NUMBER"), 78); // 78 = Model

            // 커맨드라인으로 시작요소 / 출력파일 덮어쓰기 가능
            //   사용법: E3dLeafExport.exe [시작요소이름] [출력파일경로]
            if (args.Length >= 1 && !string.IsNullOrEmpty(args[0])) startElement = args[0];
            if (args.Length >= 2 && !string.IsNullOrEmpty(args[1])) outputFile = args[1];

            // PASSWORD 는 없을 수 있으므로(비번 없는 프로젝트) 필수 검사에서 제외
            if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(mdb))
            {
                Console.Error.WriteLine("[오류] App.config 의 PROJECT / USER / MDB 값을 먼저 채워주세요. (PASSWORD 는 없으면 비워두세요)");
                return 1;
            }
            if (password == null) password = "";

            if (string.IsNullOrEmpty(outputFile))
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                outputFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                          "E3D_Leaf_Export_" + stamp + ".txt");
            }

            bool sessionStarted = false;
            bool projectOpened = false;

            try
            {
                // ------------------------------------------------------------
                // 2) Standalone 세션 시작 (Model 모듈)
                // ------------------------------------------------------------
                Console.WriteLine("Standalone 세션 시작 (module {0}) ...", moduleNumber);
                Standalone.Start(moduleNumber, env);
                sessionStarted = true;

                // ------------------------------------------------------------
                // 3) 프로젝트 + MDB 열기 (로그인)
                // ------------------------------------------------------------
                Console.WriteLine("프로젝트 열기: {0} / MDB {1} (user {2}) ...", project, mdb, user);
                PdmsMessage error;
                bool ok = Standalone.Open(project, user, password, mdb, out error);
                if (!ok)
                {
                    int? num = (error != null) ? error.MessageNumber : (int?)null;
                    Console.Error.WriteLine("[오류] 프로젝트 로그인 실패. 메시지 번호: {0}", num);
                    return 2;
                }
                projectOpened = true;
                Console.WriteLine("프로젝트 열기 성공.");

                // ------------------------------------------------------------
                // 4) 탐색 시작점 결정
                //    - START_ELEMENT 가 지정되면 그 요소부터,
                //    - 비어 있으면 MDB 안의 모든 SITE 부터 탐색.
                // ------------------------------------------------------------
                List<DbElement> roots = new List<DbElement>();

                if (!string.IsNullOrEmpty(startElement))
                {
                    DbElement start = DbElement.GetElement(startElement);
                    if (start == null || !start.IsValid)
                    {
                        Console.Error.WriteLine("[오류] 시작 요소를 찾을 수 없습니다: {0}", startElement);
                        return 3;
                    }
                    roots.Add(start);
                    Console.WriteLine("시작 요소: {0}", startElement);
                }
                else
                {
                    // 열린 설계 DB 전체에서 SITE 수집 (root 없는 필터 컬렉션)
                    TypeFilter siteFilter = new TypeFilter(DbElementTypeInstance.SITE);
                    DBElementCollection siteCol = new DBElementCollection(siteFilter);
                    foreach (DbElement site in siteCol)
                    {
                        if (site != null && site.IsValid) roots.Add(site);
                    }
                    Console.WriteLine("SITE {0} 개에서 탐색을 시작합니다.", roots.Count);
                }

                if (roots.Count == 0)
                {
                    Console.Error.WriteLine("[경고] 탐색할 SITE/요소가 없습니다. MDB 구성 또는 START_ELEMENT 를 확인하세요.");
                }

                // ------------------------------------------------------------
                // 5) 재귀 순회하여 leaf(최하위) 요소 수집
                // ------------------------------------------------------------
                List<LeafRow> leaves = new List<LeafRow>();
                foreach (DbElement root in roots)
                {
                    CollectLeaves(root, leaves);
                }
                Console.WriteLine("최하위(leaf) 요소 {0} 개 수집 완료.", leaves.Count);

                // ------------------------------------------------------------
                // 6) 텍스트 파일로 저장 (탭 구분: Type / Name / Reference)
                // ------------------------------------------------------------
                WriteOutput(outputFile, project, mdb, startElement, leaves);
                Console.WriteLine("저장 완료: {0}", outputFile);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[예외] " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 10;
            }
            finally
            {
                // ------------------------------------------------------------
                // 7) 정리: 프로젝트 닫기 → 세션 종료
                //    (Standalone.Finish 는 열린 프로젝트를 자동으로 닫지 않으므로
                //     반드시 Project.CurrentProject.Close() 를 먼저 호출)
                // ------------------------------------------------------------
                try
                {
                    if (projectOpened && Project.CurrentProject != null)
                        Project.CurrentProject.Close();
                }
                catch (Exception ex) { Console.Error.WriteLine("[정리:Close] " + ex.Message); }

                try
                {
                    if (sessionStarted) Standalone.Finish();
                }
                catch (Exception ex) { Console.Error.WriteLine("[정리:Finish] " + ex.Message); }
            }
        }

        /// <summary>
        /// 요소를 재귀적으로 내려가며 멤버(자식)가 없는 leaf 요소만 수집한다.
        /// </summary>
        private static void CollectLeaves(DbElement element, List<LeafRow> sink)
        {
            if (element == null || !element.IsValid) return;

            DbElement[] members = element.Members();

            // 멤버가 없으면 = 최하위 단위(leaf)
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

            // 멤버가 있으면 계속 내려간다
            foreach (DbElement child in members)
            {
                CollectLeaves(child, sink);
            }
        }

        // ---- 안전한 속성 취득 헬퍼 (이름 없는 요소/예외 대비) -------------------

        private static string SafeName(DbElement el)
        {
            try
            {
                string n = el.GetString(DbAttributeInstance.NAME);
                return string.IsNullOrEmpty(n) ? "" : n;
            }
            catch { return ""; }
        }

        private static string SafeRef(DbElement el)
        {
            try
            {
                // DbElement.ToString() 은 참조값(예: =12345/678) 을 반환한다.
                return el.ToString();
            }
            catch { return ""; }
        }

        private static string SafeType(DbElement el)
        {
            try
            {
                DbElementType t = el.GetElementType();
                return (t != null) ? t.Name : "";
            }
            catch { return ""; }
        }

        // ---- 출력 -------------------------------------------------------------

        private static void WriteOutput(string path, string project, string mdb,
                                        string startElement, List<LeafRow> leaves)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (StreamWriter sw = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                sw.WriteLine("# AVEVA E3D Standalone Leaf Export");
                sw.WriteLine("# Generated : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sw.WriteLine("# Project   : " + project);
                sw.WriteLine("# MDB       : " + mdb);
                sw.WriteLine("# Start     : " + (string.IsNullOrEmpty(startElement) ? "(all SITEs)" : startElement));
                sw.WriteLine("# Count     : " + leaves.Count);
                sw.WriteLine("#");
                sw.WriteLine("Type\tName\tReference");
                foreach (LeafRow r in leaves)
                {
                    sw.WriteLine(r.Type + "\t" + r.Name + "\t" + r.Reference);
                }
            }
        }

        // ---- 설정 헬퍼 --------------------------------------------------------

        /// <summary>App.config 의 모든 appSettings 키/값을 Hashtable 로 적재.</summary>
        private static Hashtable ReadAppSettingsToHashtable()
        {
            Hashtable ht = new Hashtable();
            foreach (string key in ConfigurationManager.AppSettings.AllKeys)
            {
                ht[key] = ConfigurationManager.AppSettings[key];
            }
            return ht;
        }

        private static string Cfg(Hashtable env, string key)
        {
            object v = env[key];
            return (v == null) ? "" : v.ToString().Trim();
        }

        private static int ParseIntOr(string s, int fallback)
        {
            int n;
            return int.TryParse(s, out n) ? n : fallback;
        }
    }
}

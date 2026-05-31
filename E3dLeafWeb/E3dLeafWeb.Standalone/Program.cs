using System;
using System.Collections;
using System.Configuration;
using E3dLeafCore;

namespace E3dLeafWeb.Standalone
{
    /// <summary>
    /// ① 프로젝트 정보 입력(Standalone) 모드 웹 호스트.
    /// AVEVA 설치 PC 에서 실행 → 브라우저로 접속 → 프로젝트 정보 입력해 추출.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // App.config 의 모든 appSettings 를 AVEVA 환경 Hashtable 로 적재 (Standalone.Start 용)
            var env = new Hashtable();
            foreach (string k in ConfigurationManager.AppSettings.AllKeys)
                env[k] = ConfigurationManager.AppSettings[k];

            int port = ParseInt(Get(env, "PORT"), 8731);
            var bind = string.Equals(Get(env, "BIND"), "lan", StringComparison.OrdinalIgnoreCase)
                       ? BindScope.Lan : BindScope.Localhost;
            int module = ParseInt(Get(env, "MODULE_NUMBER"), 78);

            var provider = new StandaloneModelProvider(env, module);
            using (var disp = new WorkerDispatcher())
            {
                var server = new LeafWebServer(provider, disp, new WebOptions { Port = port, Bind = bind });
                try
                {
                    server.Start();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[오류] 웹서버 시작 실패: " + ex.Message);
                    if (bind == BindScope.Lan)
                        Console.Error.WriteLine("       LAN 모드는 관리자 권한 또는 netsh URL ACL 등록이 필요합니다 (README 참고).");
                    return 2;
                }

                Console.WriteLine("AVEVA Leaf Export 웹서버 시작");
                Console.WriteLine("  주소 : " + server.Url + (bind == BindScope.Lan ? "   (+ LAN: http://<이 PC IP>:" + port + "/)" : ""));
                Console.WriteLine("  모드 : ① 프로젝트 정보 입력 (Standalone)");
                Console.WriteLine("브라우저에서 위 주소를 여세요. 종료하려면 Enter.");
                Console.ReadLine();

                server.Stop();
                provider.Shutdown();
            }
            return 0;
        }

        private static string Get(Hashtable e, string k) { var v = e[k]; return v == null ? "" : v.ToString().Trim(); }
        private static int ParseInt(string s, int d) { int n; return int.TryParse(s, out n) ? n : d; }
    }
}

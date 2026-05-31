using System;
using System.Windows.Forms;
using Aveva.ApplicationFramework;   // IAddin, ServiceManager
using E3dLeafCore;

namespace E3dLeafWeb.Addin
{
    /// <summary>
    /// ② 현재 열린 모델(Addin) 모드 웹 호스트.
    /// E3D/Marine 에 로드되면 세션 안에서 같은 웹서버를 띄운다.
    /// 브라우저로 접속 → 현재요소(CE) 기준으로 추출.
    ///
    /// 로드 방법(매뉴얼 6장):
    ///   - 빌드한 E3dLeafWeb.Addin.dll + E3dLeafCore.dll 을 CAF_ADDINS_PATH 폴더에 복사
    ///   - <module>Addins.xml 에 이 Addin 을 등록
    /// </summary>
    public class LeafExportAddin : IAddin
    {
        private LeafWebServer _server;
        private Control _uiCtrl;

        public string Name { get { return "Leaf Export Web"; } }
        public string Description { get { return "현재 모델의 최하위(leaf) 요소를 웹 UI로 추출"; } }

        public void Start(ServiceManager serviceManager)
        {
            // E3D UI 스레드에서 핸들을 만들어 DB 호출을 메인 스레드로 마샬링한다.
            _uiCtrl = new Control();
            var force = _uiCtrl.Handle; // 핸들 생성 강제 (UI 스레드)
            GC.KeepAlive(force);

            var dispatcher = new ControlDispatcher(_uiCtrl);
            var provider = new AddinModelProvider();

            // 필요 시 포트/바인드를 환경변수로 조정 가능
            int port = ReadIntEnv("LEAFWEB_PORT", 8731);
            var bind = string.Equals(Environment.GetEnvironmentVariable("LEAFWEB_BIND"), "lan", StringComparison.OrdinalIgnoreCase)
                       ? BindScope.Lan : BindScope.Localhost;

            _server = new LeafWebServer(provider, dispatcher, new WebOptions { Port = port, Bind = bind });
            _server.Start();

            Console.WriteLine("[Leaf Export] Addin 웹서버 시작: " + _server.Url);
        }

        public void Stop()
        {
            try { if (_server != null) _server.Stop(); } catch { }
            try { if (_uiCtrl != null) _uiCtrl.Dispose(); } catch { }
        }

        private static int ReadIntEnv(string name, int fallback)
        {
            int n;
            return int.TryParse(Environment.GetEnvironmentVariable(name), out n) ? n : fallback;
        }
    }
}

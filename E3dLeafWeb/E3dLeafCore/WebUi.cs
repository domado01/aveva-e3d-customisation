using System.IO;
using System.Reflection;

namespace E3dLeafCore
{
    /// <summary>임베드된 webui.html 을 읽어 반환.</summary>
    public static class WebUi
    {
        private static string _html;
        public static string Html
        {
            get
            {
                if (_html == null)
                {
                    var asm = Assembly.GetExecutingAssembly();
                    using (var s = asm.GetManifestResourceStream("E3dLeafCore.webui.html"))
                    using (var r = new StreamReader(s))
                        _html = r.ReadToEnd();
                }
                return _html;
            }
        }
    }
}

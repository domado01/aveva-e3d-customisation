using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace E3dLeafCore
{
    /// <summary>
    /// HttpListener 기반 경량 웹서버. 콘솔(Standalone)과 E3D Addin 프로세스 안에서 모두 동작.
    /// 라우트: GET / (UI), GET /api/capabilities, POST /api/extract
    /// </summary>
    public class LeafWebServer
    {
        private readonly IModelProvider _provider;
        private readonly IDispatcher _disp;
        private readonly WebOptions _opt;
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _run;

        public LeafWebServer(IModelProvider provider, IDispatcher disp, WebOptions opt)
        {
            _provider = provider; _disp = disp; _opt = opt;
        }

        public string Url { get { return "http://127.0.0.1:" + _opt.Port + "/"; } }

        public void Start()
        {
            _listener = new HttpListener();
            if (_opt.Bind == BindScope.Lan)
            {
                // 다른 PC 접속 허용. http://+:port/ 는 URL ACL(관리자) 필요 — README 참고.
                _listener.Prefixes.Add("http://+:" + _opt.Port + "/");
            }
            else
            {
                _listener.Prefixes.Add("http://127.0.0.1:" + _opt.Port + "/");
                _listener.Prefixes.Add("http://localhost:" + _opt.Port + "/");
            }
            _listener.Start();
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public void Stop()
        {
            _run = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }

        private void Loop()
        {
            while (_run)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { break; }
                try { Handle(ctx); } catch { }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;

            if (path == "/")
            {
                WriteHtml(ctx, WebUi.Html);
                return;
            }
            if (path == "/api/capabilities")
            {
                WriteJson(ctx, new Capabilities { Host = _provider.Host, Modes = _provider.Capabilities }, typeof(Capabilities));
                return;
            }
            if (path == "/api/extract" && ctx.Request.HttpMethod == "POST")
            {
                ExtractResponse resp;
                try
                {
                    ExtractRequest req = ReadJson<ExtractRequest>(ctx);
                    resp = _disp.Run(() => _provider.Extract(req));
                }
                catch (Exception ex)
                {
                    resp = new ExtractResponse { Ok = false, Error = ex.Message };
                }
                WriteJson(ctx, resp, typeof(ExtractResponse));
                return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }

        private static T ReadJson<T>(HttpListenerContext ctx)
        {
            using (var ms = new MemoryStream())
            {
                ctx.Request.InputStream.CopyTo(ms);
                ms.Position = 0;
                var ser = new DataContractJsonSerializer(typeof(T));
                return (T)ser.ReadObject(ms);
            }
        }

        private static void WriteJson(HttpListenerContext ctx, object obj, Type t)
        {
            var ser = new DataContractJsonSerializer(t);
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, obj);
                byte[] bytes = ms.ToArray();
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
        }

        private static void WriteHtml(HttpListenerContext ctx, string html)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }
}

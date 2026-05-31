using System;
using System.Collections.Concurrent;
using System.Threading;

namespace E3dLeafCore
{
    /// <summary>
    /// 전용 단일 STA 스레드에서 모든 작업을 직렬 실행 (Standalone 호스트용).
    /// HttpListener 가 여러 스레드로 요청을 처리해도 AVEVA 호출은 한 스레드로 모인다.
    /// </summary>
    public sealed class WorkerDispatcher : IDispatcher, IDisposable
    {
        private readonly BlockingCollection<Action> _q = new BlockingCollection<Action>();
        private readonly Thread _t;

        public WorkerDispatcher()
        {
            _t = new Thread(Loop) { IsBackground = true };
            _t.SetApartmentState(ApartmentState.STA);
            _t.Start();
        }

        private void Loop()
        {
            foreach (var job in _q.GetConsumingEnumerable())
            {
                try { job(); } catch { }
            }
        }

        public ExtractResponse Run(Func<ExtractResponse> work)
        {
            ExtractResponse result = null;
            Exception err = null;
            using (var done = new ManualResetEventSlim(false))
            {
                _q.Add(() =>
                {
                    try { result = work(); }
                    catch (Exception ex) { err = ex; }
                    finally { done.Set(); }
                });
                done.Wait();
            }
            if (err != null) throw err;
            return result;
        }

        public void Dispose()
        {
            try { _q.CompleteAdding(); } catch { }
        }
    }

    /// <summary>
    /// E3D UI 스레드로 마샬링 (Addin 호스트용). E3D 는 WinForms 기반이므로
    /// UI 스레드에서 만든 Control 의 Invoke 로 DB 호출을 메인 스레드에 보낸다.
    /// </summary>
    public sealed class ControlDispatcher : IDispatcher
    {
        private readonly System.Windows.Forms.Control _ctrl;
        public ControlDispatcher(System.Windows.Forms.Control ctrl) { _ctrl = ctrl; }

        public ExtractResponse Run(Func<ExtractResponse> work)
        {
            if (_ctrl != null && _ctrl.InvokeRequired)
                return (ExtractResponse)_ctrl.Invoke(work);
            return work();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace E3dLeafCli
{
    /// <summary>
    /// 선택한 AM 의 GUI 창을 전면으로 가져와 명령(예: "ADD CE") + Enter 를 키 입력으로 보낸다.
    /// 외부 프로세스는 AM 3D 뷰를 직접 못 만지므로, AM 명령창에 실제 입력하는 방식.
    /// 주의: 환경(프로젝트)을 가진 프로세스와 GUI 창을 가진 프로세스가 다를 수 있어,
    ///       모든 AVEVA 창을 스캔해 pid → 프로젝트명 → 단일창 순으로 대상 창을 고른다.
    /// </summary>
    internal static class AmExec
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int max);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const ushort VK_RETURN = 0x0D;
        private const int SW_RESTORE = 9;

        private class WinInfo { public IntPtr Hwnd; public int Pid; public string Title = ""; }

        public static bool Exec(int preferredPid, string project, string cmd, out string err)
        {
            err = "";
            List<WinInfo> wins = FindAvevaWindows();
            if (wins.Count == 0)
            { err = "AVEVA GUI 창을 찾지 못했습니다. AM 이 최소화/숨김이면 복원한 뒤 다시 시도하세요."; return false; }

            IntPtr hwnd = IntPtr.Zero;
            // 1) 선택한 pid 의 창
            foreach (WinInfo w in wins) if (w.Pid == preferredPid) { hwnd = w.Hwnd; break; }
            // 2) 제목에 프로젝트명이 들어간 창
            if (hwnd == IntPtr.Zero && !string.IsNullOrEmpty(project))
                foreach (WinInfo w in wins) if (w.Title.IndexOf(project, StringComparison.OrdinalIgnoreCase) >= 0) { hwnd = w.Hwnd; break; }
            // 3) AVEVA 창이 하나뿐이면 그것
            if (hwnd == IntPtr.Zero && wins.Count == 1) hwnd = wins[0].Hwnd;

            if (hwnd == IntPtr.Zero)
            {
                List<string> t = new List<string>();
                foreach (WinInfo w in wins) t.Add("pid " + w.Pid + " : " + w.Title);
                err = "AM 창을 특정하지 못했습니다(여러 개). 후보: " + string.Join(" | ", t.ToArray())
                    + "  → 제목에 프로젝트명이 보이는 AM 을 목록에서 선택하거나, 그 AM 만 남기고 다시 시도하세요.";
                return false;
            }

            if (!ForceForeground(hwnd)) { /* 계속 시도 */ }
            Thread.Sleep(350);
            if (GetForegroundWindow() != hwnd)
            { err = "AM 창을 전면으로 가져오지 못했습니다. AM 명령창을 한번 클릭(포커스)한 뒤 다시 시도하세요."; return false; }

            TypeString(cmd);
            Thread.Sleep(60);
            TapVk(VK_RETURN);
            return true;
        }

        /// <summary>해당 pid 가 보이는 메인 창을 가지면 true + 제목.</summary>
        public static bool TryGetWindow(int pid, out string title)
        {
            IntPtr found = IntPtr.Zero; string t = "";
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) <= 0) return true;
                uint wp; GetWindowThreadProcessId(h, out wp);
                if ((int)wp == pid) { found = h; t = GetTitle(h); return false; }
                return true;
            }, IntPtr.Zero);
            title = t;
            return found != IntPtr.Zero;
        }

        private static List<WinInfo> FindAvevaWindows()
        {
            HashSet<int> avevaPids = ProcessEnv.AvevaPids();
            List<WinInfo> wins = new List<WinInfo>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) <= 0) return true;
                uint wp; GetWindowThreadProcessId(h, out wp);
                if (!avevaPids.Contains((int)wp)) return true;
                wins.Add(new WinInfo { Hwnd = h, Pid = (int)wp, Title = GetTitle(h) });
                return true;
            }, IntPtr.Zero);
            return wins;
        }

        private static string GetTitle(IntPtr h)
        {
            int n = GetWindowTextLength(h);
            if (n <= 0) return "";
            StringBuilder sb = new StringBuilder(n + 1);
            GetWindowText(h, sb, n + 1);
            return sb.ToString();
        }

        private static bool ForceForeground(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            uint dummy;
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out dummy);
            uint thisThread = GetCurrentThreadId();
            bool attached = false;
            if (fgThread != thisThread) attached = AttachThreadInput(thisThread, fgThread, true);
            BringWindowToTop(hwnd);
            bool ok = SetForegroundWindow(hwnd);
            if (attached) AttachThreadInput(thisThread, fgThread, false);
            return ok;
        }

        private static void TypeString(string s)
        {
            foreach (char c in s)
            {
                INPUT[] inp = new INPUT[2];
                inp[0].type = INPUT_KEYBOARD;
                inp[0].ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE, time = 0, dwExtraInfo = IntPtr.Zero };
                inp[1].type = INPUT_KEYBOARD;
                inp[1].ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero };
                SendInput(2, inp, Marshal.SizeOf(typeof(INPUT)));
                Thread.Sleep(8);
            }
        }

        private static void TapVk(ushort vk)
        {
            INPUT[] inp = new INPUT[2];
            inp[0].type = INPUT_KEYBOARD;
            inp[0].ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero };
            inp[1].type = INPUT_KEYBOARD;
            inp[1].ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero };
            SendInput(2, inp, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}

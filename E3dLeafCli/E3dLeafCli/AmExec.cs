using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace E3dLeafCli
{
    /// <summary>
    /// 선택한 AM 프로세스의 메인 창을 전면으로 가져와 명령 문자열 + Enter 를 키 입력으로 보낸다.
    /// (예: "ADD CE" → AM 명령처리기로 들어가 현재 선택요소를 3D 뷰에 ADD)
    /// 외부 프로세스가 AM 의 3D 뷰를 직접 조작할 수 없으므로, AM 명령창에 실제 입력하는 방식.
    /// </summary>
    internal static class AmExec
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
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

        public static bool Exec(int pid, string cmd, out string err)
        {
            err = "";
            IntPtr hwnd = FindMainWindow((uint)pid);
            if (hwnd == IntPtr.Zero) { err = "AM 메인 창을 찾지 못함 (pid " + pid + ")"; return false; }

            if (!ForceForeground(hwnd)) { /* 전면 실패해도 계속 시도 */ }
            Thread.Sleep(350);

            if (GetForegroundWindow() != hwnd)
            { err = "AM 창을 전면으로 가져오지 못했습니다. AM 명령창을 클릭해 포커스를 둔 뒤 다시 시도하세요."; return false; }

            TypeString(cmd);
            Thread.Sleep(60);
            TapVk(VK_RETURN);
            return true;
        }

        private static IntPtr FindMainWindow(uint pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                uint wp; GetWindowThreadProcessId(h, out wp);
                if (wp == pid && IsWindowVisible(h) && GetWindowTextLength(h) > 0) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static bool ForceForeground(IntPtr hwnd)
        {
            ShowWindow(hwnd, SW_RESTORE);
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out uint _);
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

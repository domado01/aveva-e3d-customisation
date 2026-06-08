using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace E3dLeafForm
{
    /// <summary>
    /// 실행 중인 다른 32비트 프로세스(AM/PDMS)의 환경변수를 PEB 에서 읽는다.
    /// AM 이 PDMS 를 띄울 때 설정한 projects_dir / 프로젝트경로 등을 그대로 가져와
    /// 우리 단독 세션에 적용하기 위함. (우리 폼도 x86, PDMS 도 x86 → 32비트 오프셋)
    /// </summary>
    internal static class ProcessEnv
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2a;
            public IntPtr Reserved2b;
            public IntPtr UniqueProcessId;
            public IntPtr Reserved3;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr h, int cls, ref PROCESS_BASIC_INFORMATION pbi, int len, out int ret);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_VM_READ = 0x0010;

        /// <summary>주어진 pid 의 환경변수를 읽어 반환 (실패 시 빈 사전).</summary>
        public static Dictionary<string, string> Read(int pid)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
            if (h == IntPtr.Zero) return result;
            try
            {
                PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                int ret;
                if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf(pbi), out ret) != 0) return result;
                // 32비트 PEB: +0x10 ProcessParameters, RTL_USER_PROCESS_PARAMETERS: +0x48 Environment
                IntPtr procParams = ReadPtr32(h, pbi.PebBaseAddress, 0x10);
                if (procParams == IntPtr.Zero) return result;
                IntPtr envPtr = ReadPtr32(h, procParams, 0x48);
                if (envPtr == IntPtr.Zero) return result;

                int size = 128 * 1024;
                byte[] buf = new byte[size];
                int read;
                if (!ReadProcessMemory(h, envPtr, buf, size, out read) || read < 4) return result;
                ParseEnv(buf, read, result);
            }
            catch { }
            finally { CloseHandle(h); }
            return result;
        }

        private static IntPtr ReadPtr32(IntPtr h, IntPtr baseAddr, int offset)
        {
            byte[] b = new byte[4];
            int read;
            IntPtr addr = (IntPtr)(baseAddr.ToInt64() + offset);
            if (!ReadProcessMemory(h, addr, b, 4, out read) || read != 4) return IntPtr.Zero;
            return (IntPtr)BitConverter.ToUInt32(b, 0);
        }

        private static void ParseEnv(byte[] buf, int len, Dictionary<string, string> result)
        {
            // UTF-16LE, "KEY=VALUE\0" 반복, 마지막 "\0\0"
            int i = 0;
            StringBuilder sb = new StringBuilder();
            while (i + 1 < len)
            {
                char c = (char)(buf[i] | (buf[i + 1] << 8));
                i += 2;
                if (c == '\0')
                {
                    if (sb.Length == 0) break; // 이중 널 = 끝
                    string s = sb.ToString(); sb.Length = 0;
                    int eq = s.IndexOf('=');
                    if (eq > 0) result[s.Substring(0, eq)] = s.Substring(eq + 1);
                }
                else sb.Append(c);
            }
        }

        /// <summary>
        /// AVEVA 경로에서 실행 중인 프로세스 중 projects_dir(또는 프로젝트코드000)을
        /// 가진 환경을 찾아 반환. 못 찾으면 빈 사전.
        /// </summary>
        public static Dictionary<string, string> FindAvevaEnv(out string procName)
        {
            procName = "";
            foreach (Process p in Process.GetProcesses())
            {
                string path;
                try { path = (p.MainModule != null) ? p.MainModule.FileName : ""; }
                catch { continue; }
                if (string.IsNullOrEmpty(path) || path.IndexOf("AVEVA", StringComparison.OrdinalIgnoreCase) < 0) continue;

                Dictionary<string, string> env = Read(p.Id);
                if (env.Count == 0) continue;
                if (env.ContainsKey("projects_dir") || HasProjectVar(env)) { procName = p.ProcessName; return env; }
            }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasProjectVar(Dictionary<string, string> env)
        {
            foreach (string k in env.Keys)
                if (k.Length >= 6 && k.EndsWith("000")) return true;
            return false;
        }

        /// <summary>환경에서 프로젝트 코드 목록 추출 (XXX000 키 → XXX).</summary>
        public static List<string> ProjectCodes(Dictionary<string, string> env)
        {
            List<string> codes = new List<string>();
            foreach (string k in env.Keys)
                if (k.Length >= 6 && k.EndsWith("000"))
                {
                    string code = k.Substring(0, k.Length - 3);
                    if (!codes.Contains(code)) codes.Add(code);
                }
            codes.Sort();
            return codes;
        }
    }
}

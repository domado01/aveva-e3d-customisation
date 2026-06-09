using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace E3dLeafCli
{
    /// <summary>실행 중인 32비트 AM/PDMS 프로세스의 환경변수를 PEB 에서 읽는다.</summary>
    internal static class ProcessEnv
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1; public IntPtr PebBaseAddress; public IntPtr Reserved2a;
            public IntPtr Reserved2b; public IntPtr UniqueProcessId; public IntPtr Reserved3;
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
            byte[] b = new byte[4]; int read;
            IntPtr addr = (IntPtr)(baseAddr.ToInt64() + offset);
            if (!ReadProcessMemory(h, addr, b, 4, out read) || read != 4) return IntPtr.Zero;
            return (IntPtr)BitConverter.ToUInt32(b, 0);
        }
        private static void ParseEnv(byte[] buf, int len, Dictionary<string, string> result)
        {
            int i = 0; StringBuilder sb = new StringBuilder();
            while (i + 1 < len)
            {
                char c = (char)(buf[i] | (buf[i + 1] << 8)); i += 2;
                if (c == '\0')
                {
                    if (sb.Length == 0) break;
                    string s = sb.ToString(); sb.Length = 0;
                    int eq = s.IndexOf('=');
                    if (eq > 0) result[s.Substring(0, eq)] = s.Substring(eq + 1);
                }
                else sb.Append(c);
            }
        }
        public static Dictionary<string, string> FindAvevaEnv(out string procName)
        {
            procName = "";
            foreach (Process p in Process.GetProcesses())
            {
                string path;
                try { path = (p.MainModule != null) ? p.MainModule.FileName : ""; } catch { continue; }
                if (string.IsNullOrEmpty(path) || path.IndexOf("AVEVA", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Dictionary<string, string> env = Read(p.Id);
                if (env.Count == 0) continue;
                if (env.ContainsKey("projects_dir") || HasProjectVar(env)) { procName = p.ProcessName; return env; }
            }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        private static bool HasProjectVar(Dictionary<string, string> env)
        {
            foreach (string k in env.Keys) if (k.Length >= 6 && k.EndsWith("000")) return true;
            return false;
        }
        public static List<string> ProjectCodes(Dictionary<string, string> env)
        {
            List<string> codes = new List<string>();
            foreach (string k in env.Keys)
                if (k.Length >= 6 && k.EndsWith("000")) { string c = k.Substring(0, k.Length - 3); if (!codes.Contains(c)) codes.Add(c); }
            codes.Sort();
            return codes;
        }

        /// <summary>주어진 pid 의 명령줄(CommandLine)을 PEB 에서 읽는다. (어떤 프로젝트로 띄웠는지 추정용)</summary>
        public static string ReadCommandLine(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                int ret;
                if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf(pbi), out ret) != 0) return "";
                IntPtr procParams = ReadPtr32(h, pbi.PebBaseAddress, 0x10);
                if (procParams == IntPtr.Zero) return "";
                // 32비트 RTL_USER_PROCESS_PARAMETERS: +0x40 CommandLine (UNICODE_STRING)
                return ReadUnicodeString(h, procParams, 0x40);
            }
            catch { return ""; }
            finally { CloseHandle(h); }
        }

        private static string ReadUnicodeString(IntPtr h, IntPtr baseAddr, int offset)
        {
            byte[] lenBuf = new byte[4]; int read;
            if (!ReadProcessMemory(h, (IntPtr)(baseAddr.ToInt64() + offset), lenBuf, 4, out read) || read != 4) return "";
            int length = BitConverter.ToUInt16(lenBuf, 0);
            if (length <= 0) return "";
            if (length > 32768) length = 32768;
            IntPtr buf = ReadPtr32(h, baseAddr, offset + 4);
            if (buf == IntPtr.Zero) return "";
            byte[] data = new byte[length];
            if (!ReadProcessMemory(h, buf, data, length, out read) || read <= 0) return "";
            return Encoding.Unicode.GetString(data, 0, read);
        }

        /// <summary>실행 중인 모든 AVEVA(프로젝트 환경 보유) 프로세스를 나열.</summary>
        public static List<AmProc> ListAm()
        {
            List<AmProc> list = new List<AmProc>();
            foreach (Process p in Process.GetProcesses())
            {
                string path;
                try { path = (p.MainModule != null) ? p.MainModule.FileName : ""; }
                catch { continue; }
                if (string.IsNullOrEmpty(path) || path.IndexOf("AVEVA", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Dictionary<string, string> env = Read(p.Id);
                if (env.Count == 0) continue;
                if (!(env.ContainsKey("projects_dir") || HasProjectVar(env))) continue;
                list.Add(new AmProc { Pid = p.Id, Name = p.ProcessName, Path = path, Env = env, CmdLine = ReadCommandLine(p.Id) });
            }
            return list;
        }

        /// <summary>pid 의 프로세스 이름(실패 시 빈 문자열).</summary>
        public static string ProcName(int pid)
        {
            try { return Process.GetProcessById(pid).ProcessName; } catch { return ""; }
        }
    }

    internal class AmProc
    {
        public int Pid;
        public string Name = "";
        public string Path = "";
        public Dictionary<string, string> Env;
        public string CmdLine = "";
    }
}

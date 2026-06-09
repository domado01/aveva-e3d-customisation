using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace E3dLeafCli
{
    /// <summary>
    /// AVEVA 관리/네이티브 DLL 을 어디서 실행하든 AVEVA bin 에서 로드되도록 설정.
    /// (exe 가 AVEVA bin 밖에 있으면 'could not load file or assembly ...' 오류가 나므로)
    /// </summary>
    internal static class AvevaHost
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static string BinDir = "";

        public static void Setup()
        {
            BinDir = FindBin();
            if (BinDir == "") return;
            try { SetDllDirectory(BinDir); } catch { }
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (path.IndexOf(BinDir, StringComparison.OrdinalIgnoreCase) < 0)
                    Environment.SetEnvironmentVariable("PATH", BinDir + ";" + path);
            }
            catch { }
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name;
                string p = Path.Combine(BinDir, name + ".dll");
                if (File.Exists(p)) return Assembly.LoadFrom(p);
                // bin 의 한 단계 상/하위도 탐색 (설치본이 폴더 분리된 경우)
                string parent = Path.GetDirectoryName(BinDir);
                if (parent != null)
                {
                    string[] hits = SafeFind(parent, name + ".dll");
                    if (hits.Length > 0) return Assembly.LoadFrom(hits[0]);
                }
            }
            catch { }
            return null;
        }

        private static string[] SafeFind(string root, string file)
        {
            try { return Directory.GetFiles(root, file, SearchOption.AllDirectories); }
            catch { return new string[0]; }
        }

        private static string FindBin()
        {
            // 1) exe 자신의 폴더에 AVEVA DLL 이 있으면 그곳
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (File.Exists(Path.Combine(baseDir, "Aveva.Pdms.Database.dll"))) return baseDir;
            // 2) 실행 중인 AM 프로세스의 폴더
            try { string b = ProcessEnv.FindAvevaBinDir(); if (b != "") return b; } catch { }
            // 3) 흔한 설치 경로 재귀 탐색
            foreach (string g in new[] { @"C:\AVEVA\Marine\OH12.1.SP5", @"C:\AVEVA\Marine", @"C:\AVEVA", @"C:\Program Files (x86)\AVEVA" })
            {
                string[] hits = SafeFind(g, "Aveva.Pdms.Database.dll");
                if (hits.Length > 0) return Path.GetDirectoryName(hits[0]);
            }
            return "";
        }
    }
}

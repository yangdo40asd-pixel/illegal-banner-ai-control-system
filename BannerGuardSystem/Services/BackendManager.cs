using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BannerGuardSystem.Services;

/// <summary>
/// Python FastAPI AI 백엔드 프로세스 라이프사이클 관리.
/// WPF 시작 시 서버 기동, WPF 종료 시 서버 함께 종료 (Job Object).
/// </summary>
public static class BackendManager
{
    private static Process? _backendProcess;
    private static JobObject? _jobObject;
    private static readonly object _lock = new();
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend_manager.log");

    public static void Log(string message)
    {
        try
        {
            string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(LogFilePath, formatted);
            Debug.WriteLine(message);
        }
        catch { }
    }

    /// <summary>WPF 시작 시 호출 — 포트 8000 정리 후 백엔드 기동.</summary>
    public static void Start()
    {
        lock (_lock)
        {
            Log("==========================================================");
            Log("[BackendManager] 백엔드 시작 요청 수신 (WPF 연동 모드).");

            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                Log("[BackendManager] 백엔드 프로세스가 이미 실행 중입니다.");
                return;
            }

            KillProcessOnPort(8000);

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exePath = Path.Combine(baseDir, "ai_server", "ai_server.exe");
                if (!File.Exists(exePath))
                    exePath = Path.Combine(baseDir, "ai_server.exe");

                string fileName;
                string arguments;
                string workingDir;

                if (File.Exists(exePath))
                {
                    Log($"[BackendManager] 배포 모드 감지. 실행 파일: {exePath}");
                    fileName = exePath;
                    arguments = string.Empty;
                    workingDir = Path.GetDirectoryName(exePath) ?? baseDir;
                }
                else
                {
                    string? scriptPath = FindDeveloperBackendScript();
                    if (scriptPath == null)
                    {
                        Log("[BackendManager] 오류: 배포용 exe 및 개발용 main.py를 찾을 수 없습니다.");
                        return;
                    }

                    string pythonExe = FindPythonExecutable();
                    Log($"[BackendManager] 개발자 모드. 파이썬: {pythonExe}");
                    Log($"[BackendManager] 스크립트: {scriptPath}");

                    fileName = pythonExe;
                    arguments = $"-u \"{scriptPath}\"";
                    string? projRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(scriptPath)));
                    workingDir = projRoot ?? baseDir;
                }

                _jobObject = new JobObject();

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                string pythonDir = Path.GetDirectoryName(fileName) ?? string.Empty;
                if (!string.IsNullOrEmpty(pythonDir))
                {
                    string extraPaths = $"{pythonDir};{Path.Combine(pythonDir, "Library", "bin")};{Path.Combine(pythonDir, "Scripts")};{Path.Combine(pythonDir, "Library", "mingw-w64", "bin")};{Path.Combine(pythonDir, "Library", "usr", "bin")}";
                    string? currentPath = startInfo.EnvironmentVariables.ContainsKey("PATH")
                        ? startInfo.EnvironmentVariables["PATH"]
                        : Environment.GetEnvironmentVariable("PATH");
                    startInfo.EnvironmentVariables["PATH"] = $"{extraPaths};{currentPath}";
                    Log($"[BackendManager] PATH에 Conda 경로 추가: {extraPaths}");
                }

                _backendProcess = new Process { StartInfo = startInfo };
                _backendProcess.OutputDataReceived += (s, e) => { if (e.Data != null) Log($"[AI-Server] {e.Data}"); };
                _backendProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) Log($"[AI-Server-Err] {e.Data}"); };

                Log($"[BackendManager] 프로세스 기동: {fileName} {arguments} (cwd: {workingDir})");
                _backendProcess.Start();
                _backendProcess.BeginOutputReadLine();
                _backendProcess.BeginErrorReadLine();

                _jobObject.AddProcess(_backendProcess.Handle);
                Log("[BackendManager] Job Object 등록 완료 — WPF 종료 시 서버 자동 종료.");
            }
            catch (Exception ex)
            {
                Log($"[BackendManager] 프로세스 기동 실패: {ex.Message}");
                Log($"[BackendManager] {ex.StackTrace}");
                Stop();
            }
        }
    }

    /// <summary>WPF 종료 시 호출 — 백엔드 프로세스 강제 종료.</summary>
    public static void Stop()
    {
        lock (_lock)
        {
            Log("[BackendManager] 백엔드 종료 요청 수신 (WPF 연동 모드).");
            try
            {
                if (_backendProcess != null)
                {
                    if (!_backendProcess.HasExited)
                    {
                        Log("[BackendManager] 백엔드 프로세스 Kill.");
                        _backendProcess.Kill(entireProcessTree: true);
                    }
                    _backendProcess.Dispose();
                    _backendProcess = null;
                }
            }
            catch (Exception ex)
            {
                Log($"[BackendManager] 프로세스 종료 중 예외: {ex.Message}");
            }

            try
            {
                _jobObject?.Dispose();
                _jobObject = null;
            }
            catch { }

            KillProcessOnPort(8000);
            Log("[BackendManager] 프로세스 관리 정리 완료.");
        }
    }

    private static string? FindDeveloperBackendScript()
    {
        string current = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            string target = Path.Combine(current, "backend", "app", "main.py");
            if (File.Exists(target))
                return target;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }

    private static string FindPythonExecutable()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] possiblePaths =
        {
            Path.Combine(userProfile, "anaconda3", "envs", "banner_env", "python.exe"),
            Path.Combine(userProfile, "miniconda3", "envs", "banner_env", "python.exe"),
            Path.Combine(userProfile, "anaconda3", "python.exe"),
            Path.Combine(userProfile, "miniconda3", "python.exe"),
            Path.Combine(userProfile, "AppData", "Local", "miniconda3", "python.exe"),
            Path.Combine(userProfile, "AppData", "Local", "anaconda3", "python.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python313", "python.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python311", "python.exe"),
            @"C:\ProgramData\anaconda3\python.exe",
            @"C:\anaconda3\python.exe",
            @"C:\miniconda3\python.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                Log($"[BackendManager] 파이썬 탐지: {path}");
                return path;
            }
        }

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "where";
            process.StartInfo.Arguments = "python";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0 && File.Exists(lines[0].Trim()))
                    return lines[0].Trim();
            }
        }
        catch { }

        Log("[BackendManager] 경고: 파이썬 경로 미탐지. 'python' 폴백.");
        return "python";
    }

    private static void KillProcessOnPort(int port)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c netstat -ano | findstr :{port}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process == null) return;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 4 && int.TryParse(parts[^1].Trim(), out int pid) && pid > 0)
                {
                    if (pid == Environment.ProcessId) continue;
                    try
                    {
                        var procToKill = Process.GetProcessById(pid);
                        Log($"[BackendManager] 포트 {port} 점유 프로세스 종료: PID={pid}, Name={procToKill.ProcessName}");
                        procToKill.Kill(entireProcessTree: true);
                        procToKill.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log($"[BackendManager] PID {pid} 종료 실패: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[BackendManager] 포트 정리 중 예외: {ex.Message}");
        }
    }
}

internal class JobObject : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private IntPtr _handle;
    private bool _disposed;

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private enum JobObjectInfoType { ExtendedLimitInformation = 9 }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public byte SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    public JobObject()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Job Object를 생성할 수 없습니다.");

        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE };
        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = info };
        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr ptr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, ptr, false);
            if (!SetInformationJobObject(_handle, JobObjectInfoType.ExtendedLimitInformation, ptr, (uint)length))
                throw new InvalidOperationException("Job Object 제한 설정 실패.");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public bool AddProcess(IntPtr processHandle) =>
        _handle != IntPtr.Zero && AssignProcessToJobObject(_handle, processHandle);

    public void Dispose()
    {
        if (_disposed) return;
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
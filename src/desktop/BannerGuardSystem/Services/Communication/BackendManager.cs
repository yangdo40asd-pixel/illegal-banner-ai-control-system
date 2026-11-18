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
    private static string LogFilePath => Helpers.RuntimePaths.RuntimeFile("logs", "backend_manager.log");
    private static bool _isExternalServerReused;

    public static bool IsExternalServerReused => _isExternalServerReused;
    public static int? OwnedProcessId => _backendProcess != null && !_backendProcess.HasExited ? _backendProcess.Id : null;

    public static bool IsServerHealthy(string? url = null)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
            var response = client.GetAsync(url ?? Helpers.RuntimePaths.Get("ApiBaseUrl", "http://localhost:8000").TrimEnd('/') + "/health").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

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

    /// <summary>WPF 시작 시 호출 — 기존 AI 서버 확인 또는 신규 자식 프로세스 기동.</summary>
    public static void Start()
    {
        lock (_lock)
        {
            Log("==========================================================");
            Log("[BackendManager] 백엔드 시작 요청 수신 (WPF 연동 모드).");

            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                Log($"[BackendManager] 자신이 기동한 백엔드 프로세스가 이미 실행 중입니다 (PID={_backendProcess.Id}).");
                return;
            }

            if (IsServerHealthy())
            {
                Log("[BackendManager] 기존 AI 서버가 이미 포트 8000에서 정상 구동 중입니다. 기존 서버를 재사용합니다.");
                _isExternalServerReused = true;
                return;
            }

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
                    workingDir = Helpers.RuntimePaths.Root;
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
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                startInfo.Environment["BANNERGUARD_ROOT"] = Helpers.RuntimePaths.Root;
                startInfo.Environment["BANNERGUARD_CONFIG"] = Helpers.RuntimePaths.ConfigurationFile;
                startInfo.Environment["PYTHONUTF8"] = "1";
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

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

    /// <summary>WPF 종료 시 호출 — 자신이 직접 기동한 백엔드 프로세스만 안전하게 종료.</summary>
    public static void Stop()
    {
        lock (_lock)
        {
            Log("[BackendManager] 백엔드 종료 요청 수신 (WPF 연동 모드).");

            if (_isExternalServerReused)
            {
                Log("[BackendManager] 기존 외부 가동 서버를 재사용 중이었으므로 해당 프로세스를 kill하지 않고 보존합니다.");
                _isExternalServerReused = false;
                return;
            }

            try
            {
                if (_backendProcess != null)
                {
                    if (!_backendProcess.HasExited)
                    {
                        Log($"[BackendManager] 자식 백엔드 프로세스(PID={_backendProcess.Id})를 안전하게 종료합니다.");
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

            Log("[BackendManager] 자식 프로세스 관리 정리 완료.");
        }
    }

    private static string? FindDeveloperBackendScript()
    {
        var target = Helpers.RuntimePaths.Resolve("BackendEntry", "src/backend/app/main.py");
        return File.Exists(target) ? target : null;
    }

    private static string FindPythonExecutable()
    {
        var configured = Helpers.RuntimePaths.Get("PythonExecutable");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.IsPathRooted(configured) ? configured : Path.Combine(Helpers.RuntimePaths.Root, configured);
        var virtualEnvironment = Path.Combine(Helpers.RuntimePaths.Root, ".venv", "Scripts", "python.exe");
        if (File.Exists(virtualEnvironment)) return virtualEnvironment;
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

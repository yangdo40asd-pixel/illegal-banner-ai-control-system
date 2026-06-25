using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BannerGuardSystem.Services;

/// <summary>
/// Python FastAPI AI 백엔드 프로세스의 라이프사이클을 제어하고,
/// Windows Job Object 커널 객체에 매핑하여 좀비 프로세스 방지를 보장하는 관리자 서비스입니다.
/// </summary>
public static class BackendManager
{
    private static Process? _backendProcess;
    private static JobObject? _jobObject;
    private static readonly object _lock = new();
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend_manager.log");

    /// <summary>
    /// 로그 파일에 디버그 메시지를 기록합니다.
    /// </summary>
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

    /// <summary>
    /// 백엔드 서버 구동을 시도합니다.
    /// 이미 구동 중이거나 포트가 점유되어 있다면 실행하지 않습니다.
    /// </summary>
    public static void Start()
    {
        lock (_lock)
        {
            // 로그 파일 초기화/구분선 삽입
            Log("==========================================================");
            Log("[BackendManager] 백엔드 시작 요청 수신.");

            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                Log("[BackendManager] 백엔드 프로세스가 이미 실행 중입니다.");
                return;
            }

            // 기존 백그라운드에 남아 포트를 점유하고 있는 파이썬/FastAPI 좀비 프로세스 정리
            KillProcessOnPort(8000);

            try
            {
                // 1. 배포 모드 실행파일 탐색 (ai_server/ai_server.exe)
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exePath = Path.Combine(baseDir, "ai_server", "ai_server.exe");

                // 로컬 폴더(동일 경로) 내 직접 위치 탐색 지원
                if (!File.Exists(exePath))
                {
                    exePath = Path.Combine(baseDir, "ai_server.exe");
                }

                string fileName;
                string arguments;
                string workingDir;

                if (File.Exists(exePath))
                {
                    Log($"[BackendManager] 배포 모드 감지. 실행 파일 경로: {exePath}");
                    fileName = exePath;
                    arguments = string.Empty;
                    workingDir = Path.GetDirectoryName(exePath) ?? baseDir;
                }
                else
                {
                    // 2. 개발 모드 탐색 (python backend/app/main.py)
                    string? scriptPath = FindDeveloperBackendScript();
                    if (scriptPath == null)
                    {
                        Log("[BackendManager] 오류: 배포용 exe 및 개발용 main.py 파일을 모두 찾을 수 없습니다.");
                        return;
                    }

                    Log($"[BackendManager] 개발자 모드 감지. 파이썬 스크립트 경로: {scriptPath}");
                    
                    // 환경변수에 등록되지 않은 Anaconda / Miniconda 파이썬 경로 정밀 탐색
                    string pythonExe = FindPythonExecutable();
                    Log($"[BackendManager] 사용할 파이썬 인터프리터 경로: {pythonExe}");

                    fileName = pythonExe;
                    arguments = $"-u \"{scriptPath}\"";
                    
                    // WorkingDirectory를 현재 프로젝트의 루트 디렉터리로 설정하여 backend/app/main.py 위치 인지 보완
                    string? projRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(scriptPath)));
                    workingDir = projRoot ?? baseDir;
                }

                // 3. Job Object 초기화 (좀비 프로세스 강제 자동 격추 목적)
                _jobObject = new JobObject();

                // 4. 프로세스 시작 정보 설정
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

                // CUDA DLL 및 파이썬 종속성 정상 로드를 위해 Conda 환경 경로를 PATH 환경 변수에 동적 추가
                string pythonDir = Path.GetDirectoryName(fileName) ?? string.Empty;
                if (!string.IsNullOrEmpty(pythonDir))
                {
                    string extraPaths = $"{pythonDir};{Path.Combine(pythonDir, "Library", "bin")};{Path.Combine(pythonDir, "Scripts")};{Path.Combine(pythonDir, "Library", "mingw-w64", "bin")};{Path.Combine(pythonDir, "Library", "usr", "bin")}";
                    string? currentPath = startInfo.EnvironmentVariables.ContainsKey("PATH") ? startInfo.EnvironmentVariables["PATH"] : Environment.GetEnvironmentVariable("PATH");
                    startInfo.EnvironmentVariables["PATH"] = $"{extraPaths};{currentPath}";
                    Log($"[BackendManager] 환경 변수 PATH에 추가된 Conda 경로: {extraPaths}");
                }

                _backendProcess = new Process { StartInfo = startInfo };

                // 비동기 로그 수집 리스너 등록 (파일로 실시간 기록)
                _backendProcess.OutputDataReceived += (s, e) => { if (e.Data != null) Log($"[AI-Server] {e.Data}"); };
                _backendProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) Log($"[AI-Server-Err] {e.Data}"); };

                Log($"[BackendManager] 프로세스 기동 명령 실행: {fileName} {arguments} (작업 디렉토리: {workingDir})");
                _backendProcess.Start();

                _backendProcess.BeginOutputReadLine();
                _backendProcess.BeginErrorReadLine();

                // 5. Job Object에 프로세스 핸들 등록
                _jobObject.AddProcess(_backendProcess.Handle);
                Log("[BackendManager] 프로세스가 Job Object에 성공적으로 할당 및 격리되었습니다.");
            }
            catch (Exception ex)
            {
                Log($"[BackendManager] 프로세스 기동 실패 (치명적 오류): {ex.Message}");
                Log($"[BackendManager] 상세 예외 정보: {ex.StackTrace}");
                Stop();
            }
        }
    }

    /// <summary>
    /// 구동 중인 백엔드 프로세스를 강제 종료하고 리소스를 반환합니다.
    /// </summary>
    public static void Stop()
    {
        lock (_lock)
        {
            Log("[BackendManager] 백엔드 종료 요청 수신.");
            try
            {
                if (_backendProcess != null)
                {
                    if (!_backendProcess.HasExited)
                    {
                        Log("[BackendManager] 백엔드 프로세스를 명시적으로 종료(Kill)합니다.");
                        _backendProcess.Kill();
                    }
                    _backendProcess.Dispose();
                    _backendProcess = null;
                }
            }
            catch (Exception ex)
            {
                Log($"[BackendManager] 프로세스 종료 처리 중 예외 발생: {ex.Message}");
            }

            try
            {
                if (_jobObject != null)
                {
                    _jobObject.Dispose();
                    _jobObject = null;
                }
            }
            catch { }
            Log("[BackendManager] 프로세스 관리 정리 완료.");
        }
    }

    /// <summary>
    /// 디렉토리 트리를 거슬러 올라가며 개발자용 main.py의 상대 경로를 탐색합니다.
    /// </summary>
    private static string? FindDeveloperBackendScript()
    {
        string current = AppDomain.CurrentDomain.BaseDirectory;
        // 탐색 깊이를 10단계로 확장하여 publish/debug 하위 깊이에서도 정상적으로 루트 디렉터리를 탐색하도록 함
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

    /// <summary>
    /// 시스템 PATH 및 사용자 아나콘다/미니콘다 설치 디렉토리에서 python.exe의 절대 경로를 탐색합니다.
    /// </summary>
    private static string FindPythonExecutable()
    {
        // 1. 일반적인 Anaconda / Miniconda 설치 절대 경로 목록 탐색
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] possiblePaths = new[]
        {
            // 사용자가 지정한 Conda banner_env 환경 경로 지원
            Path.Combine(userProfile, "anaconda3", "envs", "banner_env", "python.exe"),
            Path.Combine(userProfile, "miniconda3", "envs", "banner_env", "python.exe"),
            
            // 기존 디폴트 환경 경로들
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
                Log($"[BackendManager] 아나콘다/로컬 설치 경로에서 파이썬 탐지 성공: {path}");
                return path;
            }
        }

        // 2. PATH 환경변수 상에서 python.exe 탐색 시도
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
                {
                    return lines[0].Trim();
                }
            }
        }
        catch { }

        // 3. 탐지 실패 시 기본 OS 커맨드 대체
        Log("[BackendManager] 경고: 파이썬 실행파일의 명시적 경로 탐지 실패. 기본 환경 변수 'python' 호출로 폴백합니다.");
        return "python";
    }

    /// <summary>
    /// 특정 포트를 사용하고 있는 프로세스를 찾아 강제 종료합니다.
    /// </summary>
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
                if (parts.Length > 4)
                {
                    string pidStr = parts[parts.Length - 1].Trim();
                    if (int.TryParse(pidStr, out int pid) && pid > 0)
                    {
                        // 본인 프로세스는 종료하지 않음
                        if (pid == Environment.ProcessId) continue;

                        try
                        {
                            var procToKill = Process.GetProcessById(pid);
                            Log($"[BackendManager] 포트 {port}을(를) 점유 중인 프로세스 종료 시도: PID={pid}, Name={procToKill.ProcessName}");
                            procToKill.Kill();
                            procToKill.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Log($"[BackendManager] PID {pid} 종료 실패: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[BackendManager] 포트 점유 프로세스 정리 중 예외 발생: {ex.Message}");
        }
    }
}

/// <summary>
/// 윈도우 OS 커널의 Job Object를 호출하여 부모 프로세스 종료 시 자식 프로세스를 자동 격추하도록 관리하는 래퍼 클래스입니다.
/// </summary>
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

    public JobObject()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Job Object를 생성할 수 없습니다.");

        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        };

        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = info
        };

        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
            if (!SetInformationJobObject(_handle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
                throw new InvalidOperationException("Job Object의 제한 정보를 설정할 수 없습니다.");
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    public bool AddProcess(IntPtr processHandle)
    {
        if (_handle == IntPtr.Zero) return false;
        return AssignProcessToJobObject(_handle, processHandle);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
            _disposed = true;
        }
    }

    ~JobObject()
    {
        Dispose(false);
    }

    #region Native Win32 API Definitions

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

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

    #endregion
}

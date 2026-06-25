@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
cd /d "%ROOT%"

set "REQUIRED=D:\138.종합 민원 이미지 AI데이터"

echo ============================================================
echo  BannerGuardSystem - 다른 PC 초기 설치
echo ============================================================
echo.

if /i not "%CD%"=="%REQUIRED%" (
    echo [중요] 현재 폴더: %CD%
    echo [중요] AI 백엔드 경로가 고정되어 있어 아래 경로여야 합니다:
    echo          %REQUIRED%
    echo.
    echo  GitHub Desktop ^> Clone ^> Local path 를 위 경로로 지정하세요.
    echo.
    pause
)

echo [사전 요구사항]
echo   - .NET 9 SDK
echo   - Python 3.11+ 또는 Anaconda/Miniconda
echo   - NVIDIA GPU + CUDA ^(Qwen2.5-VL 추론용^)
echo   - 인터넷 ^(최초 실행 시 AI 모델 자동 다운로드, 약 6GB^)
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [오류] dotnet을 찾을 수 없습니다. .NET 9 SDK를 설치하세요.
    echo        https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

set "PYTHON="
set "USERPROFILE_DIR=%USERPROFILE%"

for %%P in (
    "%USERPROFILE_DIR%\anaconda3\envs\banner_env\python.exe"
    "%USERPROFILE_DIR%\miniconda3\envs\banner_env\python.exe"
    "%USERPROFILE_DIR%\anaconda3\python.exe"
    "%USERPROFILE_DIR%\miniconda3\python.exe"
    "%USERPROFILE_DIR%\AppData\Local\Programs\Python\Python313\python.exe"
    "%USERPROFILE_DIR%\AppData\Local\Programs\Python\Python312\python.exe"
    "%USERPROFILE_DIR%\AppData\Local\Programs\Python\Python311\python.exe"
) do (
    if exist %%P (
        set "PYTHON=%%~P"
        goto :found_python
    )
)

where python >nul 2>&1
if not errorlevel 1 (
    for /f "delims=" %%P in ('where python 2^>nul') do (
        set "PYTHON=%%P"
        goto :found_python
    )
)

echo [오류] Python을 찾을 수 없습니다.
echo        Anaconda 설치 후 banner_env 환경을 쓰면 가장 안정적입니다.
pause
exit /b 1

:found_python
echo [1/3] Python: !PYTHON!
"!PYTHON!" -m pip install --upgrade pip
"!PYTHON!" -m pip install -r "%ROOT%\backend\requirements.txt"
if errorlevel 1 (
    echo [오류] Python 패키지 설치 실패
    pause
    exit /b 1
)

echo.
echo [2/3] WPF 빌드 중...
dotnet restore "%ROOT%\BannerGuardSystem\BannerGuardSystem.sln"
dotnet build "%ROOT%\BannerGuardSystem\BannerGuardSystem.sln" -c Release
if errorlevel 1 (
    echo [오류] WPF 빌드 실패
    pause
    exit /b 1
)

if not exist "%ROOT%\yolo26s\runs\banner_s_20260506_1909\weights\best.pt" (
    echo [오류] YOLO 모델 ^(best.pt^)이 없습니다. Git pull을 다시 확인하세요.
    pause
    exit /b 1
)

echo.
echo [3/3] 설치 완료!
echo.
echo  실행: run.bat 더블클릭
echo  최초 AI 분석 시 Qwen 모델 다운로드로 5~15분 걸릴 수 있습니다.
echo.
pause
endlocal
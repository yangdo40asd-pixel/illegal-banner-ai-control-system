@echo off
chcp 65001 >nul
setlocal

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

set "EXE=%ROOT%\BannerGuardSystem\bin\Release\net9.0-windows\BannerGuardSystem.exe"

if not exist "%EXE%" (
    set "EXE=%ROOT%\BannerGuardSystem\bin\Debug\net9.0-windows\BannerGuardSystem.exe"
)

if not exist "%EXE%" (
    echo [오류] 실행 파일이 없습니다. 먼저 setup.bat 을 실행하세요.
    pause
    exit /b 1
)

echo BannerGuardSystem 실행 중...
echo ^(WPF가 Python AI 백엔드를 자동으로 기동/종료합니다^)
start "" "%EXE%"
endlocal
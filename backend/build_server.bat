@echo off
echo ===================================================
echo [Build] Starting PyInstaller compilation for ai_server...
echo ===================================================
pyinstaller --clean ai_server.spec
echo ===================================================
echo [Build] PyInstaller compilation completed successfully!
echo ===================================================
pause

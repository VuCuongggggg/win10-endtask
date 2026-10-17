@echo off
echo Building Win10EndTask...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /win32manifest:"app.manifest" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Accessibility.dll /out:"Win10EndTask.exe" "Program.cs"
if %ERRORLEVEL% EQU 0 (
    echo [OK] Build successful: Win10EndTask.exe
) else (
    echo [ERROR] Build failed!
)
pause

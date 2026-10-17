@echo off
setlocal
cd /d "%~dp0"
rem Builds BrickMyBoard.exe with the C# compiler included in every Windows 10/11 install (.NET Framework 4.8). No SDK needed.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" echo The .NET Framework 4.8 compiler was not found. && exit /b 1

echo Generating icon...
powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1 ..\Assets\AppIcon.png AppIcon.ico || exit /b 1

echo Compiling...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /nowarn:1591 ^
  /win32manifest:app.manifest /win32icon:AppIcon.ico ^
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  /out:BrickMyBoard.exe KeyboardLock.cs Checks.cs App.cs Views.cs Program.cs || exit /b 1
copy /y app.config BrickMyBoard.exe.config >nul

echo Running self-checks...
"%~dp0BrickMyBoard.exe" --self-test || exit /b 1
"%~dp0BrickMyBoard.exe" --check
echo Built BrickMyBoard.exe

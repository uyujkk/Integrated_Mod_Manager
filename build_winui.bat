@echo off
setlocal

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

set "DOTNET=%ProgramFiles%\dotnet\x64\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"

set "MSBUILD=C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if not exist "%MSBUILD%" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if not exist "%MSBUILD%" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

set "PROJECT=%ROOT%\WinUI3\ModFolderCopier.WinUI.csproj"
set "SOURCE=%ROOT%\WinUI3\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
set "OUTPUT=%ROOT%\dist\WinUI3"
set "LAUNCHER_SOURCE=%ROOT%\WinUILauncher.cs"
set "LAUNCHER_OUTPUT=%ROOT%\dist\ModFolderCopier.exe"
set "SEVENZIP_DIR=C:\Program Files\7-Zip"
set "TOOLS_OUTPUT=%OUTPUT%\Tools"
set "APP_ICON=%ROOT%\WinUI3\Assets\AppIcon.ico"

if not exist "%PROJECT%" (
  echo WinUI project not found.
  exit /b 1
)

echo Building WinUI 3 project...
if exist "%MSBUILD%" (
  echo Using MSBuild: %MSBUILD%
  "%MSBUILD%" "%PROJECT%" /restore /t:Build /p:Configuration=Release /p:Platform=x64 || exit /b 1
) else (
  if not exist "%DOTNET%" (
    echo Neither MSBuild nor dotnet SDK was found.
    exit /b 1
  )
  echo MSBuild not found, falling back to dotnet build.
  "%DOTNET%" build "%PROJECT%" -c Release -p:Platform=x64 || exit /b 1
)

if not exist "%ROOT%\dist" mkdir "%ROOT%\dist"
if not exist "%OUTPUT%" mkdir "%OUTPUT%"

robocopy "%SOURCE%" "%OUTPUT%" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
if errorlevel 8 exit /b %errorlevel%

if exist "%ROOT%\dist\config.ini" copy /Y "%ROOT%\dist\config.ini" "%OUTPUT%\config.ini" >nul

if exist "%SEVENZIP_DIR%\7z.exe" (
  if not exist "%TOOLS_OUTPUT%" mkdir "%TOOLS_OUTPUT%"
  copy /Y "%SEVENZIP_DIR%\7z.exe" "%TOOLS_OUTPUT%\7z.exe" >nul
)
if exist "%SEVENZIP_DIR%\7z.dll" (
  if not exist "%TOOLS_OUTPUT%" mkdir "%TOOLS_OUTPUT%"
  copy /Y "%SEVENZIP_DIR%\7z.dll" "%TOOLS_OUTPUT%\7z.dll" >nul
)
if exist "%SEVENZIP_DIR%\License.txt" (
  if not exist "%TOOLS_OUTPUT%" mkdir "%TOOLS_OUTPUT%"
  copy /Y "%SEVENZIP_DIR%\License.txt" "%TOOLS_OUTPUT%\7zip-License.txt" >nul
)

if exist "%APP_ICON%" (
  "%CSC%" /nologo /target:winexe /win32icon:"%APP_ICON%" /out:"%LAUNCHER_OUTPUT%" /reference:System.dll /reference:System.Windows.Forms.dll "%LAUNCHER_SOURCE%" || exit /b 1
) else (
  "%CSC%" /nologo /target:winexe /out:"%LAUNCHER_OUTPUT%" /reference:System.dll /reference:System.Windows.Forms.dll "%LAUNCHER_SOURCE%" || exit /b 1
)

if exist "%OUTPUT%\startup.log" del /f /q "%OUTPUT%\startup.log"
if exist "%OUTPUT%\ModFolderCopier.WinUI.pdb" del /f /q "%OUTPUT%\ModFolderCopier.WinUI.pdb"

echo WinUI 3 build completed.
echo Launcher: %LAUNCHER_OUTPUT%
echo Runtime:  %OUTPUT%
endlocal

@echo off
setlocal

set "ROOT=%~dp0.."
set "OUT=%ROOT%\artifacts\native\win-x64"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if not exist "%VSWHERE%" (
  echo Visual Studio Installer vswhere.exe was not found. 1>&2
  exit /b 2
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"
if not defined VSINSTALL (
  echo Visual Studio with the Desktop development with C++ workload was not found. 1>&2
  exit /b 3
)

set "VSCMD_SKIP_SENDTELEMETRY=1"
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b %errorlevel%

if not exist "%OUT%" mkdir "%OUT%"
pushd "%OUT%"

cl /nologo /LD /O2 /W4 /WX /utf-8 /std:c17 /TC "%ROOT%\native\learn_c\learn_c.c" /Fe:"%OUT%\learn_c.dll" /link /INCREMENTAL:NO
if errorlevel 1 goto :fail

cl /nologo /LD /O2 /W4 /WX /utf-8 /EHsc /std:c++20 "%ROOT%\native\learn_cpp\learn_cpp.cpp" /Fe:"%OUT%\learn_cpp.dll" /link /INCREMENTAL:NO
if errorlevel 1 goto :fail

popd
exit /b 0

:fail
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%

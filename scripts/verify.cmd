@echo off
setlocal

for %%I in ("%~dp0..") do set "ROOT=%%~fI"

call "%ROOT%\scripts\setup-python.cmd"
if errorlevel 1 exit /b %errorlevel%

dotnet restore "%ROOT%\LearnDotnetCSharp.slnx" --ignore-failed-sources --nologo
if errorlevel 1 exit /b %errorlevel%

dotnet build "%ROOT%\LearnDotnetCSharp.slnx" --configuration Release --no-restore --nologo -p:EnforceCodeStyleInBuild=true
if errorlevel 1 exit /b %errorlevel%

rem Folder mode verifies whitespace without loading the mixed C#/F#/VB project graph.
dotnet format whitespace "%ROOT%" --folder --verify-no-changes --include "%ROOT%\src" "%ROOT%\tests" --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

dotnet test --solution "%ROOT%\LearnDotnetCSharp.slnx" --configuration Release --no-build --minimum-expected-tests 45
if errorlevel 1 exit /b %errorlevel%

dotnet run --project "%ROOT%\src\LearnDotnetCSharp.App" --configuration Release --no-build -- self-test
exit /b %errorlevel%

@echo off
setlocal

set "ROOT=%~dp0.."
set "DOCS_PYTHON=%ROOT%\.docs-venv\Scripts\python.exe"

if defined LEARN_DOTNET_DOCS_PYTHON (
    set "DOCS_PYTHON=%LEARN_DOTNET_DOCS_PYTHON%"
) else (
    call "%ROOT%\scripts\setup-docs-python.cmd"
    if errorlevel 1 exit /b 1
)

"%DOCS_PYTHON%" "%ROOT%\scripts\markdown_to_pdf.py" ^
    --input "%ROOT%\docs\advanced-dotnet-csharp-study-guide.md" ^
    --output "%ROOT%\output\pdf\LearnDotnetCSharp-Study-Guide.pdf" ^
    --repo-root "%ROOT%" ^
    --repo-url "https://github.com/yuweiyang9611/LearnDotnetCSharp/blob/main" ^
    %*
exit /b %errorlevel%

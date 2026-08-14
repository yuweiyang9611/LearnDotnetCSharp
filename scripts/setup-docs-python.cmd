@echo off
setlocal

set "ROOT=%~dp0.."
set "WORKSPACE_PYTHON=%ROOT%\.venv\Scripts\python.exe"
set "DOCS_PYTHON=%ROOT%\.docs-venv\Scripts\python.exe"

if not exist "%WORKSPACE_PYTHON%" (
    call "%ROOT%\scripts\setup-python.cmd"
    if errorlevel 1 exit /b 1
)

if not exist "%DOCS_PYTHON%" (
    "%WORKSPACE_PYTHON%" -m venv "%ROOT%\.docs-venv"
    if errorlevel 1 exit /b 1
)

"%DOCS_PYTHON%" -c "import sys; from importlib.metadata import version; from pathlib import Path; requirements=[line.strip().split('==',1) for line in Path(sys.argv[1]).read_text(encoding='utf-8').splitlines() if line.strip() and not line.lstrip().startswith('#')]; assert all(len(item)==2 and version(item[0])==item[1] for item in requirements)" "%ROOT%\requirements-docs.txt" >nul 2>nul
if not errorlevel 1 exit /b 0

set "PIP_DISABLE_PIP_VERSION_CHECK=1"
set "PIP_NO_INPUT=1"
set "PIP_INDEX_URL=https://pypi.tuna.tsinghua.edu.cn/simple"
set "PIP_DEFAULT_TIMEOUT=20"
set "PIP_RETRIES=2"
"%DOCS_PYTHON%" -m pip install ^
    --require-virtualenv ^
    --no-deps ^
    --only-binary=:all: ^
    --index-url "https://pypi.tuna.tsinghua.edu.cn/simple" ^
    --timeout 20 ^
    --retries 2 ^
    --requirement "%ROOT%\requirements-docs.txt"
if "%ERRORLEVEL%"=="0" exit /b 0

echo Tsinghua PyPI mirror failed; retrying with official PyPI...
set "PIP_INDEX_URL=https://pypi.org/simple"
"%DOCS_PYTHON%" -m pip install ^
    --require-virtualenv ^
    --no-deps ^
    --only-binary=:all: ^
    --index-url "https://pypi.org/simple" ^
    --timeout 20 ^
    --retries 2 ^
    --requirement "%ROOT%\requirements-docs.txt"
if "%ERRORLEVEL%"=="0" exit /b 0
exit /b 1

@echo off
setlocal

set "ROOT=%~dp0.."
set "PYTHON=%ROOT%\.venv\Scripts\python.exe"

if not exist "%PYTHON%" (
  where py >nul 2>nul
  if not errorlevel 1 (
    py -3 -m venv "%ROOT%\.venv"
  ) else (
    python -m venv "%ROOT%\.venv"
  )
  if errorlevel 1 exit /b 1
)

"%PYTHON%" -c "import sys; assert sys.prefix != sys.base_prefix; print('workspace venv:', sys.executable)"
exit /b %errorlevel%

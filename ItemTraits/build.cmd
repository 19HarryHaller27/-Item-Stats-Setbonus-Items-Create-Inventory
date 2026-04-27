@echo off
cd /d "%~dp0"
dotnet build -c Release
exit /b %ERRORLEVEL%

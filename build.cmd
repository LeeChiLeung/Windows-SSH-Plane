@echo off
chcp 65001 >nul
setlocal

rem 选一个真正带 SDK 的 dotnet：PATH 里的可能只有运行时（能跑程序但没有编译能力）
set "DOTNET="
for /f "delims=" %%i in ('dotnet --list-sdks 2^>nul') do set "DOTNET=dotnet"
if not defined DOTNET (
  if exist "%USERPROFILE%\.dotnet-sdk\dotnet.exe" (
    for /f "delims=" %%i in ('"%USERPROFILE%\.dotnet-sdk\dotnet.exe" --list-sdks 2^>nul') do set "DOTNET=%USERPROFILE%\.dotnet-sdk\dotnet.exe"
  )
)
if not defined DOTNET (
  echo [错误] 未找到可用的 .NET SDK（PATH 里的 dotnet 可能只有运行时）：
  echo        请安装 .NET 10 SDK https://dotnet.microsoft.com/download
  echo        或把它装到 %USERPROFILE%\.dotnet-sdk 后再运行本脚本
  pause
  exit /b 1
)
echo 使用 SDK: %DOTNET%

if /i "%1"=="selfcontained" (
  echo [1/2] 自包含单文件发布 ...
  "%DOTNET%" publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist-selfcontained
  if errorlevel 1 goto fail
  echo [2/2] 完成：dist-selfcontained\Windows-SSH-Plane.exe
  goto end
)

echo [1/3] 编译 ...
"%DOTNET%" build -c Release
if errorlevel 1 goto fail

echo [2/3] 自检 ...
bin\Release\net10.0-windows\Windows-SSH-Plane.exe --selftest
if errorlevel 1 goto fail

echo [3/3] 发布到 dist ...
"%DOTNET%" publish -c Release -o dist
if errorlevel 1 goto fail
echo 完成：dist\Windows-SSH-Plane.exe
goto end

:fail
echo.
echo [失败] 构建未通过，请查看上方输出。
pause
exit /b 1

:end
endlocal
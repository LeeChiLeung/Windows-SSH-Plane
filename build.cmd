@echo off
chcp 65001 >nul
setlocal

rem 若 PATH 里没有 dotnet，尝试本机已安装的用户目录版本
set "DOTNET=dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  if exist "%USERPROFILE%\.dotnet-sdk\dotnet.exe" (
    set "DOTNET=%USERPROFILE%\.dotnet-sdk\dotnet.exe"
  ) else (
    echo [错误] 未找到 dotnet SDK，请先安装 .NET 10 SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
  )
)

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
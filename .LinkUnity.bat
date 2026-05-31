@echo off
setlocal

REM 检查是否以管理员身份运行
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo Request administrator permissions...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:InputDir
set /p target_path=Please enter the target directory path: 
if "%target_path%"=="" (
    echo You must enter a directory path.
    goto InputDir
)

REM 尝试切换到用户输入的目录，失败则重新输入
cd /D "%target_path%" 2>nul
if errorlevel 1 (
    echo The directory "%target_path%" does not exist or cannot be accessed.
    goto InputDir
)

REM 设置目标目录为 Packages 子目录
set "target_dir=%cd%\Packages"
echo Target directory: %target_dir%

REM 切换回脚本目录
cd /D "%~dp0"

REM 设置源目录为脚本所在目录，去掉末尾反斜杠
set "source_dir=%~dp0"
set "source_dir=%source_dir:~0,-1%"
echo Source directory: %source_dir%

REM 创建软链接
mklink /D "%target_dir%\com.tencent.dawn.tod" "%source_dir%"

endlocal
pause
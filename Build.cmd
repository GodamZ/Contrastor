@echo off
setlocal
pushd "%~dp0"
set "CONTRASTOR_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist dist mkdir dist
"%CONTRASTOR_CSC%" /nologo /target:winexe /optimize+ /win32manifest:src\Contrastor.manifest /out:dist\Contrastor.exe src\Contrastor.cs src\ScreenPicker.cs src\VerdictLabel.cs
if errorlevel 1 goto failed
"%CONTRASTOR_CSC%" /nologo /target:winexe /optimize+ /win32manifest:src\Contrastor.manifest /resource:dist\Contrastor.exe,Contrastor.exe /out:dist\Contrastor-Installation.exe src\Setup.cs
if errorlevel 1 goto failed
popd
exit /b 0
:failed
popd
exit /b 1

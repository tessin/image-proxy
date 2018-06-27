@echo off

if not "%VSCMD_VER%"=="" (
	goto VsDevCmd
)

set "VSCMD_START_DIR=%CD%"

if exist "C:\Program Files (x86)\Microsoft Visual Studio\2017\BuildTools\Common7\Tools\VsDevCmd.bat" (
	call "C:\Program Files (x86)\Microsoft Visual Studio\2017\BuildTools\Common7\Tools\VsDevCmd.bat" %*
	goto VsDevCmd
)

if exist "C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\Common7\Tools\VsDevCmd.bat" (
	call "C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\Common7\Tools\VsDevCmd.bat" %*
	goto VsDevCmd
)

if exist "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\Common7\Tools\VsDevCmd.bat" (
	call "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\Common7\Tools\VsDevCmd.bat" %*
	goto VsDevCmd
)

:VsDevCmd

msbuild FunctionApp1.sln /t:FunctionApp1 /nologo /p:Configuration=Release /clp:ErrorsOnly;Summary /m
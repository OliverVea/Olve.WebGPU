@echo off
rem Links webgpu_dawn.dll from Dawn's static webgpu_dawn.lib and builds webgpu_layout_check.dll, with the newest Visual Studio's x64 tools.
rem usage: build-natives-windows.cmd <dawn dir> <native out dir> <layout-check out dir (holds webgpu_dawn.def)>
setlocal
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VS=%%i
if not defined VS (echo Visual Studio with the C++ tools not found & exit /b 1)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
link /nologo /DLL /OUT:"%~2\webgpu_dawn.dll" /DEF:"%~3\webgpu_dawn.def" /IMPLIB:"%~3\webgpu_dawn.lib" /OPT:REF /OPT:ICF /ignore:4221 "%~1\lib\webgpu_dawn.lib" user32.lib onecore_apiset.lib dxguid.lib advapi32.lib dbghelp.lib || exit /b 1
cl /nologo /LD /O1 /I"%~1\include" "%~dp0..\native\webgpu_layout_check.c" /Fe"%~3\webgpu_layout_check.dll" /Fo"%~3\\" /link /EXPORT:wgpu_layout_values /EXPORT:wgpu_init_struct || exit /b 1

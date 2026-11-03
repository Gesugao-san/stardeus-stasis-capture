@ECHO OFF
:: Сборка 0.15\Libraries\StasisCapture.dll против установленной игры (Stardeus v0.15).
:: Нужен .NET SDK (dotnet).
::   build.cmd                          :: игра в папке Steam по умолчанию
::   build.cmd "D:\Games\Stardeus"      :: игра в другой папке
SETLOCAL
CD /D "%~dp0"

IF "%~1"=="" (
  SET "GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\Stardeus"
) ELSE (
  SET "GAME_DIR=%~1"
)

:: Проверяем, что DLL игры на месте, иначе сборка упадёт с непонятными ошибками
IF NOT EXIST "%GAME_DIR%\Stardeus_Data\Managed\Game.dll" (
  ECHO Game.dll not found in "%GAME_DIR%\Stardeus_Data\Managed"
  EXIT /B 1
)

dotnet build ".vscode\mod.csproj" -c Release -p:StardeusDir="%GAME_DIR%"
EXIT /B %ERRORLEVEL%


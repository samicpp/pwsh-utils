@echo off
setlocal

if "%~1"=="" (
    set "dir=.\build"
) else (
    set "dir=%~1"
)

dotnet build main -c Release || exit /b 1

if not exist "%dir%" (
    mkdir "%dir%"
)

copy /Y ".\dotnet-http\bin\Release\net9.0\dotnet-http.dll" "%dir%"
copy /Y ".\main\bin\Release\net9.0\main.dll" "%dir%"
copy /Y ".\utils.ps1" "%dir%"

endlocal

$dir = $args[0] ?? ".\build"

dotnet build main -c Release

Copy-Item .\dotnet-http\bin\Release\net9.0\dotnet-http.dll $dir
Copy-Item .\main\bin\Release\net9.0\main.dll $dir
Copy-Item .\utils.ps1 $dir


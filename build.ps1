$dir = $args[0] ?? ".\build"

dotnet build main -c Release

New-Item -Path $dir -Type Directory -ErrorAction SilentlyContinue | Out-Null

Copy-Item .\dotnet-http\bin\Release\net9.0\dotnet-http.dll $dir
Copy-Item .\main\bin\Release\net9.0\main.dll $dir
Copy-Item .\utils.ps1 $dir


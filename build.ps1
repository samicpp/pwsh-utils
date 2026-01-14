$dir = $args[0] ?? ".\build"

dotnet build main -c Release

New-Item -Path $dir -Type Directory -ErrorAction SilentlyContinue | Out-Null

$httplib = "$dir\$("dotnet-http_" + [guid]::NewGuid() + ".dll")";
$pwshlib = "$dir\$("main_" + [guid]::NewGuid() + ".dll")";

$utils = "$dir\utils.ps1";


Copy-Item .\dotnet-http\bin\Release\net9.0\dotnet-http.dll $httplib
Copy-Item .\main\bin\Release\net9.0\main.dll $pwshlib

$inject = @"
`$script:httplib = "$httplib";
`$script:pwshlib = "$pwshlib";`n
"@

Set-Content -Path $utils -Value $inject -Encoding UTF8 -Force -NoNewline
Add-Content -Path $utils -Value (Get-Content .\utils.ps1 -Raw)


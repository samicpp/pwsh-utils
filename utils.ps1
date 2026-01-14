$script:self = $PSScriptRoot
$script:httplib = "$self\dotnet-http.dll";
$script:pwshlib = "$self\main.dll";

# $httplib = [IO.File]::ReadAllBytes("$self\dotnet-http.dll");
# $pwshlib = [IO.File]::ReadAllBytes("$self\main.dll");
# [System.Reflection.Assembly]::Load($httplib);
# [System.Reflection.Assembly]::Load($pwshlib);

# Add-Type -Path "$self\dotnet-http.dll"
# Add-Type -Path "$self\main.dll"

$script:htmp = Join-Path $env:TEMP ("dotnet-http_" + [guid]::NewGuid() + ".dll")
$script:mtmp = Join-Path $env:TEMP ("main_" + [guid]::NewGuid() + ".dll")
Copy-Item $script:httplib $script:htmp -Force
Copy-Item $script:pwshlib $script:mtmp -Force

Add-Type -Path $script:htmp
Add-Type -Path $script:mtmp

function get-http1 {
    param ( [int]$port );
    [Samicpp.Pwsh.Utils]::GetHttp1($port);
}

function serve-dir {
    param ( [int]$port );

    # $http = [Samicpp.Http.IDualHttpSocket]$null;
    $hand = [Samicpp.Pwsh.SimpleServe]::new();
    $chan = [Samicpp.Pwsh.Utils]::ServeHttp($port);

    $hand.allowDirs = 1;
    $hand.lookforDirname = 0;
    $hand.startdir = Get-Location;

    # while (1) {
    #     $chan.Reader.WaitToReadAsync([System.Threading.CancellationToken]::None).AsTask().Wait();
    #     $chan.Reader.TryRead([ref]$http);

    #     Start-Job -ScriptBlock {
    #         param([Samicpp.Http.IDualHttpSocket]$sock, [Samicpp.Pwsh.SimpleServe]$hand);
    #         $hand.Entry($sock).Wait();
    #     } -ArgumentList $http,$hand
    # }

    [Samicpp.Pwsh.IHandler]::Serve($hand, $chan).Wait();
}

function start-server{
    param ( [int]$port );
    $chan = [Samicpp.Pwsh.Utils]::ServeHttp($port);

    return $chan;
}

function get-http {
    param ( [System.Threading.Channels.Channel`1[Samicpp.Http.IDualHttpSocket]]$chan )

    $http = [Samicpp.Http.IDualHttpSocket]$null;
    $chan.Reader.WaitToReadAsync([System.Threading.CancellationToken]::None).AsTask().Wait() | Out-Null;
    $chan.Reader.TryRead([ref]$http) | Out-Null;

    return $http;
}
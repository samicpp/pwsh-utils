$script:self = $PSScriptRoot

Add-Type -Path $httplib
Add-Type -Path $pwshlib

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

function clog {
    param ( [byte]$color, [Parameter(ValueFromRemainingArguments=$true)] [string[]] $msg )
    Write-Output "`e[38;5;$($color)m$($msg -Join " ")`e[0m"
}


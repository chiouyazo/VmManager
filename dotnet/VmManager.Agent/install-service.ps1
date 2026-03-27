param(
    [switch]$AddFirewallRule
)

$ServiceName = "VmManager.Agent"
$ApiPort = 18275
$RdpProxyPort = 13389
$ExePath = Join-Path $PSScriptRoot "VmManager.Agent.exe"

if (-not (Test-Path $ExePath)) {
    Write-Error "VmManager.Agent.exe not found at $ExePath"
    exit 1
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists. Stopping and removing..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Creating service '$ServiceName'..."
sc.exe create $ServiceName binPath= "`"$ExePath`"" start= auto | Out-Null
sc.exe description $ServiceName "VmManager Remote Agent for Hyper-V VM management" | Out-Null

if ($AddFirewallRule) {
    foreach ($rule in @("VmManager Agent API", "VmManager Agent RDP")) {
        $existingRule = Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue
        if ($existingRule) {
            Remove-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue
        }
    }
    Write-Host "Adding firewall rules..."
    New-NetFirewallRule -DisplayName "VmManager Agent API" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $ApiPort | Out-Null
    New-NetFirewallRule -DisplayName "VmManager Agent RDP" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $RdpProxyPort | Out-Null
    Write-Host "Firewall rules added for ports $ApiPort (API) and $RdpProxyPort (RDP proxy)."
}

Write-Host "Starting service..."
sc.exe start $ServiceName | Out-Null

Write-Host ""
Write-Host "Service '$ServiceName' installed and started."
Write-Host "  API:       http://localhost:$ApiPort"
Write-Host "  Swagger:   http://localhost:$ApiPort/swagger"
Write-Host "  RDP Proxy: port $RdpProxyPort (token-authenticated)"
if (-not $AddFirewallRule) {
    Write-Host ""
    Write-Host "To allow remote access, run with -AddFirewallRule"
}

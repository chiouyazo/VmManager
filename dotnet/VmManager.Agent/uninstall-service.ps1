$ServiceName = "VmManager.Agent"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' is not installed."
}
else {
    Write-Host "Stopping service '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    Write-Host "Removing service..."
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service '$ServiceName' removed."
}

foreach ($rule in @("VmManager Agent", "VmManager Agent API", "VmManager Agent RDP")) {
    $existingRule = Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue
    if ($existingRule) {
        Write-Host "Removing firewall rule '$rule'..."
        Remove-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue
    }
}
Write-Host "Done."

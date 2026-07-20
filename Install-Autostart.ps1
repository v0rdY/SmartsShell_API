$ErrorActionPreference = 'Stop'

$taskName = 'SmartShell Club Panel'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $projectRoot 'bin\Debug\net8.0-windows\SmartShellCleaner.Avalonia.exe'

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Приложение не найдено: $exe"
}

$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $projectRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$trigger.Delay = 'PT25S'
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description 'Запуск SmartShell Club Panel через 25 секунд после входа в Windows.' `
    -Force | Out-Null

Write-Host "Автозапуск установлен: $taskName"

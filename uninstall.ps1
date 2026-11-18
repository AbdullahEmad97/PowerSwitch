$ErrorActionPreference = "Stop"

$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\PowerSwitch"
$processes = Get-CimInstance Win32_Process -Filter "Name = 'PowerSwitch.App.exe'" -ErrorAction SilentlyContinue
foreach ($process in $processes) {
    Stop-Process -Id $process.ProcessId -Force
}

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $runKey -Name "PowerKeySwitch" -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\PowerKeySwitch" -Recurse -Force -ErrorAction SilentlyContinue

$shortcuts = @(
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\PowerSwitch.lnk"),
    (Join-Path ([Environment]::GetFolderPath("Desktop")) "PowerSwitch.lnk")
)
foreach ($shortcut in $shortcuts) {
    Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath $installDirectory -Recurse -Force -ErrorAction SilentlyContinue

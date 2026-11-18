$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "src\PowerSwitch.App\PowerSwitch.App.csproj"
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\PowerSwitch"
$executable = Join-Path $installDirectory "PowerSwitch.App.exe"

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
dotnet publish $projectPath -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o $installDirectory
if ($LASTEXITCODE -ne 0) {
    throw "PowerKeySwitch publish failed with exit code $LASTEXITCODE."
}

$shell = New-Object -ComObject WScript.Shell

$startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuDirectory "PowerSwitch.lnk"))
$startMenuShortcut.TargetPath = $executable
$startMenuShortcut.WorkingDirectory = $installDirectory
$startMenuShortcut.Description = "PowerSwitch keyboard layout translator"
$startMenuShortcut.Save()

$desktopShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "PowerSwitch.lnk"))
$desktopShortcut.TargetPath = $executable
$desktopShortcut.WorkingDirectory = $installDirectory
$desktopShortcut.Description = "PowerSwitch keyboard layout translator"
$desktopShortcut.Save()

Start-Process -FilePath $executable

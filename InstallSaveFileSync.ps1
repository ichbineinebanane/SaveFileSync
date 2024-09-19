# Run this script as an administrator
# Usage: .\InstallSaveFileSync.ps1 -ExePath "C:\Path\To\SaveFileSync.exe"

param (
    [Parameter(Mandatory=$true)]
    [string]$ExePath
)

$ServiceName = "SaveFileSync"

# Check if the service already exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($service -ne $null) {
    Write-Host "Service $ServiceName already exists. Stopping and removing it..."
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Creating service $ServiceName..."
sc.exe create $ServiceName binPath= $ExePath

# Set the service to auto-start
Write-Host "Configuring service to start automatically..."
sc.exe config $ServiceName start= auto

# Start the service
Write-Host "Starting service $ServiceName..."
Start-Service -Name $ServiceName

# Check the status
$status = (Get-Service -Name $ServiceName).Status
Write-Host "Service $ServiceName is now $status"

Write-Host "Installation complete!"
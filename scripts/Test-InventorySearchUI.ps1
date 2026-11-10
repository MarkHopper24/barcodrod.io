param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$DataDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$SearchText,
    [Parameter(Mandatory)][string]$ItemId
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-InventoryUI.ps1') -AppPid $AppPid -DataDirectory $DataDirectory -OutputDirectory $OutputDirectory -HelpersOnly
Invoke-UI 'NavInventory'
Run-Test 'Submitting a name search keeps matching results without changing inventory' {
    $before = (Get-FileHash -LiteralPath (Join-Path $DataDirectory 'inventory.json')).Hash
    Set-UI 'TextBox' $SearchText; Invoke-UI 'InventorySearch'; Wait-UI 'InventoryStatus' -Value 'matching item'
    if ($before -ne (Get-FileHash -LiteralPath (Join-Path $DataDirectory 'inventory.json')).Hash) { throw 'A name search modified inventory.' }
}
Run-Test 'An exact item lookup selects its details with scan tools open at normal height' {
    Scan $ItemId; Ui @('invoke','InventoryTools','--action','expand','-w',"$mainHwnd") | Out-Null
    Wait-UI 'InventoryDetailText' -Value ('Item ID  ' + $ItemId)
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'inventory-ready.png')) | Out-Null
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json')
Write-Host "Passed: $($results.Count); Failed: 0"

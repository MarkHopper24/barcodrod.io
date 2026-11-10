param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$DataDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$Python,
    [int]$StartAt = 1
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-InventoryUI.ps1') -AppPid $AppPid -DataDirectory $DataDirectory -OutputDirectory $OutputDirectory -StartAt $StartAt -HelpersOnly
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class InventoryReviewBounds { [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags); }'
$code = 'UI-QA-001'
Invoke-UI 'NavInventory'
Ui @('wait-for','InventoryNew','-w',"$mainHwnd",'-p','IsEnabled','--value','True','-t','5000') | Out-Null
Ui @('invoke','InventoryExcelTools','--action','expand','-w',"$mainHwnd") | Out-Null
function Edit-Workbook([string]$Name, [string]$Quantity = 'keep') {
    $path = (Inventory-Data).Excel.Path
    if (!$path -or $path -notlike '*\artifacts\ux-review-*\e2e-results-*\inventory.xlsx') { throw 'Expected the isolated E2E workbook.' }
    $program = @'
import sys
from openpyxl import load_workbook
wb=load_workbook(sys.argv[1]); ws=wb['Inventory']; headers={c.value:c.column for c in ws[1]}
row=next(r for r in range(2,ws.max_row+1) if ws.cell(r,headers['SKU']).value==sys.argv[2])
ws.cell(row,headers['Name']).value=sys.argv[3]
if sys.argv[4]!='keep': ws.cell(row,headers['Quantity']).value=float(sys.argv[4])
wb.save(sys.argv[1])
'@
    & $Python -c $program $path $code $Name $Quantity
    if ($LASTEXITCODE) { throw 'Workbook fixture edit failed.' }
}
Run-Test 'Excel edits merge, same-field conflicts stop, and resolution preserves local data' {
    Edit-Workbook 'Studio item renamed in Excel'
    Invoke-UI 'InventorySync'; Wait-UI 'InventoryStatus' -Value 'synchronized'
    if (((Inventory-Data).Items | Where-Object SKU -eq $code).Name -ne 'Studio item renamed in Excel') { throw 'Excel name edit was not imported.' }
    Scan $code; Invoke-UI 'InventoryStock'; Set-UI 'StockAmount' '1'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Add quantity recorded'
    Edit-Workbook 'Studio item renamed in Excel' '20'
    Invoke-UI 'InventorySync'; Wait-UI 'InventoryStatus' -Value 'Sync stopped'
    if (((Inventory-Data).Items | Where-Object SKU -eq $code).Quantity -ne 13.5) { throw 'A conflict overwrote local quantity.' }
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'excel-conflict.png')) | Out-Null
    Edit-Workbook 'Studio item renamed in Excel' '13.5'
    Invoke-UI 'InventorySync'; Wait-UI 'InventoryStatus' -Value 'synchronized'
}
Run-Test 'A reviewed CSV import adds multiple items and custom fields' {
    $csv = Join-Path $OutputDirectory 'bulk-items.csv'
    "SKU,Name,Quantity,Location,Project`nDEMO-IMPORT-001,Personal reference books,3,Living room,Reading`nDEMO-IMPORT-002,Watercolor paper,12,Art cabinet,Painting" | Set-Content -LiteralPath $csv -Encoding utf8
    Overflow; Invoke-UI 'InventoryImport'; Picker $csv 'Open'; Wait-UI 'TransferCulture'; Invoke-UI 'PrimaryButton'; Wait-UI 'ColumnIncluded0'; Invoke-UI 'PrimaryButton'; Wait-UI 'PrimaryButton' -Value 'Import'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Imported 2 new'
    $imported = @((Inventory-Data).Items | Where-Object SKU -like 'DEMO-IMPORT-*')
    if ($imported.Count -ne 2 -or $imported[1].CustomFields.Project -ne 'Painting') { throw 'Bulk import did not preserve the mapped custom field.' }
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'bulk-import.png')) | Out-Null
}
Run-Test 'Backup restore recovers records and history and disconnects Excel' {
    $backup = Join-Path (Split-Path (Inventory-Data).Excel.Path) 'backup.json'
    $expected = Get-Content -LiteralPath $backup -Raw | ConvertFrom-Json
    Overflow; Invoke-UI 'InventoryRestore'; Picker $backup 'Open'; Wait-UI 'PrimaryButton' -Value 'Restore'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'backup restored'
    $restored = Inventory-Data
    if ($restored.Items.Count -ne $expected.Items.Count -or $restored.Movements.Count -ne $expected.Movements.Count -or $null -ne $restored.Excel) { throw 'Restore did not recover the complete snapshot safely.' }
    Ui @('wait-for','InventorySync','-w',"$mainHwnd",'-p','IsEnabled','--value','False') | Out-Null
}
Run-Test 'Create, print, read from an image, and manage History work through native button actions' {
    [InventoryReviewBounds]::SetWindowPos([IntPtr]$mainHwnd,[IntPtr]::Zero,80,80,1260,900,0x14) | Out-Null
    Invoke-UI 'Create'; Wait-UI 'EncodeButton'; Set-UI 'TxtActivityLog' $code; Invoke-UI 'QR_CODE'; Invoke-UI 'EncodeButton'
    Ui @('wait-for','EncodePrint','-w',"$mainHwnd",'-p','IsEnabled','--value','True','-t','5000') | Out-Null
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'create-code.png')) | Out-Null
    Invoke-UI 'EncodePrint'
    $print = (@(Ui @('list-windows','-a',"$AppPid",'--json') | ConvertFrom-Json) | Where-Object title -match 'Print labels' | Select-Object -First 1).hwnd
    Wait-UI 'LabelSummary' $print '1 label'; Invoke-UI 'LabelClose' $print
    Invoke-UI 'SaveImageButton'; $png = Join-Path $OutputDirectory ('roundtrip-code-' + [guid]::NewGuid().ToString('N') + '.png'); Picker $png 'Save'
    for ($attempt = 0; $attempt -lt 50 -and (!(Test-Path -LiteralPath $png) -or (Get-Item -LiteralPath $png).Length -eq 0); $attempt++) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path -LiteralPath $png) -or (Get-Item -LiteralPath $png).Length -eq 0) { throw 'Barcode image was not saved.' }
    Invoke-UI 'Read'; Wait-UI 'DecodeFromFileButton'; Invoke-UI 'DecodeFromFileButton'; Picker $png 'Open'; Wait-UI 'TxtActivityLog' -Value $code
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'read-roundtrip.png')) | Out-Null
    Invoke-UI 'History'; Wait-UI 'HistoryCountTextBlock'; Invoke-UI 'SelectAllButton'
    Ui @('wait-for','HistoryPrint','-w',"$mainHwnd",'-p','IsEnabled','--value','True','-t','5000') | Out-Null
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'history-selected.png')) | Out-Null
    Invoke-UI 'HistoryPrint'
    $print = (@(Ui @('list-windows','-a',"$AppPid",'--json') | ConvertFrom-Json) | Where-Object title -match 'Print labels' | Select-Object -First 1).hwnd
    Wait-UI 'LabelSummary' $print 'label'; Invoke-UI 'LabelClose' $print
    Invoke-UI 'DeleteFromHistoryButton'; Wait-UI 'PrimaryButton' -Value 'Delete'; Invoke-UI 'CloseButton'; Ui @('wait-for','PrimaryButton','--gone','-w',"$mainHwnd",'-t','4000') | Out-Null
}
Run-Test 'Compact layout keeps controls and recent activity available' {
    Invoke-UI 'NavInventory'; Scan $code
    [InventoryReviewBounds]::SetWindowPos([IntPtr]$mainHwnd,[IntPtr]::Zero,80,80,780,820,0x14) | Out-Null
    Ui @('invoke','InventoryTools','--action','collapse','-w',"$mainHwnd") | Out-Null
    Ui @('invoke','InventoryExcelTools','--action','collapse','-w',"$mainHwnd") | Out-Null
    Wait-UI 'InventorySearch'; Wait-UI 'InventoryFilter'; Wait-UI 'InventorySort'
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'compact-inventory.png')) | Out-Null
    Ui @('invoke','InventoryTools','--action','expand','-w',"$mainHwnd") | Out-Null
    Ui @('invoke','InventoryExcelTools','--action','expand','-w',"$mainHwnd") | Out-Null
    $listBounds = (Ui @('inspect','InventoryList','-w',"$mainHwnd",'-d','1','--json') | ConvertFrom-Json).windows.elements | Select-Object -First 1
    if ($listBounds.height -lt 80) { throw 'Expanded tools hid the item list.' }
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'compact-tools.png')) | Out-Null
    Ui @('invoke','InventoryTools','--action','collapse','-w',"$mainHwnd") | Out-Null
    Ui @('invoke','InventoryExcelTools','--action','collapse','-w',"$mainHwnd") | Out-Null
    Overflow; Invoke-UI 'InventoryDetails'; Wait-UI 'InventoryDetailContent' -Value 'Recent activity'
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'compact-details.png')) | Out-Null
    Invoke-UI 'CloseButton'
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json')
Write-Host "Passed: $($results.Count); Failed: 0"

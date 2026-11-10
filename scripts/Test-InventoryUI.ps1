param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$DataDirectory,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\inventory-ui-results'),
    [int]$StartAt = 1,
    [switch]$KeyboardInput,
    [switch]$HelpersOnly
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$script:workbookPath = Join-Path $OutputDirectory 'inventory.xlsx'
$script:pdfPath = Join-Path $OutputDirectory 'labels.pdf'
$results = [System.Collections.Generic.List[object]]::new()
if ($StartAt -gt 1) {
    $previous = @(Get-Content -LiteralPath (Join-Path $OutputDirectory 'results.json') -Raw | ConvertFrom-Json | Select-Object -First ($StartAt - 1))
    if ($previous.Count -ne $StartAt - 1 -or @($previous | Where-Object status -ne 'PASS').Count) { throw 'Resume requires passing results for every preceding case.' }
    foreach ($entry in $previous) { $results.Add($entry) }
}
$caseIndex = 0
function Ui([string[]]$Arguments) {
    $output = & winapp ui @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw $output }
    return $output
}
function Get-Elements($Nodes) {
    foreach ($node in $Nodes) { $node; if ($node.children) { Get-Elements $node.children } }
}
function Run-Test([string]$Name, [scriptblock]$Action) {
    $script:caseIndex++
    if ($script:caseIndex -lt $StartAt) { return }
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}); Write-Host "PASS $Name" }
    catch {
        $results.Add(@{name=$Name;status='FAIL';detail="$($_.Exception.Message)"})
        $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json')
        throw "FAIL $Name : $($_.Exception.Message)"
    }
}
function Invoke-UI([string]$Id, [long]$Handle = $script:mainHwnd) { Ui @('invoke',$Id,'-w',"$Handle") | Out-Null }
function Value-Target([string]$Id, [long]$Handle) {
    if ($Id -in @('ItemQuantity','ItemReserved','ItemReorderLevel','ItemTargetStock','ItemUnitCost','ItemUnitPrice','StockAmount','LabelCopies','LabelSkip','InventoryScanQuantity')) {
        $tree = Ui @('inspect',$Id,'-w',"$Handle",'-d','3','--json') | ConvertFrom-Json
        $input = @(Get-Elements $tree.windows.elements | Where-Object { $_.type -eq 'Edit' -and $_.automationId -eq 'InputBox' }) | Select-Object -First 1
        if ($input) { return $input.selector }
    }
    return $Id
}
function Set-UI([string]$Id, [string]$Value, [long]$Handle = $script:mainHwnd) {
    Ui @('set-value',$Id,$Value,'-w',"$Handle") | Out-Null
    $target = Value-Target $Id $Handle
    if ($target -ne $Id) {
        $actual = (Ui @('get-value',$target,'-w',"$Handle",'--json') | ConvertFrom-Json).text
        if ([decimal]::Parse($actual,[cultureinfo]::InvariantCulture) -ne [decimal]::Parse($Value,[cultureinfo]::InvariantCulture)) { throw "$Id has $actual rather than $Value" }
    } else { Ui @('wait-for',$Id,'-w',"$Handle",'--value',$Value,'-t','2000') | Out-Null }
}
function Select-Combo([string]$Id, [string]$Value, [long]$Handle = $script:mainHwnd) {
    $choices = switch ($Id) {
        'StockAction' { @('Add quantity','Remove quantity','Set total quantity','Reserve quantity','Release reservation','Use reserved quantity','Move location') }
        'InventoryFilter' { @('Active items','Below minimum','None available','Expired','Expiring in 30 days','Archived','All items') }
        'InventorySort' { @('Name','Item ID','Available quantity','Location','Most recently updated') }
        'InventoryScanAction' { @('Look up','Add quantity','Remove quantity') }
        'LabelPreset' { @('Avery 5160 / 5260 · 30 labels','Avery 5163 / 5263 · 10 labels','Avery L7160 · A4 21 labels','Avery L7159 · A4 24 labels','Thermal · 2 × 1 inch','Thermal · 4 × 2 inch','Thermal · 4 × 6 inch','Custom') }
        'LabelFormat' { @('Item format','CODE_128','CODE_39','QR_CODE','DATA_MATRIX','EAN_13','EAN_8','UPC_A','UPC_E','ITF','PDF_417','AZTEC','CODABAR','MSI','PLESSEY') }
        default { throw "Unknown combo: $Id" }
    }
    $index = [Array]::IndexOf($choices, $Value)
    if ($index -lt 0) { throw "Unknown combo choice: $Value" }
    Invoke-UI $Id $Handle
    if (!$KeyboardInput) {
        $tree = Ui @('inspect',$Id,'-w',"$Handle",'-d','4','--json') | ConvertFrom-Json
        $option = @(Get-Elements $tree.windows.elements | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Value }) | Select-Object -First 1
        if (!$option) { throw "Dropdown $Id has no accessible choice '$Value'." }
        Invoke-UI $option.selector $Handle
        Wait-UI $Id $Handle $Value
        return
    }
    $keys = (@('home') + @('down') * $index + @('enter')) -join ' '
    $foreground = @(Ui @('list-windows','--json') | ConvertFrom-Json) | Where-Object isForeground | Select-Object -First 1
    if ($foreground.processId -ne $AppPid) { throw 'The app lost keyboard focus while selecting a dropdown.' }
    Ui @('send-keys',$keys,'-w',"$($foreground.hwnd)",'--via','send-input') | Out-Null
    Wait-UI $Id $Handle $Value
}
function Wait-UI([string]$Id, [long]$Handle = $script:mainHwnd, [string]$Value = '') {
    if ($Value) { $Id = Value-Target $Id $Handle }
    $arguments = @('wait-for',$Id,'-w',"$Handle",'-t','8000')
    if ($Value) { $arguments += @('--value',$Value,'--contains') }
    Ui $arguments | Out-Null
}
function Inventory-Data { Get-Content -LiteralPath (Join-Path $DataDirectory 'inventory.json') -Raw | ConvertFrom-Json }
function Scan([string]$Code, [string]$Expected = 'Found') {
    if (!$KeyboardInput) {
        $tree = Ui @('inspect','InventorySearch','-w',"$mainHwnd",'-d','3','--json') | ConvertFrom-Json
        $input = @(Get-Elements $tree.windows.elements | Where-Object type -eq 'Edit') | Select-Object -First 1
        if (!$input) { throw 'Search textbox is not accessible.' }
        Set-UI $input.selector $Code
        Invoke-UI 'InventorySearch'
        Wait-UI 'InventoryStatus' -Value $Expected
        return
    }
    Ui @('focus','InventorySearch','-w',"$script:mainHwnd") | Out-Null
    Ui @('send-keys','ctrl+a','--target','InventorySearch','-w',"$script:mainHwnd",'--via','send-input') | Out-Null
    Ui @('send-keys',$Code,'--target','InventorySearch','-w',"$script:mainHwnd",'--via','send-input') | Out-Null
    Ui @('send-keys','enter','--target','InventorySearch','-w',"$script:mainHwnd",'--via','send-input') | Out-Null
    Wait-UI 'InventoryStatus' -Value $Expected
}
function Overflow { Invoke-UI 'MoreButton' }
function Picker([string]$Path, [string]$Action, [long]$Owner = $script:mainHwnd) {
    $picker = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $windows = Ui @('list-windows','--json') | ConvertFrom-Json
        $picker = @($windows | Where-Object { $_.title -match '^(Open|Save As)$' -and $_.ownerHwnd -eq $Owner }) | Select-Object -First 1
        if ($picker) { break }; Start-Sleep -Milliseconds 150
    }
    if (!$picker) { throw 'Native file picker did not appear.' }
    $tree = Ui @('inspect','-w',"$($picker.hwnd)",'--interactive','-d','12','--json') | ConvertFrom-Json
    $elements = @(Get-Elements $tree.windows.elements)
    $filename = @($elements | Where-Object { $_.name -eq 'File name:' -and $_.type -eq 'ComboBox' }) | Select-Object -First 1
    if (!$filename) { throw 'File name control was not found.' }
    $edit = @(Get-Elements $filename.children | Where-Object type -eq 'Edit') | Select-Object -First 1
    if ($edit) { Set-UI $edit.selector $Path $picker.hwnd }
    elseif ($filename.automationId -eq '1148' -and $KeyboardInput) {
        Ui @('send-keys','alt+n ctrl+a','-w',"$($picker.hwnd)",'--via','send-input') | Out-Null
        Ui @('send-keys',$Path,'-w',"$($picker.hwnd)",'--via','send-input') | Out-Null
        Ui @('send-keys','enter','-w',"$($picker.hwnd)",'--via','send-input') | Out-Null
        return
    } else { Set-UI $filename.selector $Path $picker.hwnd }
    $button = @($elements | Where-Object { $_.type -match '^(Button|SplitButton)$' -and $_.name -eq $Action -and $_.automationId -eq '1' }) | Select-Object -First 1
    if (!$button) { throw "Picker has no $Action button." }
    Invoke-UI $button.selector $picker.hwnd
}
$script:mainHwnd = (@(Ui @('list-windows','-a',"$AppPid",'--json') | ConvertFrom-Json) | Where-Object title -eq 'barcodrod.io' | Select-Object -First 1).hwnd
if (!$mainHwnd) { throw 'Main app window not found.' }
if ($HelpersOnly) { return }
if ($KeyboardInput) {
$foregroundAtStart = @(Ui @('list-windows','--json') | ConvertFrom-Json) | Where-Object isForeground | Select-Object -First 1
if ($foregroundAtStart.processName -eq 'LockApp' -or $foregroundAtStart.title -match 'Windows Security|User Account Control') { throw 'Unlock the desktop and finish any foreground security prompt before running keyboard UI checks.' }
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class InventoryTestWindowFocus { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }'
$windowActivation = New-Object -ComObject WScript.Shell
$windowActivation.AppActivate($AppPid) | Out-Null
[InventoryTestWindowFocus]::SetForegroundWindow([IntPtr]$mainHwnd) | Out-Null
if ([InventoryTestWindowFocus]::GetForegroundWindow().ToInt64() -ne $mainHwnd) {
    $tree = Ui @('inspect','-w',"$mainHwnd",'--interactive','--json') | ConvertFrom-Json
    $caption = @(Get-Elements $tree.windows.elements | Where-Object { $_.type -eq 'Button' -and $_.name -match '^(Maximize|Restore)$' -and !$_.isOffscreen }) | Select-Object -First 1
    if ($caption) { Invoke-UI $caption.selector }
}
if ([InventoryTestWindowFocus]::GetForegroundWindow().ToInt64() -ne $mainHwnd) { throw 'Bring the inventory app to the foreground before running keyboard UI checks.' }
}
$code = 'UI-QA-001'
Run-Test 'Fourth tab and empty inventory' {
    Invoke-UI 'NavInventory'; Wait-UI 'InventoryNew'; Wait-UI 'InventorySummary' -Value '0 active'
    Ui @('wait-for','InventoryStock','-w',"$mainHwnd",'-p','IsEnabled','--value','False') | Out-Null
}
Run-Test 'Create an item with only a name and automatically select it' {
    Invoke-UI 'InventoryNew'; Wait-UI 'ItemSKU'
    Set-UI 'ItemName' 'QA studio equipment'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Added'
    $item = (Inventory-Data).Items[0]
    if ($item.SKU -ne 'ITEM-0001' -or $item.Quantity -ne 1 -or $item.UnitCost -ne 0 -or $item.Supplier) { throw 'Name-only creation did not use the expected defaults.' }
    Wait-UI 'InventorySelection' -Value '1 selected'
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'name-only-item.png')) | Out-Null
}
Run-Test 'Edit optional fields without losing fractional stock, leading zeros or custom values' {
    Invoke-UI 'InventoryEdit'; Wait-UI 'ItemSKU'
    Set-UI 'ItemSKU' $code; Set-UI 'ItemName' 'QA studio equipment'; Set-UI 'ItemLocation' 'Home studio'
    Invoke-UI 'ItemCodes'; Set-UI 'ItemBarcode' '000123456789'; Set-UI 'ItemBin' 'Shelf A'
    Invoke-UI 'ItemQuantities'
    Set-UI 'ItemQuantity' '8.5'; Set-UI 'ItemReorderLevel' '10'; Set-UI 'ItemTargetStock' '20'
    Invoke-UI 'ItemValue'; Set-UI 'ItemUnitCost' '2.50'
    Invoke-UI 'ItemCustom'; Invoke-UI 'ItemAddCustom'; Set-UI 'CustomFieldName0' 'Project'; Set-UI 'CustomFieldValue0' 'Creative studio'
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'item-dialog.png')) | Out-Null
    Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Saved'
    $item = @(Inventory-Data).Items | Where-Object SKU -eq $code
    if ($item.Quantity -ne 8.5 -or $item.Barcode -ne '000123456789' -or $item.CustomFields.Project -ne 'Creative studio') { throw 'Stored item differs from the editor.' }
}
Run-Test 'Scan lookup selects record and stock receipt persists with reference' {
    Scan $code; Invoke-UI 'InventoryStock'; Wait-UI 'StockAmount'; Set-UI 'StockAmount' '3'; Set-UI 'StockReference' 'PROJECT-QA-1'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Add quantity recorded'
    $data = Inventory-Data; if ($data.Items[0].Quantity -ne 11.5 -or $data.Movements[-1].Reference -ne 'PROJECT-QA-1') { throw 'Quantity addition was not persisted correctly.' }
}
Run-Test 'Repeated barcode scans add and remove inventory quantities with a reference' {
    Invoke-UI 'InventoryTools'; Select-Combo 'InventoryScanAction' 'Add quantity'; Set-UI 'InventoryScanQuantity' '2'; Set-UI 'InventoryScanReference' 'SCAN-PROJECT-1'
    Scan '000123456789' 'Add quantity:'
    if ((Inventory-Data).Items[0].Quantity -ne 13.5) { throw 'An add scan did not change quantity.' }
    Select-Combo 'InventoryScanAction' 'Remove quantity'; Scan '000123456789' 'Remove quantity:'
    $data = Inventory-Data
    if ($data.Items[0].Quantity -ne 11.5 -or $data.Movements[-1].Reference -ne 'SCAN-PROJECT-1') { throw 'A remove scan did not preserve the quantity/reference.' }
    Select-Combo 'InventoryScanAction' 'Look up'; Invoke-UI 'InventoryTools'
}
Run-Test 'Excess stock issue is rejected without changing stock' {
    Invoke-UI 'InventoryStock'; Wait-UI 'StockAmount'; Select-Combo 'StockAction' 'Remove quantity'; Set-UI 'StockAmount' '100'; Invoke-UI 'PrimaryButton'; Wait-UI 'StockValidation'
    if ((Inventory-Data).Items[0].Quantity -ne 11.5) { throw 'An invalid issue changed stock.' }; Invoke-UI 'CloseButton'
    Ui @('wait-for','StockAmount','-w',"$mainHwnd",'--gone','-t','5000') | Out-Null
    Start-Sleep -Milliseconds 250
}
Run-Test 'Label preview preserves selection after stock update' {
    Invoke-UI 'InventoryPrint'
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        $script:labelHwnd = (@(Ui @('list-windows','-a',"$AppPid",'--json') | ConvertFrom-Json) | Where-Object title -match 'Print labels' | Select-Object -First 1).hwnd
        if ($labelHwnd) { break }; Start-Sleep -Milliseconds 150
    }
    if (!$labelHwnd) { throw 'Label window did not open.' }
    Wait-UI 'LabelSummary' $labelHwnd '1 label'
    Ui @('screenshot','-w',"$labelHwnd",'-o',(Join-Path $OutputDirectory 'labels-sheet.png')) | Out-Null
}
Run-Test 'Native PDF picker and exact-size label PDF export' {
    Invoke-UI 'LabelSavePdf' $labelHwnd
    $script:pdfPath = Join-Path $OutputDirectory 'labels.pdf'; Picker $pdfPath 'Save' $labelHwnd
    Wait-UI 'LabelStatus' $labelHwnd 'Label PDF saved'
    if ((Get-Item -LiteralPath $pdfPath).Length -lt 1000) { throw 'PDF export is empty.' }
}
Run-Test 'Thermal label preset and barcode format update' {
    Select-Combo 'LabelPreset' 'Thermal · 2 × 1 inch' $labelHwnd; Select-Combo 'LabelFormat' 'QR_CODE' $labelHwnd; Set-UI 'LabelCopies' '3' $labelHwnd
    Invoke-UI 'LabelUpdate' $labelHwnd; Wait-UI 'LabelSummary' $labelHwnd '3 label'
    Ui @('screenshot','-w',"$labelHwnd",'-o',(Join-Path $OutputDirectory 'labels-thermal.png')) | Out-Null
    Invoke-UI 'LabelClose' $labelHwnd
}
Run-Test 'Export Excel workbook with configured columns and movement log' {
    Overflow; Invoke-UI 'InventoryExport'; Wait-UI 'ColumnIncluded0'; Invoke-UI 'PrimaryButton'
    $script:workbookPath = Join-Path $OutputDirectory 'inventory.xlsx'; Picker $workbookPath 'Save'; Wait-UI 'InventoryStatus' -Value 'Exported 1 item'
    if ((Get-Item -LiteralPath $workbookPath).Length -lt 1000) { throw 'Excel workbook export is empty.' }
}
Run-Test 'Connect existing Excel workbook and perform two-way sync' {
    Invoke-UI 'InventoryConnect'; Invoke-UI 'PrimaryButton'; Picker $workbookPath 'Open'; Wait-UI 'TransferSheet'; Invoke-UI 'PrimaryButton'; Wait-UI 'ColumnIncluded0'; Invoke-UI 'PrimaryButton'; Wait-UI 'PrimaryButton' -Value 'Connect and sync'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Excel connected'
    if ((Inventory-Data).Excel.Path -ne $workbookPath -or !(Inventory-Data).Excel.LastSyncUtc) { throw 'Excel connection was not saved.' }
}
Run-Test 'Stock save to linked Excel workbook' {
    Scan $code; Invoke-UI 'InventoryStock'; Set-UI 'StockAmount' '1'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Add quantity recorded'
    Invoke-UI 'InventoryExcelTools'; Invoke-UI 'InventorySaveExcel'; Wait-UI 'InventoryStatus' -Value 'Saved directly'
    if (!(Test-Path -LiteralPath ($workbookPath + '.barcodrod.bak'))) { throw 'Workbook backup was not created.' }
}
Run-Test 'Duplicate SKU is rejected while preserving the editor' {
    Invoke-UI 'InventoryNew'; Set-UI 'ItemSKU' $code; Set-UI 'ItemName' 'Duplicate'; Invoke-UI 'PrimaryButton'; Wait-UI 'ItemValidation'; Invoke-UI 'CloseButton'
    if ((Inventory-Data).Items.Count -ne 1) { throw 'Duplicate SKU was persisted.' }
}
Run-Test 'Filters, sort, archive/restore and backup retain records and ledger' {
    Select-Combo 'InventoryFilter' 'Below minimum'; Wait-UI 'InventorySummary' -Value '0 shown'; Select-Combo 'InventoryFilter' 'Active items'; Select-Combo 'InventorySort' 'Item ID'; Scan $code
    Overflow; Invoke-UI 'InventoryArchive'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'archived'; Select-Combo 'InventoryFilter' 'Archived'; Overflow; Invoke-UI 'InventorySelectAll'; Overflow; Invoke-UI 'InventoryArchive'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'restored'; Select-Combo 'InventoryFilter' 'Active items'
    Overflow; Invoke-UI 'InventoryBackup'; Picker (Join-Path $OutputDirectory 'backup.json') 'Save'; Wait-UI 'InventoryStatus' -Value 'backup saved'
    if ((Inventory-Data).Items[0].Archived -or (Inventory-Data).Movements.Count -lt 3) { throw 'Archive/restore lost inventory data.' }
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'inventory-final.png')) | Out-Null
}
Run-Test 'Exact counts start at the current total and reservations, usage and relocation remain consistent' {
    Scan $code; Invoke-UI 'InventoryStock'; Wait-UI 'StockAmount'; Select-Combo 'StockAction' 'Set total quantity'; Wait-UI 'StockAmount' -Value '12.5'
    Ui @('screenshot','-w',"$mainHwnd",'-o',(Join-Path $OutputDirectory 'quantity-preview.png')) | Out-Null
    Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Set total quantity recorded'
    foreach ($operation in @(@('Reserve quantity','3'),@('Release reservation','1'),@('Use reserved quantity','2'),@('Remove quantity','0.5'),@('Add quantity','2.5'))) {
        Invoke-UI 'InventoryStock'; Wait-UI 'StockAmount'; Select-Combo 'StockAction' $operation[0]; Set-UI 'StockAmount' $operation[1]; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value ($operation[0] + ' recorded')
    }
    Invoke-UI 'InventoryStock'; Select-Combo 'StockAction' 'Move location'; Set-UI 'StockDestination' 'Workshop'; Set-UI 'StockDestinationBin' 'Cabinet 2'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Move location recorded'
    $item = (Inventory-Data).Items[0]
    if ($item.Quantity -ne 12.5 -or $item.Reserved -ne 0 -or $item.Location -ne 'Workshop' -or $item.CustomFields.Project -ne 'Creative studio') { throw 'Quantity actions changed an unrelated field or produced an incorrect balance.' }
    Invoke-UI 'InventorySync'; Wait-UI 'InventoryStatus' -Value 'synchronized'
}
Run-Test 'Batch selection survives a sort refresh and prints both items' {
    Invoke-UI 'InventoryNew'; Set-UI 'ItemName' 'QA personal book collection'; Invoke-UI 'PrimaryButton'; Wait-UI 'InventoryStatus' -Value 'Added'
    Overflow; Invoke-UI 'InventorySelectAll'; Wait-UI 'InventorySelection' -Value '2 selected'
    Select-Combo 'InventorySort' 'Name'; Wait-UI 'InventorySelection' -Value '2 selected'
    Ui @('wait-for','InventoryEdit','-w',"$mainHwnd",'-p','IsEnabled','--value','False') | Out-Null
    Invoke-UI 'InventoryPrint'
    $batchWindow = (@(Ui @('list-windows','-a',"$AppPid",'--json') | ConvertFrom-Json) | Where-Object title -match 'Print labels' | Select-Object -First 1).hwnd
    Wait-UI 'LabelSummary' $batchWindow '6 label'
    Ui @('screenshot','-w',"$batchWindow",'-o',(Join-Path $OutputDirectory 'batch-labels.png')) | Out-Null
    Invoke-UI 'LabelClose' $batchWindow
    Overflow; Invoke-UI 'InventoryClearSelection'; Wait-UI 'InventorySelection' -Value 'No items selected'
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json')
$failures = @($results | Where-Object status -eq 'FAIL')
Write-Host "Passed: $($results.Count - $failures.Count); Failed: $($failures.Count)"
if ($failures.Count) { exit 1 }

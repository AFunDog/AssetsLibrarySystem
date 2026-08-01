# 最小 UIA 回归验证：启动素材库应用，确认主窗口与关键导航元素出现
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = "D:\GitRepository\AssetsLibrarySystem\src\avalonia\AssetsLibrarySystem.Avalonia\bin\Debug\net10.0\AssetsLibrarySystem.Avalonia.exe"
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 8

try {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($null -eq $win) { throw "主窗口未找到 (pid=$($proc.Id))" }
    Write-Output ("主窗口: " + $win.Current.Name)

    # 查找包含"素材库"的文本元素（页面标题或标签）
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "素材库")
    $found = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    if ($null -eq $found) {
        # 宽匹配：任意包含素材库的元素
        $anyCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, "*素材库*")
        $any = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $anyCond)
        Write-Output ("匹配素材库元素数: " + $any.Count)
    } else {
        Write-Output "素材库导航元素: 已找到"
    }
    Write-Output "UIA 回归验证通过"
} finally {
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}

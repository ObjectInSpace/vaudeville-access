# Dumps the UI Automation tree of the running Vaudeville window — what NVDA, JAWS and Narrator can see.
# Usage: pwsh -File tools\Dump-UiaTree.ps1 [-Depth 6]
param([int]$Depth = 6)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process Vaudeville -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "Vaudeville is not running."; exit 1 }
if ($proc.MainWindowHandle -eq 0) { Write-Output "Vaudeville has no main window yet."; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
$count = 0

function Describe($el) {
    $c = $el.Current
    $value = ''
    $vp = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) {
        $value = " value='" + $vp.Current.Value + "'"
    }
    $patterns = ($el.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers.Pattern','' }) -join ','
    "{0} '{1}'{2} focusable={3} focused={4} offscreen={5} rect={6} patterns=[{7}] class='{8}' fw='{9}'" -f `
        $c.ControlType.ProgrammaticName.Replace('ControlType.',''), $c.Name, $value, $c.IsKeyboardFocusable,
        $c.HasKeyboardFocus, $c.IsOffscreen, $c.BoundingRectangle, $patterns, $c.ClassName, $c.FrameworkId
}

function Walk($el, $level) {
    if ($level -gt $Depth) { return }
    $script:count++
    Write-Output ((' ' * ($level * 2)) + (Describe $el))
    $child = $walker.GetFirstChild($el)
    while ($child) {
        Walk $child ($level + 1)
        $child = $walker.GetNextSibling($child)
    }
}

Walk $root 0
Write-Output "-- $count element(s) examined."
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
Write-Output ("-- UIA focused element: " + (Describe $focused))

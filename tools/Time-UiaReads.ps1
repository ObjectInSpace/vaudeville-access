# Times UI Automation reads against the running Vaudeville window: one Name read of the first child node every
# -Interval seconds, for -Seconds. Unity answers one UIA request per frame, so this tracks frame cost as seen by a
# screen reader. Compare the timestamps with the mod's [Frames]/[Perf] log lines.
# Usage: pwsh -File tools\Time-UiaReads.ps1 [-Seconds 90] [-Interval 2]
param([int]$Seconds = 90, [double]$Interval = 2)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]

$proc = Get-Process Vaudeville -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "Vaudeville is not running."; exit 1 }
$root = $A::FromHandle($proc.MainWindowHandle)
$sw = [Diagnostics.Stopwatch]::new()
$end = (Get-Date).AddSeconds($Seconds)
$reads = 0

while ((Get-Date) -lt $end) {
    $node = $null
    try {
        $sw.Restart()
        $node = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        $findMs = $sw.ElapsedMilliseconds
        if ($node) {
            $sw.Restart(); [void]$node.Current.Name; $n1 = $sw.ElapsedMilliseconds
            $sw.Restart(); [void]$node.Current.Name; $n2 = $sw.ElapsedMilliseconds
            $reads++
            "{0} find={1}ms name={2}ms name={3}ms" -f (Get-Date).ToString('HH:mm:ss.fff'), $findMs, $n1, $n2
        } else {
            "{0} find={1}ms (no child nodes)" -f (Get-Date).ToString('HH:mm:ss.fff'), $findMs
        }
    } catch {
        "{0} read failed: {1}" -f (Get-Date).ToString('HH:mm:ss.fff'), $_.Exception.Message
    }
    Start-Sleep -Milliseconds ([int]($Interval * 1000))
}
"-- $reads timed read(s)."

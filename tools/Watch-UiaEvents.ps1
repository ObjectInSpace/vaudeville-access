# Logs the UI Automation events the running Vaudeville window raises — the signals a screen reader acts on
# without being asked (focus changes, name/value changes, structure changes). Prints one line per event.
# Usage: pwsh -File tools\Watch-UiaEvents.ps1 [-Seconds 180]
#
# The handlers are compiled C#, not PowerShell script blocks: UIA delivers events on its own threads, where a
# script block has no runspace and fails SILENTLY — an earlier version logged nothing, even for a plain Alt+Tab.
# Positive control: Alt+Tab away from the game and back must log a FOCUS line for the 'Vaudeville' pane.
param([int]$Seconds = 180)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$refs = @(
    [System.Windows.Automation.AutomationElement].Assembly.Location,
    [System.Windows.Automation.ControlType].Assembly.Location,
    [System.Windows.Automation.Automation].Assembly.Location,
    # Naming any reference replaces Add-Type's defaults, so the basics must be listed too.
    'System.Runtime', 'System.Console', 'System.Threading', 'System.Threading.Thread', 'System.ComponentModel.Primitives')

Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.Threading;
using System.Windows.Automation;

public static class UiaEventWatch
{
    static int _pid;
    static int _count;

    static string Stamp() { return DateTime.Now.ToString("HH:mm:ss.fff"); }

    static string NameOf(object sender)
    {
        try
        {
            var el = (AutomationElement)sender;
            return "'" + el.Current.Name + "' (" + el.Current.LocalizedControlType + ")";
        }
        catch (Exception e) { return "<unreadable: " + e.GetType().Name + ">"; }
    }

    static void Log(string text)
    {
        Interlocked.Increment(ref _count);
        Console.WriteLine(Stamp() + " " + text);
    }

    public static int Run(int pid, IntPtr hwnd, int seconds)
    {
        _pid = pid;
        var root = AutomationElement.FromHandle(hwnd);

        Automation.AddAutomationFocusChangedEventHandler((sender, e) =>
        {
            int owner;
            try { owner = ((AutomationElement)sender).Current.ProcessId; } catch { owner = -1; }
            if (owner == _pid) Log("FOCUS -> " + NameOf(sender));
            else Log("focus left the game (pid " + owner + ")");
        });

        Automation.AddAutomationPropertyChangedEventHandler(root, TreeScope.Subtree,
            (sender, e) => Log("PROPERTY " + e.Property.ProgrammaticName + " = '" + e.NewValue + "' on " + NameOf(sender)),
            AutomationElement.NameProperty, ValuePattern.ValueProperty, AutomationElement.HasKeyboardFocusProperty);

        Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree,
            (sender, e) => Log("STRUCTURE " + e.StructureChangeType + " on " + NameOf(sender)));

        foreach (var evt in new[] { AutomationElement.LayoutInvalidatedEvent, AutomationElement.AsyncContentLoadedEvent,
                                    InvokePattern.InvokedEvent, AutomationElement.ToolTipOpenedEvent })
        {
            Automation.AddAutomationEventHandler(evt, root, TreeScope.Subtree,
                (sender, e) => Log("EVENT " + e.EventId.ProgrammaticName + " on " + NameOf(sender)));
        }

        Console.WriteLine(Stamp() + " Listening to Vaudeville (pid " + pid + ") for " + seconds + " s.");
        Thread.Sleep(seconds * 1000);
        Automation.RemoveAllEventHandlers();
        Console.WriteLine(Stamp() + " Done. " + _count + " event(s) received.");
        return _count;
    }
}
'@

$proc = Get-Process Vaudeville -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "Vaudeville is not running."; exit 1 }
[void][UiaEventWatch]::Run($proc.Id, $proc.MainWindowHandle, $Seconds)

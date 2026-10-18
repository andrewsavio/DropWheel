# End-to-end test of the keyboard flow: select a file in Explorer, press Ctrl+Alt+T, choose with the keyboard.
# Types real keys into whatever has focus for about 10 seconds. Run: pwsh e2e/hotkey.ps1 [-Scenario enter|escape|empty]
param(
  [string]$Exe = "$PSScriptRoot\..\app\DropWheel\bin\Debug\net10.0-windows\DropWheel.exe",
  [string]$Out = "$PSScriptRoot\out",
  [ValidateSet("enter", "escape", "empty")][string]$Scenario = "enter"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class K {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, uint f, IntPtr e);
  [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
  public static List<int[]> WindowsOf(uint pid) {
    var l = new List<int[]>();
    EnumWindows((h, _) => { uint p; GetWindowThreadProcessId(h, out p); R r;
      if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out r)) l.Add(new[] { r.L, r.T, r.Rt - r.L, r.B - r.T }); return true; }, IntPtr.Zero);
    return l;
  }
}
"@
[void][K]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))
$scale = [K]::GetDpiForSystem() / 96.0
function Step($m) { Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) }
function CloseExplorer { # Start-Process explorer reuses an open window, along with its old selection, so close ours every time
  $sh = New-Object -ComObject Shell.Application
  foreach ($w in @($sh.Windows())) { try { if ($w.LocationURL -like "*dw-e2e*") { $w.Quit() } } catch { } }
}
function Cleanup { Get-Process DropWheel -ErrorAction SilentlyContinue | Stop-Process -Force; CloseExplorer }
function Fail($m) { Write-Host "FAIL: $m" -ForegroundColor Red; Cleanup; exit 1 }
function Tap($vk) { [K]::keybd_event($vk, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 40; [K]::keybd_event($vk, 0, 2, [IntPtr]::Zero) }
function Shot($name) {
  $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
  $bmp.Save("$Out\$name.png"); $bmp.Dispose()
}
function FindWheel { [K]::WindowsOf($app.Id) | Where-Object { [math]::Abs($_[2] - 420 * $scale) -lt 6 -and [math]::Abs($_[3] - 420 * $scale) -lt 6 } | Select-Object -First 1 }

New-Item -ItemType Directory -Force $Out | Out-Null
CloseExplorer
$dir = Join-Path $env:TEMP "dw-e2e"
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $dir | Out-Null
$bmp = New-Object System.Drawing.Bitmap 64, 48
[System.Drawing.Graphics]::FromImage($bmp).Clear([System.Drawing.Color]::Teal); $bmp.Save("$dir\pic.png"); $bmp.Dispose()

Get-Process DropWheel -ErrorAction SilentlyContinue | Stop-Process -Force
$app = Start-Process $Exe -PassThru
Start-Sleep 3
if ($app.HasExited) { Fail "app exited at startup" }

Start-Process explorer.exe $dir
Start-Sleep 3
$cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ListItem)
$item = $null
for ($i = 0; $i -lt 20 -and -not $item; $i++) {
  foreach ($e in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) { if ($e.Current.Name -like "pic*") { $item = $e; break } }
  if (-not $item) { Start-Sleep -Milliseconds 500 }
}
if (-not $item) { Fail "could not find the file in Explorer" }
# SetFocus on an item selects it in Explorer, so the "nothing selected" case must not touch the item
if ($Scenario -ne "empty") { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus() }
Start-Sleep -Milliseconds 400

[K]::keybd_event(0x11, 0, 0, [IntPtr]::Zero); [K]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)
Tap 0x54
[K]::keybd_event(0x12, 0, 2, [IntPtr]::Zero); [K]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
Start-Sleep -Milliseconds 1500
$wheel = FindWheel
Shot "hotkey-$Scenario"

if ($Scenario -eq "empty") {
  Cleanup
  if ($wheel) { Fail "wheel opened with nothing selected" }
  Write-Host "PASS: nothing selected, no wheel" -ForegroundColor Green; exit 0
}
if (-not $wheel) { Fail "wheel did not open on Ctrl+Alt+T (see $Out\hotkey-$Scenario.png)" }
Step ("wheel at {0},{1}" -f $wheel[0], $wheel[1])

if ($Scenario -eq "escape") {
  Tap 0x1B; Start-Sleep 1
  $gone = -not (FindWheel); Cleanup
  if (-not $gone) { Fail "Esc did not close the wheel" }
  Write-Host "PASS: Esc closes the keyboard wheel" -ForegroundColor Green; exit 0
}

Tap 0x0D   # Enter on the first (focused) slot: JPG
$ok = $false
for ($i = 0; $i -lt 20 -and -not $ok; $i++) { Start-Sleep -Milliseconds 500; $ok = Test-Path "$dir\pic.jpg" }
Shot "hotkey-after"
Cleanup
if ($ok) { Write-Host "PASS: Ctrl+Alt+T then Enter created pic.jpg" -ForegroundColor Green; exit 0 }
Fail "pic.jpg not created. Files: $((Get-ChildItem $dir).Name -join ', ')"

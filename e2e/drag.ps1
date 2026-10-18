# End-to-end test of the real thing: Shift+drag a file in Explorer, drop it on a wheel slot, check the output.
# It moves the real mouse for about 15 seconds. Run: pwsh e2e/drag.ps1 [-Slot JPG] [-Key Shift]
param(
  [string]$Exe = "$PSScriptRoot\..\app\DropWheel\bin\Release\net10.0-windows10.0.19041.0\DropWheel.exe",
  [string]$Out = "$PSScriptRoot\out",
  [string]$Key = "Shift",
  [int]$SlotIndex = 0,        # 0 = top slot of the wheel, clockwise
  [int]$Slots = 8,
  [int]$Group = -1,           # hover this group wedge first (0 = top), then drop on SlotIndex inside it
  [int]$Groups = 4,           # how many wedges the top level shows            # how many slots this file type shows
  [string]$Expect = "pic.jpg",
  [string]$ExpectClipboard = "",   # for actions that copy text instead of writing a file
  [ValidateSet("drop", "nokey", "cancel", "marquee", "still")][string]$Scenario = "drop"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Collections.Generic; using System.Text;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, uint f, IntPtr e);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out P p);
  [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
  public static List<int[]> WindowsOf(uint pid) {
    var l = new List<int[]>();
    EnumWindows((h, _) => { uint p; GetWindowThreadProcessId(h, out p); R r;
      if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out r)) l.Add(new[] { r.L, r.T, r.Rt - r.L, r.B - r.T }); return true; }, IntPtr.Zero);
    return l;
  }
}
"@
[void][W]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))   # per-monitor v2, so coordinates are physical pixels
$scale = [W]::GetDpiForSystem() / 96.0

function CloseExplorer { # Start-Process explorer reuses an open window, along with its old selection, so close ours every time
  $sh = New-Object -ComObject Shell.Application
  foreach ($w in @($sh.Windows())) { try { if ($w.LocationURL -like "*dw-e2e*") { $w.Quit() } } catch { } }
}
function Step($m) { Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) }
function Fail($m) { Write-Host "FAIL: $m" -ForegroundColor Red; Cleanup; exit 1 }
# mouse_event, not SetCursorPos: only real input events reach low-level mouse hooks, like a physical mouse
$prim = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
function MoveTo($x, $y) {
  [W]::mouse_event(0x8001, [int]($x * 65535 / ($prim.Width - 1)), [int]($y * 65535 / ($prim.Height - 1)), 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 40
}
function Shot($name) {
  $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
  $bmp.Save("$Out\$name.png"); $bmp.Dispose()
}
$vk = @{ Shift = 0x10; Ctrl = 0x11; Alt = 0x12 }[$Key]
function Cleanup {
  [W]::keybd_event($vk, 0, 2, [IntPtr]::Zero); [W]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)
  Get-Process DropWheel -ErrorAction SilentlyContinue | Stop-Process -Force
  CloseExplorer
}

New-Item -ItemType Directory -Force $Out | Out-Null
CloseExplorer
$dir = Join-Path $env:TEMP "dw-e2e"
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $dir | Out-Null
$bmp = New-Object System.Drawing.Bitmap 64, 48
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::Teal); $bmp.Save("$dir\pic.png"); $bmp.Dispose()

Get-Process DropWheel -ErrorAction SilentlyContinue | Stop-Process -Force
$app = Start-Process $Exe -PassThru
Start-Sleep 3
if ($app.HasExited) { Fail "app exited at startup (code $($app.ExitCode))" }
Step "app running, pid $($app.Id), dpi scale $scale"

Start-Process explorer.exe $dir
Start-Sleep 3
# bring our Explorer window to the front, or a full-screen app on top gets the drag instead
foreach ($w in @((New-Object -ComObject Shell.Application).Windows())) {
  try { if ($w.LocationURL -like "*dw-e2e*") { $h = [IntPtr]$w.HWND; [void][W]::ShowWindow($h, 9); [void][W]::SetForegroundWindow($h) } } catch { }
}
Start-Sleep -Milliseconds 500
$cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ListItem)
$item = $null
for ($i = 0; $i -lt 20 -and -not $item; $i++) {
  $all = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  foreach ($e in $all) { if ($e.Current.Name -like "pic*") { $item = $e; break } }
  if (-not $item) { Start-Sleep -Milliseconds 500 }
}
if (-not $item) { Fail "could not find the file in the Explorer window" }
$r = $item.Current.BoundingRectangle
$sx = [int]($r.X + $r.Width / 2); $sy = [int]($r.Y + $r.Height / 2)
Step "file item at $sx,$sy"

# press on the file (or, for the marquee case, on empty space), start dragging, then hold the trigger key
if ($Scenario -eq "marquee") { $sx += 500; $sy += 300 }
MoveTo $sx $sy
[W]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero)             # left down
Start-Sleep -Milliseconds 150
for ($i = 1; $i -le 6; $i++) { MoveTo ($sx + 12 * $i) ($sy + 8 * $i) }
if ($Scenario -ne "nokey") { [W]::keybd_event($vk, 0, 0, [IntPtr]::Zero) }   # trigger key down
# "still": the key goes down while the mouse is not moving; the wheel must open anyway
if ($Scenario -ne "still") { for ($i = 1; $i -le 4; $i++) { MoveTo ($sx + 72 + 6 * $i) ($sy + 48 + 4 * $i) } }
Start-Sleep -Milliseconds 1200

function FindWheel { [W]::WindowsOf($app.Id) | Where-Object { [math]::Abs($_[2] - 420 * $scale) -lt 6 -and [math]::Abs($_[3] - 420 * $scale) -lt 6 } | Select-Object -First 1 }
$wheel = FindWheel
Shot "drag-$Scenario-wheel"
$files = { (Get-ChildItem $dir -File).Name -join ", " }

if ($Scenario -in "nokey", "marquee") {
  $seen = [bool]$wheel
  [W]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero); [W]::keybd_event($vk, 0, 2, [IntPtr]::Zero)
  Start-Sleep 2
  $extra = (Get-ChildItem $dir -File | Where-Object Name -ne "pic.png")
  Cleanup
  if ($seen) { Fail "wheel appeared but should not have ($Scenario)" }
  if ($extra) { Fail "unexpected output: $(& $files)" }
  Write-Host "PASS: $Scenario shows no wheel and creates nothing" -ForegroundColor Green; exit 0
}

if (-not $wheel) { Fail "wheel did not appear (see $Out\drag-$Scenario-wheel.png)" }
$cx = $wheel[0] + $wheel[2] / 2; $cy = $wheel[1] + $wheel[3] / 2
$cur = New-Object W+P; [void][W]::GetCursorPos([ref]$cur)
$off = [math]::Sqrt([math]::Pow($cx - $cur.X, 2) + [math]::Pow($cy - $cur.Y, 2))
Step ("WHEEL APPEARED at {0},{1} size {2}x{3}; cursor {4},{5}; centre is {6:N0}px from cursor" -f $wheel[0], $wheel[1], $wheel[2], $wheel[3], $cur.X, $cur.Y, $off)
if ($off -gt 40) { Fail "wheel is not under the cursor ($([int]$off)px away)" }

if ($Scenario -eq "cancel") { $tx = $cx + 400 * $scale; $ty = $cy }
else {
  # wedge labels sit on a ring of radius about 125 dip, first wedge at the top, clockwise
  $ang = $SlotIndex * 2 * [math]::PI / $Slots - [math]::PI / 2
  $tx = $cx + [math]::Cos($ang) * 125 * $scale; $ty = $cy + [math]::Sin($ang) * 125 * $scale
}
if ($Group -ge 0 -and $Scenario -ne "cancel") {
  # open the group by resting on its wedge, as a person would, then go on to the tool inside it
  $ga = $Group * 2 * [math]::PI / $Groups - [math]::PI / 2
  $gx = $cx + [math]::Cos($ga) * 125 * $scale; $gy = $cy + [math]::Sin($ga) * 125 * $scale
  for ($i = 1; $i -le 10; $i++) { MoveTo ($cur.X + ($gx - $cur.X) * $i / 10) ($cur.Y + ($gy - $cur.Y) * $i / 10) }
  Start-Sleep -Milliseconds 300; Shot "drag-$Scenario-top"; Start-Sleep -Milliseconds 500
  # back through the centre so the path does not cross other wedges, then out to the tool
  for ($i = 1; $i -le 6; $i++) { MoveTo ($gx + ($cx - $gx) * $i / 6) ($gy + ($cy - $gy) * $i / 6) }
  $cur.X = [int]$cx; $cur.Y = [int]$cy
}
for ($i = 1; $i -le 12; $i++) { MoveTo ($cur.X + ($tx - $cur.X) * $i / 12) ($cur.Y + ($ty - $cur.Y) * $i / 12) }
Start-Sleep -Milliseconds 300
Shot "drag-$Scenario-hover"
[W]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)               # drop / release
[W]::keybd_event($vk, 0, 2, [IntPtr]::Zero)
Step "released ($Scenario)"

if ($Scenario -eq "cancel") {
  Start-Sleep 3
  $gone = -not (FindWheel); $extra = (Get-ChildItem $dir -File | Where-Object Name -ne "pic.png")
  Shot "drag-cancel-after"; Cleanup
  if (-not $gone) { Fail "wheel still open 3s after release elsewhere" }
  if ($extra) { Fail "cancel still produced output: $(& $files)" }
  Write-Host "PASS: release outside the wheel cancels cleanly" -ForegroundColor Green; exit 0
}

$ok = $false
for ($i = 0; $i -lt 20 -and -not $ok; $i++) {
  Start-Sleep -Milliseconds 500
  $ok = if ($ExpectClipboard) { (Get-Clipboard -Raw) -like "*$ExpectClipboard*" } else { Test-Path (Join-Path $dir $Expect) }
}
Shot "drag-$Scenario-after"
Cleanup
if ($ok) { Write-Host "PASS: $Expect$ExpectClipboard" -ForegroundColor Green; exit 0 }
Fail "expected '$Expect$ExpectClipboard' did not happen. Files: $(& $files)"

# Answers a DropWheel prompt dialog like a person: waits for the window, types into the box, presses OK.
# Used alongside drag.ps1 for tools that ask a question. Run: pwsh e2e/answer.ps1 -Title Resize -Text 32
param([string]$Title = "Resize", [string]$Text = "32", [int]$Seconds = 25)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$byName = New-Object System.Windows.Automation.PropertyCondition ($A::NameProperty), $Title
for ($i = 0; $i -lt $Seconds * 4; $i++) {
  $win = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $byName)
  if ($win) {
    $edit = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition ($A::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Edit)))
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
    $ok = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition ($A::NameProperty), "OK"))
    $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host "answered '$Title' with '$Text'"; exit 0
  }
  Start-Sleep -Milliseconds 250
}
Write-Host "no '$Title' dialog appeared"; exit 1

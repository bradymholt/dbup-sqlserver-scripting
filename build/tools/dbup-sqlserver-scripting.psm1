<# Package Manager Console scripts to support DbUp #>

function Start-DatabaseScript {
  $project = Get-Project
  Write-Host "Building..."
  $dte.Solution.SolutionBuild.BuildProject("Debug", $project.FullName, $true)
  $projectDirectory = Split-Path $project.FullName
  $projectExe = $projectDirectory + "\bin\Debug\" + $project.Name + ".exe" + " --scriptAllDefinitions"
  iex $projectExe
 }

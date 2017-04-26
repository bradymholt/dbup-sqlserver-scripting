<# Package Manager Console scripts to support DbUp #>

function Start-DatabaseScript {
  $project = Get-Project
  Write-Host "Building..."
  $dte.Solution.SolutionBuild.BuildProject("Debug", $project.FullName, $true)
  $projectDirectory = Split-Path $project.FullName
  $projectExe = $projectDirectory + "\bin\Debug\" + $project.Name + ".exe"
  $args = " --scriptAllDefinitions"
  
  & $projectExe $args
 }


function New-InitialScript {
  
    $project = get-project
    $projectDirectory = Split-Path $project.FullName
    #wrap in a job so type isn't locked
    $job = Start-Job -ScriptBlock {        
        $basePath = $args[0]
        $binDirectory = "$basePath\bin\debug"        
        try{
            Add-Type -Path "$binDirectory\DbUp.dll"
            Add-Type -Path "$binDirectory\DbUp.Support.SqlServer.Scripting.dll"
          }
          catch 
          {
            Write-Host -foreground yellow "LoadException";
            $Error | format-list -force
            Write-Host -foreground red $Error[0].Exception.LoaderExceptions;
          }

        $scriptProvider = new-object DbUp.Support.SqlServer.Scripting.DefinitionScriptProvider -ArgumentList "$basePath\Definitions"
        $scriptProvider.GetScriptContent()
    } -ArgumentList $projectDirectory
    Wait-Job $job    
      
  #create scripts folder if it doesn't exist
  $scripts = $project.ProjectItems | ?{ $_.Name.Equals("Scripts") }
  if($scripts -eq $null){
    $project.ProjectItems.AddFolder("Scripts")
  }
  #Create the new script by calling the lib
  $initialScriptPath = "$projectDirectory\Scripts\001-initial.sql"
  Receive-Job $job | New-Item $initialScriptPath -Force -ErrorAction Ignore
  
  #add to project
  $addedItem = $project.ProjectItems.Item("Scripts").ProjectItems.AddFromFileCopy($initialScriptPath)
  #update to embedded resource
  $addedItem.Properties.Item("BuildAction").Value = 3
}
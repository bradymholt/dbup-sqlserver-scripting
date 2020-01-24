#!/usr/bin/env npx jbash

/* Releases dbup-sqlserver-scripting
   This includes: building in release mode, packaging, and publishing on
   NuGet, creating a GitHub release, uploading .nupkg to GitHub release.

   Example:
     release.sh 2.0.0 "Fixed DOW bug causing exception" */

// Halt on an error
set("-e");

// cd to root dir
cd(`${__dirname}/../`);

if (args.length != 2) {
  echo(`
Usage: release.sh version "Release notes..."
Example:
  release.js 2.0.0 "Fixed bug causing exception"`);
  exit(1);
}

if ($(`git status --porcelain`)) {
  echo(`All changes must be committed first.`);
  exit(1);
}

let projectId = "dbup-sqlserver-scripting";
let projectName = "DbUp.Support.SqlServer.Scripting";
let libCsproj = "DbUp.Support.SqlServer.Scripting/DbUp.Support.SqlServer.Scripting.csproj"
let version = args[0];
let notes = args[1];
let preRelease = version.indexOf("-") > -1; // If version contains a '-' character (i.e. 2.0.0-alpha-1) we will consider this a pre-release
let projectCopyright = `Copyright ${(new Date).getFullYear()}`;
let ghRepo = "bradymholt/dbup-sqlserver-scripting";
let buildDir = "tmp";

// Build, pack, and push to NuGet
eval(`dotnet build -c release ${libCsproj}`);
eval(`dotnet pack -c release --no-build ${libCsproj}`);
eval(`dotnet nuget push ${buildDir}/${nupkgFile} -k ${env.NUGET_API_KEY}`);

// Create NuGet package
eval(`mkdir -p ${buildDir}/pack/lib/net35`)
eval(`cp dbup-sqlserver-scripting.nuspec tmp/pack`)
eval(`cp -r tools dbup-sqlserver-scripting.nuspec ${buildDir}/pack/`)
eval(`cp -r ${projectName}/bin/Release/${projectName}.* ${buildDir}/pack/lib/net35`)
eval(`nuget pack ${buildDir}/pack/${projectId}.nuspec -Properties "version=${version};notes=v${version} - ${notes};copyright=${projectCopyright}" -OutputDirectory ${buildDir}`)
let nupkgFile = `${buildDir}/${projectId}.${version}.nupkg`;

// Commit changes to project file
eval(`git commit -am "New release: ${version}"`);

// Create release tag
eval(`git tag -a ${version} -m "${notes}"`);
eval(`git push --follow-tags`);

// Create release on GitHub
let response = $(`curl -f -H "Authorization: token ${env.GITHUB_API_TOKEN}" \
  -d '{"tag_name":"${version}", "name":"${version}","body":"${notes}","prerelease": ${preRelease}}' \
  https://api.github.com/repos/${ghRepo}/releases`
);

let releaseId = JSON.parse(response).id;

// Get the release id and then upload the and upload the .nupkg
eval(`curl -H "Authorization: token ${env.GITHUB_API_TOKEN}" -H "Content-Type: application/octet-stream" \
  --data-binary @"${nupkgFile}" \
  https://uploads.github.com/repos/${ghRepo}/releases/${releaseId}/assets?name=${nupkgFile}`);

eval(`rm -r ${buildDir}`)

echo(`DONE!  Released version ${version} to NuGet and GitHub.`);
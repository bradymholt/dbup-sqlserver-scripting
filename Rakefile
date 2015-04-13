#DbUp.ConsoleScripts
#Example usage: rake release[1.2.0,"Release notes here..."]

require 'albacore'
require 'date'
require 'net/http'
require 'openssl'

project_id = "dbup-sqlserver-scripting"
project_copyright = "Copyright #{DateTime.now.strftime('%Y')}"

task :restore do |t, args|
  nugets_restore :restore do |p|
    p.out = 'packages'
    p.exe = 'tools/NuGet.exe'
  end
end

task :build do |t, args|
  build :build => [:restore] do |b|
    b.sln = 'DbUp.Support.SqlServer.Scripting.sln'
    b.target = ['Clean', 'Rebuild'] 
    b.prop 'Configuration', 'Release'
  end
end

task :package, [:version_number, :notes] => [:build] do |t, args|
	desc "copy lib"
	sh "xcopy /Y DbUp.Support.SqlServer.Scripting\\bin\\Release\\DbUp.Support.SqlServer.Scripting* build\\lib\\net35\\"
	desc "create the nuget package"
	sh "tools\\NuGet.exe pack build\\#{project_id}.nuspec -Properties \"id=#{project_id};version=#{args.version_number};notes=v#{args.version_number} - #{args.notes};copyright=#{project_copyright}\""
end

task :push, [:version_number, :notes] do |t, args|
	sh "tools\\NuGet.exe push #{project_id}.#{args.version_number}.nupkg"
end

task :tag, [:version_number, :notes] do |t, args|
	sh "git tag -a v#{args.version_number} -m \"#{args.notes}\""
	sh "git push --tags"
end

task :release, [:version_number, :notes] => [:package, :push, :tag] do |t, args|
	puts "v#{args.version_number} Released!"
end
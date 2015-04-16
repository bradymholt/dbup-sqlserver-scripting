#DbUp.ConsoleScripts
#Example usage: rake release

require 'albacore'
require 'date'
require 'net/http'
require 'openssl'

project_id = "dbup-sqlserver-scripting"
project_copyright = "Copyright #{DateTime.now.strftime('%Y')}"
project_name = "DbUp.Support.SqlServer.Scripting"
project_copyright = "Copyright #{DateTime.now.strftime('%Y')}"

task :version do |t|
    file_content = File.open('versions.txt', &:readline).split(',') #reads first line only
    @version = file_content[0]
    @notes = file_content[1].strip
    puts "Version: #{@version}, Notes: #{@notes}"
end

task :restore do |t|
  nugets_restore :restore do |p|
    p.out = 'packages'
    p.exe = 'tools/NuGet.exe'
  end
end

task :build => [:version] do |t|
  build :build => [:restore] do |b|
    b.sln = 'DbUp.Support.SqlServer.Scripting.sln'
    b.target = ['Clean', 'Rebuild'] 
    b.prop 'Configuration', 'Release'
  end
end

task :package => [:build] do |t, args|
	desc "copy lib"
	sh "xcopy /Y DbUp.Support.SqlServer.Scripting\\bin\\Release\\DbUp.Support.SqlServer.Scripting* build\\lib\\net35\\"
	desc "create the nuget package"
	sh "tools\\NuGet.exe pack build\\#{project_id}.nuspec -Properties \"id=#{project_id};version=#{@version};notes=v#{@version} - #{@notes};copyright=#{project_copyright}\""
end

task :push do |t, args|
	sh "tools\\NuGet.exe push #{project_id}.#{@version}.nupkg"
end

task :tag do |t, args|
	sh "git tag -a v#{@version} -m \"#{@notes}\""
	sh "git push --tags"
end

task :release => [:package, :push, :tag] do |t, args|
	puts "v#{@version} Released!"
end
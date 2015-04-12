#DbUp.ConsoleScripts
#Example usage: rake release[1.2.0,"Release notes here..."]

require 'albacore'
require 'date'
require 'net/http'
require 'openssl'

project_id = "dbup-sqlserver-scripting"
project_copyright = "Copyright #{DateTime.now.strftime('%Y')}"

task :package, [:version_number, :notes] do |t, args|
	desc "Download nuget.exe"
	uri = URI.parse("https://api.nuget.org/downloads/nuget.exe")
	http = Net::HTTP.new(uri.host, uri.port)
	http.use_ssl = true
	http.verify_mode = OpenSSL::SSL::VERIFY_NONE # read into this
	data = http.get(uri.request_uri)
	open("nuget.exe", "wb") { |file| file.write(data.body) }
	desc "copy lib"
	sh "xcopy /Y DbUp.Support.SqlServer.Scripting\\bin\\Release\\DbUp.Support.SqlServer.Scripting* build\\lib\\net35\\"
	desc "create the nuget package"
	sh "nuget.exe pack build\\#{project_id}.nuspec -Properties \"id=#{project_id};version=#{args.version_number};notes=v#{args.version_number} - #{args.notes};copyright=#{project_copyright}\""
end

task :push, [:version_number, :notes] do |t, args|
	sh "nuget.exe push #{project_id}.#{args.version_number}.nupkg"
end

task :tag, [:version_number, :notes] do |t, args|
	sh "git tag -a v#{args.version_number} -m \"#{args.notes}\""
	sh "git push --tags"
end

task :release, [:version_number, :notes] => [:package, :push, :tag] do |t, args|
	puts "v#{args.version_number} Released!"
end
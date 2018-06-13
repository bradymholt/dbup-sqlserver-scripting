using DbUp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DbUp.Support.SqlServer.Scripting;
using Microsoft.SqlServer.Management.Smo;

class Program
{
    static int Main(string[] args)
    {
        var connectionString = "Server=(localdb)\\v11.0;Integrated Security=true;AttachDbFileName=C:\\Users\\bholt\\DbUpTest.mdf;";

        var engine =
            DeployChanges.To
                .SqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
                .LogToConsole()
                .Build();

        Options options = new Options()
        {
            ScriptingOptions = new ScriptingOptions()
            {
                ScriptBatchTerminator = true, //include 'GO' statements at the end of script batches
            }
        };

      //  args = new string[] { "--scriptAllDefinitions" };

        ScriptingUpgrader upgradeScriptingEngine = new ScriptingUpgrader(connectionString, engine, options);
        var result = upgradeScriptingEngine.Run(args);

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.Error);
            Console.ResetColor();
            return -1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Success!");
        Console.ResetColor();
        return 0;
    }
}
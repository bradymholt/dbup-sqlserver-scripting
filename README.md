# DbUp SQL Server Object Scripting
SQL Server object definition scripting for [DbUp](http://dbup.github.io/).  Extends DbUp to provide SQL Server object definition scripting when running migrations from Visual Studio Package Manager Console.  When a database object changes during a migration, its latest definition will be saved in the project.  This allows you to have all of your database object definitions versioned in your repository and to easily compare the before/after diff of a definition changed by a migration (great for pull request / code reviews!).   

## Demo

[![Demo Video](http://img.youtube.com/vi/2uMsVl_Zk6Y/0.jpg)](https://www.youtube.com/watch?v=2uMsVl_Zk6Y)

## Install
    Install-Package dbup-sqlserver-scripting

## Setup

1. You must use the SqlDatabase upgrade engine in DbUp (i.e. `DeployChanges.To.SqlDatabase(connectionString)`) for definition scripting to work.
2. Rather than calling `.PerformUpgrade` on the UpgradeEngine, you need to instantiate a ScriptingUpgrader object and call `Run` on it instead.

For example:
<pre>
    static int Main(string[] args)
    {
        var connectionString = "Server=(localdb)\\v11.0;Integrated Security=true;AttachDbFileName=C:\\Users\\johndoe\\DbUpTest.mdf;";
        var upgrader =
            DeployChanges.To
                <strong>.SqlDatabase(connectionString)</strong>
                .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
                .LogToConsole()
                .Build();

        //var result = upgrader.PerformUpgrade(); //Don't do this!  Do the following instead.

        <strong>ScriptingUpgrader upgradeScriptingEngine = new ScriptingUpgrader(upgrader);</strong>
        <strong>var result = upgradeScriptingEngine.Run(args);</strong>

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
</pre>
## Usage
This package depends on [dbup-consolescripts](https://github.com/bradyholt/dbup-consolescripts) to provide Package Manager Console usage.

1. Run `New-Migration` from the Package Manager Console
2. Edit the newly created .sql file in the \Migrations folder.
3. Run `Start-Migrations` to migrate the database.  Notice in the output, your changed object definitions have been saved/updated.

By default, the definitions are saved in the \Definitions folder in the root of your project.  **The definitions will not be *included* in the Visual Studio Project so to see them from within Visual Studio you will need to use the *Show All Files* (refresh) option in Solution Explorer**.

If the migration issues a CREATE statement and the definition is not already saved, it will be created.  If a DROP is being performed, the definition file will be *deleted*.  Otherwise, an existing definition file will simply be updated with the latest object definition.

Scripting of definitions will only be performed when running `Start-Migrations` from the Package Manager Console and not when running your DbUp project directly.  `Start-Migrations` passes in `--fromconsole` which triggers scripting.  This is important because when running your DbUp project during a deployment to another environment (or integrated into Octopus Deploy for instance) you do not want object definition scripting to run but only the migrations.

## Object Types
The following SQL Server object types are currently supported:

* Tables
* View
* Stored Procedures
* User Defined Functions
* Synonyms

## Script All Definitions
You can run `Start-DatabaseScript` from the Package Manager Console to script all objects in the database.  If working with an existing database, it is recommended to run this command initially so that all your definition files are saved.  

## Supported SQL Server Editions
All SQL Server editions are supported (including Express / LocalDB) with the exception of SQL Server Compact. 

using DbUp.Builder;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Support.SqlServer.Scripting;
using System.Reflection;

namespace DbUp;

public class ScriptingUpgrader
{
    UpgradeEngine m_engine;
    Options m_options;

    public ScriptingUpgrader(
        string connectionString,
        UpgradeEngine engine,
        Options options
        )
    {
        m_engine = engine;
        m_options = options;
        m_connectionString = connectionString;
    }

    public ScriptingUpgrader(
        string connectionString,
        UpgradeEngine engine
        )
        : this(connectionString, engine, new Options())
    {
    }

    UpgradeConfiguration m_configuration;

    UpgradeConfiguration UpgradeConfiguration
    {
        get
        {
            if (m_configuration == null)
            {
                //configuration field on UpgradeEngine is private so we need to use reflection to break in
                var field = typeof(UpgradeEngine).GetField("configuration",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                m_configuration = (UpgradeConfiguration)field.GetValue(m_engine);
            }

            return m_configuration;
        }
    }

    IUpgradeLog m_log;

    IUpgradeLog Log
    {
        get
        {
            if (m_log == null)
            {
                m_log = this.UpgradeConfiguration.Log;
            }

            return m_log;
        }
    }

    readonly string m_connectionString;

    string ConnectionString
    {
        get
        {
            /* if (m_connectionString == null)
             {
                 var connectionManager = this.UpgradeConfiguration.ConnectionManager;
                 if (connectionManager is SqlConnectionManager)
                 {
                     // this does not work in dbup 3.2.4
                     //connectionString field is private on SqlConnectionManager so we need to use reflection to break in
                     var field = typeof(SqlConnectionManager).GetField("connectionString", BindingFlags.NonPublic | BindingFlags.Instance);
                     m_connectionString = (string)field.GetValue((SqlConnectionManager)connectionManager);
                 }
             }
             */
            return m_connectionString;
        }
    }

    public DatabaseUpgradeResult ScriptAll()
    {
        this.Log.WriteInformation("Scripting all database object definitions...");

        if (this.ConnectionString == null)
        {
            return new DatabaseUpgradeResult(Enumerable.Empty<SqlScript>(), false,
                new Exception("connectionString could not be determined"));
        }

        var scripter = new DbObjectScripter(this.ConnectionString, m_options, this.Log);
        scripter.ScriptAll();

        return new DatabaseUpgradeResult(new List<SqlScript>(), true, null);
    }

    public DatabaseUpgradeResult Run(
        string[] args
        )
    {
        DatabaseUpgradeResult result = null;
        if (args.Any(a => "--scriptAllDefinitions".Equals(a.Trim(), StringComparison.InvariantCultureIgnoreCase)))
        {
            result = ScriptAll();
        }
        else
        {
            var scriptsToExecute = m_engine.GetScriptsToExecute();

            if (args.Any(a => "--whatIf".Equals(a.Trim(), StringComparison.InvariantCultureIgnoreCase)))
            {
                result = new DatabaseUpgradeResult(Enumerable.Empty<SqlScript>(), true, null);

                this.Log.WriteWarning("WHATIF Mode!");
                this.Log.WriteWarning("The following scripts would have been executed:");
                scriptsToExecute.ForEach(r => this.Log.WriteWarning(r.Name));
            }
            else
            {
                var executedScriptsBeforeUpgrade = this.m_engine.GetExecutedScripts();
                result = m_engine.PerformUpgrade();
                if (args.Any(a => "--fromconsole".Equals(a.Trim(), StringComparison.InvariantCultureIgnoreCase)))
                {
                    var scripter = new DbObjectScripter(this.ConnectionString, this.m_options, this.Log);
                    if (result.Successful)
                    {
                        this.Log.WriteInformation("Scripting changed database objects...");
                        var scriptorResult = scripter.ScriptMigrationTargets(scriptsToExecute);
                    }
                    else
                    {
                        this.Log.WriteInformation("Scripting successfully changed database objects...");
                        var executedScriptsAfterUpgrade = this.m_engine.GetExecutedScripts();
                        var appliedScripts = scriptsToExecute.Where(s => executedScriptsAfterUpgrade
                            .Except(executedScriptsBeforeUpgrade)
                            .Contains(s.Name));
                        var scriptorResult = scripter.ScriptMigrationTargets(appliedScripts);
                    }
                }
            }
        }

        return result;
    }
}
using DbUp;
using DbUp.Builder;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Support.SqlServer;
using DbUp.Support.SqlServer.Scripting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DbUp
{
    public class ScriptingUpgrader
    {
        UpgradeEngine m_engine;
        Options m_options;

        public ScriptingUpgrader(UpgradeEngine engine, Options options)
        {
            m_engine = engine;
            m_options = options;
        }

        public ScriptingUpgrader(UpgradeEngine engine)
            : this(engine, new Options())
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
                    var field = typeof(UpgradeEngine).GetField("configuration", BindingFlags.NonPublic | BindingFlags.Instance);
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

        string m_connectionString;
        string ConnectionString
        {
            get
            {
                if (m_connectionString == null)
                {
                    var connectionManager = this.UpgradeConfiguration.ConnectionManager;
                    if (connectionManager is SqlConnectionManager)
                    {
                        //connectionString field is private on SqlConnectionManager so we need to use reflection to break in
                        var field = typeof(SqlConnectionManager).GetField("connectionString", BindingFlags.NonPublic | BindingFlags.Instance);
                        m_connectionString = (string)field.GetValue((SqlConnectionManager)connectionManager);
                    }
                }

                return m_connectionString;
            }
        }

        public DatabaseUpgradeResult ScriptAll()
        {
            this.Log.WriteInformation("Scripting all database object definitions...");

            if (this.ConnectionString == null)
            {
                return new DatabaseUpgradeResult(null, false, new Exception("connectionString could not be determined"));
            }

            var scripter = new DbObjectScripter(this.ConnectionString, m_options, this.Log);
            scripter.ScriptAll();

            return new DatabaseUpgradeResult(new List<SqlScript>(), true, null);
        }

        public DatabaseUpgradeResult Run(string[] args)
        {
            DatabaseUpgradeResult result = null;
            if (args.Any(a => "--scriptAllDefinitions".Equals(a, StringComparison.InvariantCultureIgnoreCase)))
            {
                result = ScriptAll();
            }
            else
            {
                var scriptsToExecute = m_engine.GetScriptsToExecute();

                if (args.Any(a => "--whatIf".Equals(a, StringComparison.InvariantCultureIgnoreCase)))
                {
                    result = new DatabaseUpgradeResult(null, true, null);

                    this.Log.WriteWarning("WHATIF Mode!");
                    this.Log.WriteWarning("The following scripts would have been executed:");
                    scriptsToExecute.ForEach(r => this.Log.WriteWarning(r.Name));
                }
                else
                {
                    result = m_engine.PerformUpgrade();

                    if (result.Successful
                        && args.Any(a => "--fromconsole".Equals(a, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        this.Log.WriteInformation("Scripting changed database objects...");
                        var scripter = new DbObjectScripter(this.ConnectionString, m_options, this.Log);
                        var scriptorResult = scripter.ScriptMigrationTargets(scriptsToExecute);
                    }
                }
            }

            return result;
        }
    }
}

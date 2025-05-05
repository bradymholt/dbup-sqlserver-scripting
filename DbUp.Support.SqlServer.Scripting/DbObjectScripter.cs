using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System.Collections.Specialized;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace DbUp.Support.SqlServer.Scripting;

public class DbObjectScripter
{
    private const string SCRIPTING_OBJECT_REGEX =
        @"((CREATE|ALTER|DROP|CREATE\s*OR\s*ALTER)\s*(TABLE|VIEW|PROCEDURE|PROC|FUNCTION|SYNONYM|TYPE|TRIGGER|)\s*(IF?\s*EXISTS? | \s*)\s*([\w\[\]\-]+)?\.?([\w\[\]\-]*))|(sp_rename{1,1}\s*'([\w\[\]\-]+)?\.?([\w\[\]\-]*)'\s*,\s*'([\w\[\]\-]*)')";

    private const int REGEX_INDEX_ACTION_TYPE = 2;
    private const int REGEX_INDEX_OBJECT_TYPE = 3;
    private const int REGEX_INDEX_SCHEMA_NAME = 5;
    private const int REGEX_INDEX_OBJECT_NAME = 6;
    private const int REGEX_INDEX_OBJECT_RENAME_SCHEMA = 8;
    private const int REGEX_INDEX_OBJECT_RENAME_OLD_NAME = 9;
    private const int REGEX_INDEX_OBJECT_RENAME_NEW_NAME = 10;

    private readonly Regex m_targetDbObjectRegex = new Regex(SCRIPTING_OBJECT_REGEX,
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private Options m_options;
    private string m_definitionDirectory;
    private SqlConnectionStringBuilder m_connectionBuilder;
    private IUpgradeLog m_log;

    public DbObjectScripter(
        string connectionString,
        Options options,
        IUpgradeLog log
        )
    {
        m_connectionBuilder = new SqlConnectionStringBuilder(connectionString);
        m_options = options;
        m_log = log;

        var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        m_definitionDirectory =
            Path.GetFullPath(Path.Combine(exePath, m_options.BaseFolderNameDefinitions)).Normalize();
        EnsureDirectoryExists(m_definitionDirectory);
    }

    public ScripterResult ScriptAll()
    {
        ScripterResult result = new ScripterResult();

        try
        {
            //When scripting all object, do scripting in parallel so it will go faster.
            //We need a DbServerContext for each scripting task since SMO is not thread safe.

            var tablesScriptTask = Task.Run(() =>
            {
                var context = GetDatabaseContext(true);
                ScriptAllTables(context);
            });

            var viewsScriptTask = Task.Run(() =>
            {
                var context = GetDatabaseContext(true);
                ScriptAllViews(context);
            });

            var storedProceduresScriptTask = Task.Run(() =>
            {
                var context = GetDatabaseContext(true);
                ScriptAllStoredProcedures(context);
            });

            var synonymsScriptTask = Task.Run(() =>
            {
                var context = GetDatabaseContext(true);
                ScriptAllSynonyms(context);
            });

            var udtScriptTask = Task.Run(() =>
            {
                var context = GetDatabaseContext(true);
                ScriptAllUserDefinedTypes(context);
            });

            var functionsScriptTask = Task.Run(() =>
            {
                var context = GetDatabaseContext(true);
                this.ScriptAllFunctions(context);
            });

            Task.WaitAll(
                tablesScriptTask,
                viewsScriptTask,
                storedProceduresScriptTask,
                synonymsScriptTask,
                udtScriptTask,
                functionsScriptTask
                );
        }
        catch (Exception ex)
        {
            result.Successful = false;
            result.Error = ex;
        }

        return result;
    }

    public ScripterResult ScriptMigrationTargets(
        IEnumerable<SqlScript> migrationScripts
        )
    {
        List<ScriptObject> scriptObjects =
            new List<ScriptObject>(migrationScripts.SelectMany(this.GetObjectsFromMigrationScripts));
        scriptObjects = CleanupScriptObjects(scriptObjects);

        return ScriptObjects(scriptObjects);
    }

    private IEnumerable<ScriptObject> GetObjectsFromMigrationScripts(
        SqlScript script
        )
    {
        //extract db object target(s) from scripts
        MatchCollection matches = this.m_targetDbObjectRegex.Matches(script.Contents);
        foreach (Match m in matches)
        {
            //if this group is empty, it means the second part of the regex matched (sp_rename)
            if (!string.IsNullOrEmpty(m.Groups[REGEX_INDEX_ACTION_TYPE].Value))
            {
                if (!Enum.TryParse<ObjectTypeEnum>(m.Groups[REGEX_INDEX_OBJECT_TYPE].Value, true, out var type))
                {
                    //We're adjusting for "PROC" vs "PROCEDURE" since we're checking for it in m_targetDbObjectRegex but it's not an enum member ( "PROCEDURE|PROC" )
                    if (m.Groups[REGEX_INDEX_OBJECT_TYPE].Value.Equals("PROC", StringComparison.OrdinalIgnoreCase))
                    {
                        type = ObjectTypeEnum.Procedure;
                    }

                    //else it's ObjectTypeEnum.Undefined
                }

                if (type != ObjectTypeEnum.Undefined)
                {
                    //replace CREATE OR ALTER by CREATE
                    var actionString = m.Groups[REGEX_INDEX_ACTION_TYPE].Value
                        .StartsWith(ObjectActionEnum.Create.ToString(), StringComparison.OrdinalIgnoreCase)
                        ? ObjectActionEnum.Create.ToString()
                        : m.Groups[REGEX_INDEX_ACTION_TYPE].Value;

                    ObjectActionEnum action =
                        (ObjectActionEnum)Enum.Parse(typeof(ObjectActionEnum), actionString, true);
                    var scriptObject = new ScriptObject(type, action);

                    if (string.IsNullOrEmpty(m.Groups[REGEX_INDEX_OBJECT_NAME].Value) &&
                        !string.IsNullOrEmpty(m.Groups[REGEX_INDEX_SCHEMA_NAME].Value))
                    {
                        //no schema specified. in that case, object name is in the schema group
                        scriptObject.ObjectName = RemoveBrackets(m.Groups[REGEX_INDEX_SCHEMA_NAME].Value);
                    }
                    else
                    {
                        scriptObject.ObjectSchema = RemoveBrackets(m.Groups[REGEX_INDEX_SCHEMA_NAME].Value);
                        scriptObject.ObjectName = RemoveBrackets(m.Groups[REGEX_INDEX_OBJECT_NAME].Value);
                    }

                    yield return scriptObject;
                }
            }
            else
            {
                string schemaName;
                string oldObjectName;
                string newObjectName;
                if (string.IsNullOrEmpty(m.Groups[REGEX_INDEX_OBJECT_RENAME_OLD_NAME].Value) &&
                    !string.IsNullOrEmpty(m.Groups[REGEX_INDEX_OBJECT_RENAME_SCHEMA].Value))
                {
                    //no schema specified. in that case, object name is in the schema group
                    schemaName = "dbo";
                    oldObjectName = RemoveBrackets(m.Groups[REGEX_INDEX_OBJECT_RENAME_SCHEMA].Value);
                    newObjectName = RemoveBrackets(m.Groups[REGEX_INDEX_OBJECT_RENAME_OLD_NAME].Value);
                }
                else
                {
                    schemaName = m.Groups[REGEX_INDEX_OBJECT_RENAME_SCHEMA].Value;
                    oldObjectName = RemoveBrackets(m.Groups[REGEX_INDEX_OBJECT_RENAME_OLD_NAME].Value);
                    newObjectName = RemoveBrackets(m.Groups[REGEX_INDEX_OBJECT_RENAME_NEW_NAME].Value);
                }

                var type = GetObjectTypeFromDb(schemaName, newObjectName);

                var scriptObjectDrop = new ScriptObject(type, ObjectActionEnum.Drop);
                scriptObjectDrop.ObjectSchema = schemaName;
                scriptObjectDrop.ObjectName = oldObjectName;

                yield return scriptObjectDrop;

                var scriptObjectCreate = new ScriptObject(type, ObjectActionEnum.Create);
                scriptObjectCreate.ObjectSchema = schemaName;
                scriptObjectCreate.ObjectName = newObjectName;

                yield return scriptObjectCreate;
            }
        }
    }

    private static string RemoveBrackets(
        string @string
        )
    {
        char[] removeCharacters = { '[', ']' };
        return removeCharacters.Aggregate(@string, (
            c1,
            c2
            ) => c1.Replace(c2.ToString(), ""));
    }

    private ObjectTypeEnum GetObjectTypeFromDb(
        string schemaName,
        string objectName
        )
    {
        var context = this.GetDatabaseContext(false);
        if (context.Database.Tables[objectName, schemaName] != null)
        {
            return ObjectTypeEnum.Table;
        }

        if (context.Database.Views[objectName, schemaName] != null)
        {
            return ObjectTypeEnum.View;
        }

        if (context.Database.Synonyms[objectName, schemaName] != null)
        {
            return ObjectTypeEnum.Synonym;
        }

        if (context.Database.StoredProcedures[objectName, schemaName] != null ||
            context.Database.ExtendedStoredProcedures[objectName, schemaName] != null)
        {
            return ObjectTypeEnum.Procedure;
        }

        if (context.Database.UserDefinedFunctions[objectName, schemaName] != null)
        {
            return ObjectTypeEnum.Function;
        }

        if (context.Database.UserDefinedDataTypes[objectName, schemaName] != null ||
            context.Database.UserDefinedTableTypes[objectName, schemaName] != null ||
            context.Database.UserDefinedTypes[objectName, schemaName] != null)
        {
            return ObjectTypeEnum.Type;
        }

        return ObjectTypeEnum.Undefined;
    }

    /// <summary>
    /// Remove duplicates from a list of ScriptObjects to avoid double sripting of files and not run into errors with later droped objects
    /// </summary>
    /// <param name="scriptObjects"></param>
    /// <returns></returns>
    private static List<ScriptObject> CleanupScriptObjects(
        List<ScriptObject> scriptObjects
        )
    {
        var preCleanUpScripts = new List<ScriptObject>(scriptObjects);
        preCleanUpScripts.Reverse();

        var cleanedUpScripts = new List<ScriptObject>();
        foreach (var script in preCleanUpScripts)
        {
            if (!cleanedUpScripts.Any(s => s.FullName.Equals(script.FullName, StringComparison.OrdinalIgnoreCase)))
            {
                cleanedUpScripts.Add(script);
            }
        }

        return cleanedUpScripts;
    }

    public ScripterResult ScriptObjects(
        IEnumerable<ScriptObject> objects
        )
    {
        ScripterResult result = new ScripterResult();

        try
        {
            var context = GetDatabaseContext(false);

            ScriptTables(context, objects.Where(o => o.ObjectType == ObjectTypeEnum.Table));
            ScriptViews(context, objects.Where(o => o.ObjectType == ObjectTypeEnum.View));
            ScriptStoredProcedures(context, objects.Where(o => o.ObjectType == ObjectTypeEnum.Procedure));
            ScriptFunctions(context, objects.Where(o => o.ObjectType == ObjectTypeEnum.Function));
            ScriptSynonyms(context, objects.Where(o => o.ObjectType == ObjectTypeEnum.Synonym));
            ScriptUserDefinedTypes(context, objects.Where(o => o.ObjectType == ObjectTypeEnum.Type));

            WarnUndefinedObjects(objects.Where(o => o.ObjectType == ObjectTypeEnum.Undefined));
        }
        catch (Exception ex)
        {
            result.Successful = false;
            result.Error = ex;
        }

        return result;
    }

    protected void ScriptAllTables(
        DbServerContext context
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Table) == ObjectTypeEnum.Table)
        {
            List<ScriptObject> tables = new List<ScriptObject>();
            foreach (Table table in context.Database.Tables)
            {
                if (!table.IsSystemObject)
                {
                    tables.Add(new ScriptObject(ObjectTypeEnum.Table, ObjectActionEnum.Create)
                    {
                        ObjectName = table.Name,
                        ObjectSchema = table.Schema
                    });
                }
            }

            ScriptTables(context, tables);
        }
    }

    protected void ScriptTables(
        DbServerContext context,
        IEnumerable<ScriptObject> tables
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Table) == ObjectTypeEnum.Table)
        {
            string outputDirectory = Path.Combine(m_definitionDirectory, m_options.FolderNameTables);

            foreach (ScriptObject tableObject in tables)
            {
                if (tableObject.ObjectAction == ObjectActionEnum.Drop)
                {
                    DeleteScript(tableObject, outputDirectory);
                }
                else
                {
                    Table currentTable = context.Database.Tables[tableObject.ObjectName, tableObject.ObjectSchema];

                    ScriptDefinition(tableObject, outputDirectory,
                        new Func<StringCollection>(() => { return currentTable.Script(m_options.ScriptingOptions); }));
                }
            }
        }
    }

    protected void ScriptAllUserDefinedTypes(
        DbServerContext context
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Type) == ObjectTypeEnum.Type)
        {
            List<ScriptObject> types = new List<ScriptObject>();
            foreach (UserDefinedType udt in context.Database.UserDefinedTypes)
            {
                types.Add(new ScriptObject(ObjectTypeEnum.Type, ObjectActionEnum.Create)
                {
                    ObjectName = udt.Name,
                    ObjectSchema = udt.Schema
                });
            }

            foreach (UserDefinedDataType udt in context.Database.UserDefinedDataTypes)
            {
                types.Add(new ScriptObject(ObjectTypeEnum.Type, ObjectActionEnum.Create)
                {
                    ObjectName = udt.Name,
                    ObjectSchema = udt.Schema
                });
            }

            foreach (UserDefinedFunction udt in context.Database.UserDefinedFunctions)
            {
                if (!udt.IsSystemObject)
                {
                    types.Add(new ScriptObject(ObjectTypeEnum.Type, ObjectActionEnum.Create)
                    {
                        ObjectName = udt.Name,
                        ObjectSchema = udt.Schema
                    });
                }
            }

            foreach (UserDefinedTableType udt in context.Database.UserDefinedTableTypes)
            {
                if (udt.IsUserDefined)
                {
                    types.Add(new ScriptObject(ObjectTypeEnum.Type, ObjectActionEnum.Create)
                    {
                        ObjectName = udt.Name,
                        ObjectSchema = udt.Schema
                    });
                }
            }

            ScriptUserDefinedTypes(context, types);
        }
    }

    protected void ScriptUserDefinedTypes(
        DbServerContext context,
        IEnumerable<ScriptObject> udts
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Type) == ObjectTypeEnum.Type)
        {
            string outputDirectory = Path.Combine(m_definitionDirectory, m_options.FolderNameUserDefinedTypes);

            foreach (ScriptObject udtObject in udts)
            {
                if (udtObject.ObjectAction == ObjectActionEnum.Drop)
                {
                    DeleteScript(udtObject, outputDirectory);
                }
                else
                {
                    if (context.Database.UserDefinedTypes.Contains(udtObject.ObjectName, udtObject.ObjectSchema))
                    {
                        var currentType =
                            context.Database.UserDefinedTypes[udtObject.ObjectName, udtObject.ObjectSchema];
                        ScriptDefinition(udtObject, outputDirectory,
                            new Func<StringCollection>(() =>
                            {
                                return currentType.Script(m_options.ScriptingOptions);
                            }));
                    }
                    else if (context.Database.UserDefinedDataTypes.Contains(udtObject.ObjectName,
                                 udtObject.ObjectSchema))
                    {
                        var currentType =
                            context.Database.UserDefinedDataTypes[udtObject.ObjectName, udtObject.ObjectSchema];
                        ScriptDefinition(udtObject, outputDirectory,
                            new Func<StringCollection>(() =>
                            {
                                return currentType.Script(m_options.ScriptingOptions);
                            }));
                    }
                    else if (context.Database.UserDefinedFunctions.Contains(udtObject.ObjectName,
                                 udtObject.ObjectSchema))
                    {
                        var currentType =
                            context.Database.UserDefinedFunctions[udtObject.ObjectName, udtObject.ObjectSchema];
                        ScriptDefinition(udtObject, outputDirectory,
                            new Func<StringCollection>(() =>
                            {
                                return currentType.Script(m_options.ScriptingOptions);
                            }));
                    }
                    else
                    {
                        var currentType =
                            context.Database.UserDefinedTableTypes[udtObject.ObjectName, udtObject.ObjectSchema];
                        ScriptDefinition(udtObject, outputDirectory,
                            new Func<StringCollection>(() =>
                            {
                                return currentType.Script(m_options.ScriptingOptions);
                            }));
                    }
                }
            }
        }
    }

    protected void ScriptAllViews(
        DbServerContext context
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.View) == ObjectTypeEnum.View)
        {
            List<ScriptObject> views = new List<ScriptObject>();
            foreach (View view in context.Database.Views)
            {
                if (!view.IsSystemObject)
                {
                    views.Add(new ScriptObject(ObjectTypeEnum.View, ObjectActionEnum.Create)
                    {
                        ObjectName = view.Name,
                        ObjectSchema = view.Schema
                    });
                }
            }

            ScriptViews(context, views);
        }
    }

    protected void ScriptViews(
        DbServerContext context,
        IEnumerable<ScriptObject> views
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.View) == ObjectTypeEnum.View)
        {
            string outputDirectory = Path.Combine(m_definitionDirectory, m_options.FolderNameViews);

            foreach (ScriptObject viewObject in views)
            {
                if (viewObject.ObjectAction == ObjectActionEnum.Drop)
                {
                    DeleteScript(viewObject, outputDirectory);
                }
                else
                {
                    View currentView = context.Database.Views[viewObject.ObjectName, viewObject.ObjectSchema];
                    ScriptDefinition(viewObject, outputDirectory,
                        new Func<StringCollection>(() => { return currentView.Script(m_options.ScriptingOptions); }));
                }
            }
        }
    }

    protected void ScriptAllStoredProcedures(
        DbServerContext context
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Procedure) == ObjectTypeEnum.Procedure)
        {
            List<ScriptObject> sprocs = new List<ScriptObject>();
            foreach (StoredProcedure sproc in context.Database.StoredProcedures)
            {
                if (!sproc.IsSystemObject)
                {
                    sprocs.Add(new ScriptObject(ObjectTypeEnum.Procedure, ObjectActionEnum.Create)
                    {
                        ObjectName = sproc.Name,
                        ObjectSchema = sproc.Schema
                    });
                }
            }

            ScriptStoredProcedures(context, sprocs);
        }
    }

    protected void ScriptStoredProcedures(
        DbServerContext context,
        IEnumerable<ScriptObject> sprocs
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Procedure) == ObjectTypeEnum.Procedure)
        {
            string outputDirectory = Path.Combine(m_definitionDirectory, m_options.FolderNameProcedures);

            foreach (ScriptObject sprocObject in sprocs)
            {
                if (sprocObject.ObjectAction == ObjectActionEnum.Drop)
                {
                    DeleteScript(sprocObject, outputDirectory);
                }
                else
                {
                    StoredProcedure currentSproc =
                        context.Database.StoredProcedures[sprocObject.ObjectName, sprocObject.ObjectSchema];
                    ScriptDefinition(sprocObject, outputDirectory,
                        new Func<StringCollection>(() => { return currentSproc.Script(m_options.ScriptingOptions); }));
                }
            }
        }
    }

    protected void ScriptAllFunctions(
        DbServerContext context
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Function) == ObjectTypeEnum.Function)
        {
            List<ScriptObject> tables = new List<ScriptObject>();
            foreach (UserDefinedFunction udf in context.Database.UserDefinedFunctions)
            {
                if (!udf.IsSystemObject)
                {
                    tables.Add(new ScriptObject(ObjectTypeEnum.Function, ObjectActionEnum.Create)
                    {
                        ObjectName = udf.Name,
                        ObjectSchema = udf.Schema
                    });
                }
            }

            ScriptFunctions(context, tables);
        }
    }

    protected void ScriptFunctions(
        DbServerContext context,
        IEnumerable<ScriptObject> udfs
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Function) == ObjectTypeEnum.Function)
        {
            string outputDirectory = Path.Combine(m_definitionDirectory, m_options.FolderNameFunctions);

            foreach (ScriptObject udfObject in udfs)
            {
                if (udfObject.ObjectAction == ObjectActionEnum.Drop)
                {
                    DeleteScript(udfObject, outputDirectory);
                }
                else
                {
                    UserDefinedFunction currentUdf =
                        context.Database.UserDefinedFunctions[udfObject.ObjectName, udfObject.ObjectSchema];
                    ScriptDefinition(udfObject, outputDirectory,
                        new Func<StringCollection>(() => { return currentUdf.Script(m_options.ScriptingOptions); }));
                }
            }
        }
    }

    protected void ScriptAllSynonyms(
        DbServerContext context
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Synonym) == ObjectTypeEnum.Synonym)
        {
            List<ScriptObject> synonyms = new List<ScriptObject>();
            foreach (Synonym synonym in context.Database.Synonyms)
            {
                synonyms.Add(new ScriptObject(ObjectTypeEnum.Synonym, ObjectActionEnum.Create)
                {
                    ObjectName = synonym.Name,
                    ObjectSchema = synonym.Schema
                });
            }

            ScriptSynonyms(context, synonyms);
        }
    }

    protected void ScriptSynonyms(
        DbServerContext context,
        IEnumerable<ScriptObject> synonyms
        )
    {
        if ((m_options.ObjectsToInclude & ObjectTypeEnum.Synonym) == ObjectTypeEnum.Synonym)
        {
            string outputDirectory = Path.Combine(m_definitionDirectory, m_options.FolderNameSynonyms);

            foreach (ScriptObject synonymObject in synonyms)
            {
                Synonym curentSynonym = context.Database.Synonyms[synonymObject.ObjectName, synonymObject.ObjectSchema];
                ScriptDefinition(synonymObject, outputDirectory,
                    new Func<StringCollection>(() => { return curentSynonym.Script(m_options.ScriptingOptions); }));
            }
        }
    }

    private DbServerContext GetDatabaseContext(
        bool loadAllFields = false
        )
    {
        DbServerContext context = new DbServerContext();
        SqlConnection connection = new SqlConnection(m_connectionBuilder.ConnectionString);
        ServerConnection serverConnection = new ServerConnection(connection);
        context.Server = new Server(serverConnection);
        context.Database = context.Server.Databases[this.DatabaseName];

        context.Server.SetDefaultInitFields(loadAllFields);

        return context;
    }

    private void ScriptDefinition(
        ScriptObject dbObject,
        string outputDirectory,
        Func<StringCollection> scripter
        )
    {
        try
        {
            StringCollection script = scripter();
            SaveScript(dbObject, script, outputDirectory);
        }
        catch (Exception ex)
        {
            m_log.LogError(string.Format("Error when scripting definition for {0}.{1}: {2}", dbObject.ObjectSchema,
                dbObject.ObjectName, ex.Message));
        }
    }

    private void WarnUndefinedObjects(
        IEnumerable<ScriptObject> dbObjects
        )
    {
        foreach (var dbObject in dbObjects)
        {
            m_log.LogWarning(string.Format(
                "The object {0}.{1} could not be scripted, since the object type was not identifyable. Normally this means, that the object has been dropped in the meantime. If necessary delete the file manually.",
                dbObject.ObjectSchema, dbObject.ObjectName));
        }
    }

    private void SaveScript(
        ScriptObject scriptObject,
        StringCollection script,
        string outputDirectory
        )
    {
        try
        {
            EnsureDirectoryExists(outputDirectory);

            StringBuilder sb = new StringBuilder();
            foreach (string str in script)
            {
                sb.Append(str);
                sb.Append(Environment.NewLine);

                if (this.m_options.ScriptBatchTerminator)
                {
                    sb.Append("GO");
                    sb.Append(Environment.NewLine);
                    sb.Append(Environment.NewLine);
                }
            }

            m_log.LogInformation(string.Format("Saving object definition: {0}",
                Path.Combine(outputDirectory, scriptObject.FileName)));
            File.WriteAllText(Path.Combine(outputDirectory, scriptObject.FileName), sb.ToString());
        }
        catch (Exception ex)
        {
            m_log.LogError(string.Format("Error when saving script file {0}: {1}", scriptObject.FullName,
                ex.Message));
        }
    }

    private void DeleteScript(
        ScriptObject scriptObject,
        string outputDirectory
        )
    {
        try
        {
            string filePath = Path.Combine(outputDirectory, scriptObject.FileName);
            if (File.Exists(filePath))
            {
                m_log.LogInformation(string.Format("Deleting object definition: {0}", filePath));
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            m_log.LogError(string.Format("Error when deleting script file {0}: {1}", scriptObject.FullName,
                ex.Message));
        }
    }

    private void EnsureDirectoryExists(
        string directoryPath
        )
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    private string m_databaseName;

    protected string DatabaseName
    {
        get
        {
            if (m_databaseName == null)
            {
                if (!string.IsNullOrEmpty(m_connectionBuilder.InitialCatalog))
                {
                    m_databaseName = m_connectionBuilder.InitialCatalog;
                }
                else if (!string.IsNullOrEmpty(m_connectionBuilder.AttachDBFilename))
                {
                    var fileInfo = new FileInfo(m_connectionBuilder.AttachDBFilename);
                    m_databaseName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                }

                if (string.IsNullOrEmpty(m_databaseName))
                {
                    throw new InvalidArgumentException(
                        "connectionString must include 'Initial Catalog', 'Database', or 'AttachDBFilename' value!");
                }
            }

            return m_databaseName;
        }
    }
}
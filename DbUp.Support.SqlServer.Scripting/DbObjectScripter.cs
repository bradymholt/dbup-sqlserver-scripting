using DbUp.Engine.Output;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.SqlServer.Management.Dmf;

namespace DbUp.Support.SqlServer.Scripting
{
    public class DbObjectScripter
    {
        private Options m_options;
        private string m_definitionDirectory;
        private SqlConnectionStringBuilder m_connectionBuilder;
        private IUpgradeLog m_log;
	    private DateTime _lastModifiedDate = DateTime.MinValue;

        public DbObjectScripter(string connectionString, Options options, IUpgradeLog log)
        {
            m_connectionBuilder = new SqlConnectionStringBuilder(connectionString);
            m_options = options;
            m_log = log;

            var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            m_definitionDirectory = Path.GetFullPath(Path.Combine(exePath, m_options.BaseFolderNameDefinitions)).Normalize();
            EnsureDirectoryExists(m_definitionDirectory);
        }

	    public void StartWatch()
	    {
		    using (var connection = new SqlConnection(m_connectionBuilder.ConnectionString))
		    {
			    using (var cmd = new SqlCommand("SELECT SYSDATETIME() as ServerDateTime;", connection))
			    {
				    connection.Open();
				    using (var dr = cmd.ExecuteReader())
				    {
					    dr.Read();
					    _lastModifiedDate = dr.GetDateTime(0);
				    }
			    }
		    }
	    }
 
	    public ScripterResult ScriptWatched()
	    {
		    if (_lastModifiedDate == DateTime.MinValue)
		    {
			    throw new InvalidInOperatorException("You need to call \"StartWatch\" before calling \"ScriptWatched\" or use ScriptAll instead!");
		    }
 
		    return ScriptAllModifiedSince(_lastModifiedDate);
	    }
 
	    public ScripterResult ScriptAll()
	    {
		    return ScriptAllModifiedSince(DateTime.MinValue);
	    }

	    protected ScripterResult ScriptAllModifiedSince(DateTime modifiedSince)
	    {
		    var result = new ScripterResult();

		    try
		    {
			    // When scripting all object, do scripting in parallel so it will go faster.
			    // We need a DbServerContext for each scripting task since SMO is not thread safe.
			    var tablesScriptTask = Task.Run(
				    () =>
					    {
						    var context = GetDatabaseContext(true);
						    ScriptAllUserTablesModifiedSince(context, modifiedSince);
					    });

			    var viewsScriptTask = Task.Run(
				    () =>
					    {
						    var context = GetDatabaseContext(true);
						    ScriptAllUserViewsModifiedSince(context, modifiedSince);
					    });

			    var storedProceduresScriptTask = Task.Run(
				    () =>
					    {
						    var context = GetDatabaseContext(true);
						    ScriptAllUserStoredProceduresModifiedSince(context, modifiedSince);
					    });

			    var functionScriptTask = Task.Run(
				    () =>
					    {
						    var context = GetDatabaseContext(true);
						    ScriptAllUserFunctionsModifiedSince(context, modifiedSince);
					    });

			    var synonymsScriptTask = Task.Run(
				    () =>
					    {
						    var context = GetDatabaseContext(true);
						    ScriptAllSynonymsModifiedSince(context, modifiedSince);
					    });

			    var udtScriptTask = Task.Run(
				    () =>
					    {
						    var context = GetDatabaseContext(true);
						    ScriptAllUserDefinedTypesModifiedSince(context, modifiedSince);
					    });

			    Task.WaitAll(
				    tablesScriptTask,
				    viewsScriptTask,
				    storedProceduresScriptTask,
				    functionScriptTask,
				    synonymsScriptTask,
				    udtScriptTask);
		    }
		    catch (Exception ex)
		    {
			    result.Successful = false;
			    result.Error = ex;
		    }

		    return result;
	    }

	    protected void ScriptAllUserTablesModifiedSince(DbServerContext context, DateTime modifiedSince)
	    {
		    if ((m_options.ObjectsToInclude & ObjectTypeEnum.Table) == ObjectTypeEnum.Table)
		    {
			    var tables = new List<ScriptObject>();
			    foreach (Table table in context.Database.Tables)
			    {
				    if (!table.IsSystemObject && table.DateLastModified > modifiedSince)
				    {
					    tables.Add(
						    new ScriptObject(ObjectTypeEnum.Table, ObjectActionEnum.Create)
							    {
								    ObjectName = table.Name,
								    ObjectSchema = table.Schema
							    });
				    }
			    }

			    ScriptTables(context, tables);
		    }
	    }

        protected void ScriptTables(DbServerContext context, IEnumerable<ScriptObject> tables)
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

                        ScriptDefinition(tableObject, outputDirectory, new Func<StringCollection>(() =>
                        {
                            return currentTable.Script(m_options.ScriptingOptions);
                        }));
                    }
                }
            }
        }

		protected void ScriptAllUserDefinedTypesModifiedSince(DbServerContext context, DateTime modifiedSince) {
			if ((m_options.ObjectsToInclude & ObjectTypeEnum.Type) == ObjectTypeEnum.Type) {
				List<ScriptObject> types = new List<ScriptObject>();
				foreach (UserDefinedType udt in context.Database.UserDefinedTypes) {
					types.Add(new ScriptObject(ObjectTypeEnum.Type, ObjectActionEnum.Create) {
						ObjectName = udt.Name,
						ObjectSchema = udt.Schema
					});
				}
				foreach (UserDefinedDataType udt in context.Database.UserDefinedDataTypes) {
					types.Add(new ScriptObject(ObjectTypeEnum.Type, ObjectActionEnum.Create) {
						ObjectName = udt.Name,
						ObjectSchema = udt.Schema
					});
				}
				foreach (UserDefinedFunction udt in context.Database.UserDefinedFunctions) {
					if (!udt.IsSystemObject && udt.DateLastModified > modifiedSince) {
						types.Add(new ScriptObject(ObjectTypeEnum.Type, ObjectActionEnum.Create) {
							ObjectName = udt.Name,
							ObjectSchema = udt.Schema
						});
					}
				}
				foreach (UserDefinedTableType udt in context.Database.UserDefinedTableTypes) {
					if (udt.IsUserDefined && udt.DateLastModified > modifiedSince) {
						types.Add(new ScriptObject(ObjectTypeEnum.Type, ObjectActionEnum.Create) {
							ObjectName = udt.Name,
							ObjectSchema = udt.Schema
						});
					}
				}

				ScriptUserDefinedTypes(context, types);
			}
		}

		protected void ScriptUserDefinedTypes(DbServerContext context, IEnumerable<ScriptObject> udts) {
			if ((m_options.ObjectsToInclude & ObjectTypeEnum.Type) == ObjectTypeEnum.Type) {
				string outputDirectory = Path.Combine(m_definitionDirectory, m_options.FolderNameUserDefinedTypes);

				foreach (ScriptObject udtObject in udts) {
					if (udtObject.ObjectAction == ObjectActionEnum.Drop) {
						DeleteScript(udtObject, outputDirectory);
					} else {
						if (context.Database.UserDefinedTypes.Contains(udtObject.ObjectName, udtObject.ObjectSchema)) {
							var currentType = context.Database.UserDefinedTypes[udtObject.ObjectName, udtObject.ObjectSchema];
							ScriptDefinition(udtObject, outputDirectory, new Func<StringCollection>(() => {
								return currentType.Script(m_options.ScriptingOptions);
							}));
						} else if (context.Database.UserDefinedDataTypes.Contains(udtObject.ObjectName, udtObject.ObjectSchema)) {
							var currentType = context.Database.UserDefinedDataTypes[udtObject.ObjectName, udtObject.ObjectSchema];
							ScriptDefinition(udtObject, outputDirectory, new Func<StringCollection>(() => {
								return currentType.Script(m_options.ScriptingOptions);
							}));
						} else if (context.Database.UserDefinedFunctions.Contains(udtObject.ObjectName, udtObject.ObjectSchema)) {
							var currentType = context.Database.UserDefinedFunctions[udtObject.ObjectName, udtObject.ObjectSchema];
							ScriptDefinition(udtObject, outputDirectory, new Func<StringCollection>(() => {
								return currentType.Script(m_options.ScriptingOptions);
							}));
						} else {
							var currentType = context.Database.UserDefinedTableTypes[udtObject.ObjectName, udtObject.ObjectSchema];
							ScriptDefinition(udtObject, outputDirectory, new Func<StringCollection>(() => {
								return currentType.Script(m_options.ScriptingOptions);
							}));

						}
					}
				}
			}
		}

	    protected void ScriptAllUserViewsModifiedSince(DbServerContext context, DateTime modifiedSince)
	    {
		    if ((m_options.ObjectsToInclude & ObjectTypeEnum.View) == ObjectTypeEnum.View)
		    {
			    var views = new List<ScriptObject>();
			    foreach (View view in context.Database.Views)
			    {
				    if (!view.IsSystemObject && view.DateLastModified > modifiedSince)
				    {
					    views.Add(
						    new ScriptObject(ObjectTypeEnum.View, ObjectActionEnum.Create)
							    {
								    ObjectName = view.Name,
								    ObjectSchema = view.Schema
							    });
				    }
			    }

			    ScriptViews(context, views);
		    }
	    }

		protected void ScriptViews(DbServerContext context, IEnumerable<ScriptObject> views)
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
                        ScriptDefinition(viewObject, outputDirectory, new Func<StringCollection>(() =>
                        {
                            return currentView.Script(m_options.ScriptingOptions);
                        }));
                    }
                }
            }
        }

	    protected void ScriptAllUserStoredProceduresModifiedSince(DbServerContext context, DateTime modifiedSince)
	    {
		    if ((m_options.ObjectsToInclude & ObjectTypeEnum.Procedure) == ObjectTypeEnum.Procedure)
		    {
			    var sprocs = new List<ScriptObject>();
			    foreach (StoredProcedure sproc in context.Database.StoredProcedures)
			    {
				    if (!sproc.IsSystemObject && sproc.DateLastModified > modifiedSince)
				    {
					    sprocs.Add(
						    new ScriptObject(ObjectTypeEnum.Procedure, ObjectActionEnum.Create)
							    {
								    ObjectName = sproc.Name,
								    ObjectSchema = sproc.Schema
							    });
				    }
			    }

			    ScriptStoredProcedures(context, sprocs);
		    }
	    }

        protected void ScriptStoredProcedures(DbServerContext context, IEnumerable<ScriptObject> sprocs)
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
                        StoredProcedure currentSproc = context.Database.StoredProcedures[sprocObject.ObjectName, sprocObject.ObjectSchema];
                        ScriptDefinition(sprocObject, outputDirectory, new Func<StringCollection>(() =>
                        {
                            return currentSproc.Script(m_options.ScriptingOptions);
                        }));
                    }
                }
            }
        }


	    protected void ScriptAllUserFunctionsModifiedSince(DbServerContext context, DateTime modifiedSince)
	    {
		    if ((m_options.ObjectsToInclude & ObjectTypeEnum.Function) == ObjectTypeEnum.Function)
		    {
			    var tables = new List<ScriptObject>();
			    foreach (UserDefinedFunction udf in context.Database.UserDefinedFunctions)
			    {
				    if (!udf.IsSystemObject && udf.DateLastModified > modifiedSince)
				    {
					    tables.Add(
						    new ScriptObject(ObjectTypeEnum.Function, ObjectActionEnum.Create)
							    {
								    ObjectName = udf.Name,
								    ObjectSchema = udf.Schema
							    });
				    }
			    }

			    ScriptFunctions(context, tables);
		    }
	    }

        protected void ScriptFunctions(DbServerContext context, IEnumerable<ScriptObject> udfs)
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
                        UserDefinedFunction currentUdf = context.Database.UserDefinedFunctions[udfObject.ObjectName, udfObject.ObjectSchema];
                        ScriptDefinition(udfObject, outputDirectory, new Func<StringCollection>(() =>
                        {
                            return currentUdf.Script(m_options.ScriptingOptions);
                        }));
                    }
                }
            }
        }

	    protected void ScriptAllSynonymsModifiedSince(DbServerContext context, DateTime modifiedSince)
	    {
		    if ((m_options.ObjectsToInclude & ObjectTypeEnum.Synonym) == ObjectTypeEnum.Synonym)
		    {
			    var synonyms = new List<ScriptObject>();
			    foreach (Synonym synonym in context.Database.Synonyms)
			    {
				    if (synonym.DateLastModified > modifiedSince)
				    {
					    synonyms.Add(
						    new ScriptObject(ObjectTypeEnum.Synonym, ObjectActionEnum.Create)
							    {
								    ObjectName = synonym.Name,
								    ObjectSchema = synonym.Schema
							    });
				    }
			    }

			    ScriptSynonyms(context, synonyms);
		    }
	    }

        protected void ScriptSynonyms(DbServerContext context, IEnumerable<ScriptObject> synonyms)
        {
            if ((m_options.ObjectsToInclude & ObjectTypeEnum.Synonym) == ObjectTypeEnum.Synonym)
            {
                string outputDirectory = Path.Combine(m_definitionDirectory, m_options.FolderNameSynonyms);

                foreach (ScriptObject synonymObject in synonyms)
                {
                    Synonym curentSynonym = context.Database.Synonyms[synonymObject.ObjectName, synonymObject.ObjectSchema];
                    ScriptDefinition(synonymObject, outputDirectory, new Func<StringCollection>(() =>
                        {
                            return curentSynonym.Script(m_options.ScriptingOptions);
                        }));
                }
            }
        }

        private DbServerContext GetDatabaseContext(bool loadAllFields = false)
        {
            DbServerContext context = new DbServerContext();
            SqlConnection connection = new SqlConnection(m_connectionBuilder.ConnectionString);
            ServerConnection serverConnection = new ServerConnection(connection);
            context.Server = new Server(serverConnection);
            context.Database = context.Server.Databases[this.DatabaseName];

            context.Server.SetDefaultInitFields(loadAllFields);

            return context;
        }

        private void ScriptDefinition(ScriptObject dbObject, string outputDirectory, Func<StringCollection> scripter)
        {
            try
            {
                StringCollection script = scripter();
                SaveScript(dbObject, script, outputDirectory);
            }
            catch (Exception ex)
            {
                m_log.WriteError(string.Format("Error when scripting definition for {0}: {1}", dbObject.ObjectName, ex.Message));
            }
        }

        private void SaveScript(ScriptObject scriptObject, StringCollection script, string outputDirectory)
        {
            try
            {
                EnsureDirectoryExists(outputDirectory);

                StringBuilder sb = new StringBuilder();
                foreach (string str in script)
                {
                    sb.Append(str);
                    sb.Append(Environment.NewLine);
                }

                m_log.WriteInformation(string.Format("Saving object definition: {0}", Path.Combine(outputDirectory, scriptObject.FileName)));
                File.WriteAllText(Path.Combine(outputDirectory, scriptObject.FileName), sb.ToString());
            }
            catch (Exception ex)
            {
                m_log.WriteError(string.Format("Error when saving script file {0}: {1}", scriptObject.FullName, ex.Message));
            }
        }

        private void DeleteScript(ScriptObject scriptObject, string outputDirectory)
        {
            try
            {
                string filePath = Path.Combine(outputDirectory, scriptObject.FileName);
                if (File.Exists(filePath))
                {
                    m_log.WriteInformation(string.Format("Deleting object definition: {0}", filePath));
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                m_log.WriteError(string.Format("Error when deleting script file {0}: {1}", scriptObject.FullName, ex.Message));
            }
        }

        private void EnsureDirectoryExists(string directoryPath)
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
                        throw new InvalidArgumentException("connectionString must include 'Initial Catalog', 'Database', or 'AttachDBFilename' value!");
                    }
                }

                return m_databaseName;
            }
        }
    }
}

using Microsoft.SqlServer.Management.Smo;

namespace DbUp.Support.SqlServer.Scripting;

public class Options
{
    //relative to execution path; by default, place definitions in project directory which
    //is two directories up from runtime (i.e. if execution path is bin\Debug, then project directory is ..\..\

    public ScriptingOptions ScriptingOptions { get; set; } = new()
    {
        Default = true,
        ClusteredIndexes = true,
        NonClusteredIndexes = true,
        DriAll = true,
        Triggers = true
    };

    public string BaseFolderNameDefinitions { get; set; } = @"..\..\Definitions";
    public string FolderNameTables { get; set; } = "Tables";
    public string FolderNameViews { get; set; } = "Views";
    public string FolderNameUserDefinedTypes { get; set; } = "UserDefinedTypes";
    public string FolderNameProcedures { get; set; } = "Procedures";
    public string FolderNameFunctions { get; set; } = "Functions";
    public string FolderNameSynonyms { get; set; } = "Synonyms";
    public bool ScriptBatchTerminator { get; set; }

    public ObjectTypeEnum ObjectsToInclude { get; set; } = ObjectTypeEnum.Function
                                                           | ObjectTypeEnum.Procedure
                                                           | ObjectTypeEnum.Synonym
                                                           | ObjectTypeEnum.Table
                                                           | ObjectTypeEnum.View
                                                           | ObjectTypeEnum.Type;
}
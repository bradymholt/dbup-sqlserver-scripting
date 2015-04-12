using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbUp.Support.SqlServer.Scripting
{
    public class Options
    {
        public Options()
        {
            this.BaseFolderNameDefinitions = "Definitions";
            this.FolderNameTables = "Tables";
            this.FolderNameViews = "Views";
            this.FolderNameProcedures = "Procedures";
            this.FolderNameFunctions = "Functions";
            this.FolderNameSynonyms = "Synonyms";

            this.ObjectsToInclude = ObjectTypeEnum.Function
                | ObjectTypeEnum.Procedure
                | ObjectTypeEnum.Synonym
                | ObjectTypeEnum.Table
                | ObjectTypeEnum.View;

            this.ScriptingOptions = new ScriptingOptions()
          {
              Default = true,
              ClusteredIndexes = true,
              NonClusteredIndexes = true,
              DriAll = true
          };
        }

        public ScriptingOptions ScriptingOptions { get; set; }
        public string BaseFolderNameDefinitions { get; set; }
        public string FolderNameTables { get; set; }
        public string FolderNameViews { get; set; }
        public string FolderNameProcedures { get; set; }
        public string FolderNameFunctions { get; set; }
        public string FolderNameSynonyms { get; set; }
        public ObjectTypeEnum ObjectsToInclude { get; set; }
    }
}

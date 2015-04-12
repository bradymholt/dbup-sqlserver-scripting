using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbUp.Support.SqlServer.Scripting
{
    public class ScriptObject
    {
        public ScriptObject(ObjectTypeEnum type, ObjectActionEnum action)
        {
            this.ObjectSchema = "dbo";
            this.ObjectType = type;
            this.ObjectAction = action;
        }

        public ObjectTypeEnum ObjectType { get; set; }
        public ObjectActionEnum ObjectAction { get; set; }
        public string ObjectSchema { get; set; }
        public string ObjectName { get; set; }

        public string FullName
        {
            get
            {
                string name = this.ObjectSchema;
                if (!string.IsNullOrEmpty(name))
                {
                    name += ".";
                }
                name += this.ObjectName;

                return name;
            }
        }

        public string FileName
        {
            get { return this.FullName + ".sql"; }
        }
    }
}

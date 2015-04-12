using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbUp.Support.SqlServer.Scripting
{
    public class ScripterResult
    {
        public ScripterResult()
        {
            this.Successful = true;
        }

        public Exception Error { get; set; }
        public bool Successful { get; set; }
    }
}

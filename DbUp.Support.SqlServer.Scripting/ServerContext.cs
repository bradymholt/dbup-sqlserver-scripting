using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbUp.Support.SqlServer.Scripting
{
    public class DbServerContext
    {
        public Microsoft.SqlServer.Management.Smo.Server Server { get; set; }
        public Microsoft.SqlServer.Management.Smo.Database Database { get; set; }
    }
}

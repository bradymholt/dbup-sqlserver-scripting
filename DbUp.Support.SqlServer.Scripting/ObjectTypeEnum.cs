using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbUp.Support.SqlServer.Scripting
{
    [Flags]
    public enum ObjectTypeEnum : int
    {
        Table = 1,
        View = 2,
        Procedure = 4,
        Function = 8,
        Synonym = 16
    }
}

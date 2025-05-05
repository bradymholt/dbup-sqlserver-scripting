namespace DbUp.Support.SqlServer.Scripting;

public class DbServerContext
{
    public Microsoft.SqlServer.Management.Smo.Server Server { get; set; }
    public Microsoft.SqlServer.Management.Smo.Database Database { get; set; }
}
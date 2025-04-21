namespace DbUp.Support.SqlServer.Scripting;

[Flags]
public enum ObjectTypeEnum : int
{
    Undefined = 0,
    Table = 1,
    View = 2,
    Procedure = 4,
    Function = 8,
    Synonym = 16,
    Type = 32
}
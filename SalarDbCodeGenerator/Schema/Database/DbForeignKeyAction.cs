namespace SalarDbCodeGenerator.Schema.Database
{
    public enum DbForeignKeyAction
    {
        /// <summary>
        /// Not specified in database
        /// </summary>
        NotSet = -1,

        NoAction = 0,
        Cascade,
        SetNull,
        SetDefault,
        Restrict
    }
}
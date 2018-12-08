using SalarDbCodeGenerator.Schema.DbSchemaReaders;
using System.Collections.Generic;

// ====================================
// SalarDbCodeGenerator
// http://SalarDbCodeGenerator.codeplex.com
// Salar Khalilzadeh <salar2k@gmail.com>
// © 2012, All rights reserved
// 2012/07/06
// ====================================
namespace SalarDbCodeGenerator.Schema.Database
{
    public class DbDatabase
    {
        #region properties

        public string DatabaseName { get; set; }
        public List<DbTable> SchemaTables { get; set; }
        public List<DbView> SchemaViews { get; set; }

        public DatabaseProvider Provider { get; set; }

        #endregion properties

        #region public methods

        public DbDatabase()
        {
            SchemaTables = new List<DbTable>();
            SchemaViews = new List<DbView>();
        }

        #endregion public methods
    }
}
﻿using SalarDbCodeGenerator.Schema.Database;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;

// ====================================
// SalarDbCodeGenerator
// http://SalarDbCodeGenerator.codeplex.com
// Salar Khalilzadeh <salar2k@gmail.com>
// © 2012, All rights reserved
// 2012/07/06
// ====================================
namespace SalarDbCodeGenerator.Schema.DbSchemaReaders
{
    public class SQLSchemaEngine : ExSchemaEngine
    {
        private enum SQLServerVersions
        {
            SQL2000Below,
            SQL2000,
            SQL2005,
            SQL2008,
            SQL2008Above
        }

        //  Getting Foreign keys
        //SELECT        OBJECT_NAME(f.constid) AS 'ForeignKey', OBJECT_NAME(f.fkeyid) AS 'FKTable', c1.name AS 'FKColumnName', OBJECT_NAME(f.rkeyid) AS 'PKTable',
        //                 c2.name AS 'PKColumnName'
        //FROM            sysforeignkeys AS f INNER JOIN
        //                 syscolumns AS c1 ON f.fkeyid = c1.id AND f.fkey = c1.colid INNER JOIN
        //                 syscolumns AS c2 ON f.rkeyid = c2.id AND f.rkey = c2.colid
        //ORDER BY OBJECT_NAME(f.rkeyid)

        //SELECT        objtype, objname, name, value
        //FROM            ::fn_listextendedproperty('MS_Description', 'user', 'dbo', 'table', 'tbl_AutoIncrement', 'column', NULL) AS fn_listextendedproperty_1

        //SELECT        *
        //FROM            ::fn_listextendedproperty('MS_Description', 'user', 'dbo', 'table', 'tbl_AutoIncrement', 'column', 'Column11') AS fn_listextendedproperty_1

        #region local variables

        private SqlConnection _dbConnection;

        #endregion local variables

        #region public methods

        public SQLSchemaEngine(DbConnection dbConnection)
        {
            _dbConnection = (SqlConnection)dbConnection;
        }

        /// <summary>
        /// Reads list of tables and views
        /// </summary>
        public override void ReadViewsTablesList(out StringCollection tables, out StringCollection views)
        {
            views = ReadViewsList();
            tables = ReadTablesList(views);
        }

        /// <summary>
        /// Reads full database schema
        /// </summary>
        public override void FillSchema(DbDatabase schemaDatabase)
        {
            if (schemaDatabase == null)
                throw new ArgumentNullException("schemaDatabase", "Database is not specifed.");

            schemaDatabase.SchemaViews = ReadViews();
            schemaDatabase.SchemaTables = ReadTables(schemaDatabase.SchemaViews);
        }

        public override string GetDataProviderClassName(DataProviderClassNames providerClassName)
        {
            switch (providerClassName) {
                case DataProviderClassNames.ClassPrefix:
                    return "Sql";

                case DataProviderClassNames.ClassCommand:
                    return "SqlCommand";

                case DataProviderClassNames.ClassConnection:
                    return "SqlConnection";

                case DataProviderClassNames.ClassDataAdapter:
                    return "SqlDataAdapter";

                case DataProviderClassNames.ClassDataReader:
                    return "SqlDataReader";

                case DataProviderClassNames.ClassParameter:
                    return "SqlParameter";

                case DataProviderClassNames.ClassTransaction:
                    return "SqlTransaction";

                case DataProviderClassNames.ClassNamespace:
                    return "System.Data.SqlClient";

                case DataProviderClassNames.AssemblyReference:
                    return "System.Data";

                case DataProviderClassNames.StoredProcParamPrefix:
                    return "@";

                default:
                    return "";
            }
        }

        public override void Dispose()
        {
            if (_dbConnection != null)
                _dbConnection.Close();
            _dbConnection = null;
        }

        #endregion public methods



        #region private methods

        /// <summary>
        /// Reads tables list. This method requires views list to prevent from conflict!
        /// </summary>
        private StringCollection ReadTablesList(StringCollection viewsList)
        {
            StringCollection result = new StringCollection();
            using (DataTable views = _dbConnection.GetSchema("TABLES")) {
                foreach (DataRow row in views.Rows) {
                    string tableName = row["TABLE_NAME"].ToString();

                    // search in views about this
                    if (viewsList.Contains(tableName))
                        continue;

                    // add to results
                    result.Add(tableName);
                }
            }
            return result;
        }

        /// <summary>
        /// Reads views list
        /// </summary>
        private StringCollection ReadViewsList()
        {
            StringCollection result = new StringCollection();

            using (DataTable views = _dbConnection.GetSchema("Views")) {
                foreach (DataRow row in views.Rows) {
                    string viewName = row["TABLE_NAME"].ToString();

                    // add to results
                    result.Add(viewName);
                }
            }
            return result;
        }

        /// <summary>
        /// Reads tables schema from database
        /// </summary>
        private List<DbTable> ReadTables(List<DbView> viewList)
        {
            List<DbTable> result = new List<DbTable>();

            using (DataTable tables = _dbConnection.GetSchema("TABLES")) {
                foreach (DataRow row in tables.Rows) {
                    string tableName = row["TABLE_NAME"].ToString();
                    string ownerName = row["TABLE_SCHEMA"].ToString();

                    if (!IsTableSelected(tableName))
                        continue;

                    bool jumpToNext = false;

                    // search in views about this
                    for (int i = 0; i < viewList.Count; i++) {
                        if (viewList[i].TableName == tableName) {
                            jumpToNext = true;
                            // we ignore view here
                            break;
                        }
                    }
                    if (jumpToNext) continue;

                    // View columns
                    List<DbColumn> columns = ReadColumns(tableName, ownerName);

                    // read columns description
                    if (ReadColumnsDescription)
                        ApplyColumnsDescription(tableName, ownerName, columns);

                    // new table
                    var dbTable = new DbTable(tableName, columns);
                    dbTable.EscapedTableName = "[" + tableName + "]";

                    // table schema
                    dbTable.OwnerName = ownerName;

                    // add to results
                    result.Add(dbTable);
                }

                // detect the sql server version
                SQLServerVersions sqlVersion = DetectSqlServerVersion(_dbConnection);

                if (ReadConstraintKeys)
                    // The constraint keys will read here
                    ApplyTablesConstraintKeys(result, sqlVersion);

                // it is time to read foreign keys!
                // foreign keys are requested?
                if (ReadTablesForeignKeys)
                    ApplyTablesForeignKeys(result, sqlVersion);

                // Normalize the constraints keys
                NormalizeTablesConstraintKeys(result, sqlVersion);

                if (ReadTablesForeignKeys)
                    ApplyDetectedOneToOneRelation(result);
            }
            return result;
        }

        /// <summary>
        /// Reads views schema from database
        /// </summary>
        private List<DbView> ReadViews()
        {
            List<DbView> result = new List<DbView>();

            using (DataTable views = _dbConnection.GetSchema("Views")) {
                foreach (DataRow row in views.Rows) {
                    string viewName = row["TABLE_NAME"].ToString();
                    string ownerName = row["TABLE_SCHEMA"].ToString();

                    if (!IsViewSelected(viewName))
                        continue;

                    // View columns
                    List<DbColumn> columns = ReadColumns(viewName, ownerName);

                    // read columns description
                    if (ReadColumnsDescription)
                        ApplyColumnsDescription(viewName, ownerName, columns);

                    // new view
                    var view = new DbView(viewName, columns);

                    // view schema
                    view.OwnerName = ownerName;

                    // add to results
                    result.Add(view);
                }
            }
            return result;
        }

        /// <summary>
        /// Read columns schema from database
        /// </summary>
        private List<DbColumn> ReadColumns(string tableName, string ownerName)
        {
            List<DbColumn> result = new List<DbColumn>();

            using (SqlDataAdapter adapter = new SqlDataAdapter(String.Format("SELECT TOP 1 * FROM [{0}].[{1}]", ownerName, tableName), (SqlConnection)_dbConnection)) {
                adapter.MissingSchemaAction = MissingSchemaAction.AddWithKey;

                DataTable columnsSchema;

                // Jjust to avoid stupid "Failed to enable constraints" error!
                using (DataSet tempDs = new DataSet()) {
                    // Avoiding stupid "Failed to enable constraints" error!
                    tempDs.EnforceConstraints = false;

                    using (DataTable columnsList = new DataTable()) {
                        tempDs.Tables.Add(columnsList);

                        // Get from db
                        adapter.Fill(columnsList);

                        // Get schema
                        using (DataTableReader reader = new DataTableReader(columnsList))
                            columnsSchema = reader.GetSchemaTable();
                    }
                }

                // Used to get columns Sql DataType
                using (DataTable columnsDbTypeTable = _dbConnection.GetSchema("COLUMNS")) {
                    // Fetch the rows
                    foreach (DataRow dr in columnsSchema.Rows) {
                        string columnName = dr["ColumnName"].ToString();
                        DbColumn column = new DbColumn(columnName, "[" + columnName + "]") {
                            DataTypeDotNet = dr["DataType"].ToString(),
                            Length = Convert.ToInt32(dr["ColumnSize"]),
                            PrimaryKey = Convert.ToBoolean(dr["IsKey"]),
                            AutoIncrement = Convert.ToBoolean(dr["IsAutoIncrement"]),
                            AllowNull = Convert.ToBoolean(dr["AllowDBNull"]),
                            ColumnOrdinal = Convert.ToInt32(dr["ColumnOrdinal"]),
                        };
                        column.FieldNameSchema = DbSchemaNames.FieldName_RemoveInvalidChars(column.FieldNameSchema);

                        // Primary keys that aren't a valid source of dapper access patterns aren't valid
                        if (column.PrimaryKey) {
                            if (column.DataTypeDotNet == "System.DateTime") {
                                column.PrimaryKey = false;
                            }
                        }

                        // Columns which needs additional fetch
                        var succeed = FillColumnAdditionalInfo(column, columnsDbTypeTable, tableName, columnName);

                        // if additional info readin is failed, don't add it to the list
                        if (succeed) {
                            // Add to result
                            result.Add(column);
                        }
                        else {
                            // TODO: inform the user
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Column additional information
        /// </summary>
        private bool FillColumnAdditionalInfo(DbColumn toSetColumn, DataTable columnsDbTypeTable, string tableName, string columnName)
        {
            DataRow[] addInfo = columnsDbTypeTable.Select(String.Format("TABLE_NAME='{0}' AND COLUMN_NAME='{1}'",
                                              tableName,
                                              columnName));
            if (addInfo.Length == 0) {
                // can't find because of invalid name?? try this
                for (int i = 0; i < columnsDbTypeTable.Rows.Count; i++) {
                    var row = columnsDbTypeTable.Rows[i];
                    if (row["TABLE_NAME"].ToString() == tableName &&
                        row["COLUMN_NAME"].ToString() == columnName) {
                        addInfo = new DataRow[] { row };
                        break;
                    }
                }
            }

            if (addInfo.Length == 0) {
                // sometimes when a column has invalid name for string format this happends!
                // Still no chance
                return false;
            }

            object tempInfo = null;
            DataRow columnInfo = addInfo[0];

            toSetColumn.DataTypeDb = columnInfo["DATA_TYPE"].ToString();
            toSetColumn.Owner = columnInfo["TABLE_SCHEMA"].ToString();

            tempInfo = columnInfo["CHARACTER_MAXIMUM_LENGTH"];
            if (tempInfo != null && tempInfo != DBNull.Value) {
                toSetColumn.DataTypeMaxLength = Convert.ToInt32(tempInfo);
                if (toSetColumn.DataTypeMaxLength == -1) {
                    //toSetColumn.LengthIsMax = true;
                    toSetColumn.DataTypeMaxLength = int.MaxValue;
                }
                else {
                    //toSetColumn.LengthIsMax = false;
                }
            }
            else {
                toSetColumn.DataTypeMaxLength = toSetColumn.Length;
            }

            tempInfo = columnInfo["NUMERIC_SCALE"];
            if (tempInfo != null && tempInfo != DBNull.Value)
                toSetColumn.NumericScale = Convert.ToInt32(tempInfo);
            else toSetColumn.NumericScale = -1;

            tempInfo = columnInfo["NUMERIC_PRECISION"];
            if (tempInfo != null && tempInfo != DBNull.Value)
                toSetColumn.NumericPrecision = Convert.ToInt32(tempInfo);
            else toSetColumn.NumericPrecision = -1;

            return true;
        }

        /// <summary>
        /// Reads specified table foreign keys.
        /// </summary>
        private void ApplyTablesForeignKeys(List<DbTable> tables, SQLServerVersions sqlServer)
        {
            /*
			 * sql 2005 format
			SELECT        CONVERT(SYSNAME, DB_NAME()) AS PKTABLE_QUALIFIER, CONVERT(SYSNAME, SCHEMA_NAME(O1.schema_id)) AS PKTABLE_OWNER, CONVERT(SYSNAME,
									 O1.name) AS PKTABLE_NAME, CONVERT(SYSNAME, C1.name) AS PKCOLUMN_NAME, CONVERT(SYSNAME, DB_NAME()) AS FKTABLE_QUALIFIER,
									 CONVERT(SYSNAME, SCHEMA_NAME(O2.schema_id)) AS FKTABLE_OWNER, CONVERT(SYSNAME, O2.name) AS FKTABLE_NAME, CONVERT(SYSNAME, C2.name)
									 AS FKCOLUMN_NAME, CONVERT(SMALLINT, CASE OBJECTPROPERTY(F.OBJECT_ID, 'CnstIsUpdateCascade') WHEN 1 THEN 0 ELSE 1 END) AS UPDATE_RULE,
									 CONVERT(SMALLINT, CASE OBJECTPROPERTY(F.OBJECT_ID, 'CnstIsDeleteCascade') WHEN 1 THEN 0 ELSE 1 END) AS DELETE_RULE, CONVERT(SYSNAME,
									 OBJECT_NAME(F.object_id)) AS FK_NAME, CONVERT(SYSNAME, I.name) AS PK_NAME, CONVERT(SMALLINT, 7) AS DEFERRABILITY, F.delete_referential_action,
									 F.update_referential_action
			FROM            sys.all_objects AS O1 INNER JOIN
									 sys.foreign_keys AS F INNER JOIN
									 sys.foreign_key_columns AS K ON K.constraint_object_id = F.object_id INNER JOIN
									 sys.indexes AS I ON F.referenced_object_id = I.object_id AND F.key_index_id = I.index_id ON O1.object_id = F.referenced_object_id INNER JOIN
									 sys.all_objects AS O2 ON F.parent_object_id = O2.object_id INNER JOIN
									 sys.all_columns AS C1 ON F.referenced_object_id = C1.object_id AND K.referenced_column_id = C1.column_id INNER JOIN
									 sys.all_columns AS C2 ON F.parent_object_id = C2.object_id AND K.parent_column_id = C2.column_id
			 */

            // GENERAL command format
            string foreignKeySql = @"SELECT OBJECT_NAME(f.constid) AS 'ForeignKey', OBJECT_NAME(f.fkeyid) AS 'FKTable',
										c1.name AS 'FKColumnName', OBJECT_NAME(f.rkeyid) AS 'PKTable', c2.name AS 'PKColumnName' ,
										-1 as update_referential_action, -1 as delete_referential_action
									FROM sysforeignkeys AS f
										INNER JOIN syscolumns AS c1 ON f.fkeyid = c1.id AND f.fkey = c1.colid
										INNER JOIN syscolumns AS c2 ON f.rkeyid = c2.id AND f.rkey = c2.colid
									ORDER BY 'FKTable', c1.colid ";

            // NEW command format
            if (sqlServer > SQLServerVersions.SQL2000) {
                foreignKeySql =
                    @"SELECT CONVERT(SYSNAME, DB_NAME()) AS PKTABLE_QUALIFIER, CONVERT(SYSNAME, SCHEMA_NAME(O1.schema_id)) AS PKTABLE_OWNER, CONVERT(SYSNAME,
						O1.name) AS 'PKTable', CONVERT(SYSNAME, C1.name) AS 'PKColumnName', CONVERT(SYSNAME, DB_NAME()) AS FKTABLE_QUALIFIER,
						CONVERT(SYSNAME, SCHEMA_NAME(O2.schema_id)) AS FKTABLE_OWNER, CONVERT(SYSNAME, O2.name) AS 'FKTable', CONVERT(SYSNAME, C2.name)
						AS 'FKColumnName', CONVERT(SMALLINT, CASE OBJECTPROPERTY(F.OBJECT_ID, 'CnstIsUpdateCascade') WHEN 1 THEN 0 ELSE 1 END) AS UPDATE_RULE,
						CONVERT(SMALLINT, CASE OBJECTPROPERTY(F.OBJECT_ID, 'CnstIsDeleteCascade') WHEN 1 THEN 0 ELSE 1 END) AS DELETE_RULE, CONVERT(SYSNAME,
						OBJECT_NAME(F.object_id)) AS 'ForeignKey', CONVERT(SYSNAME, I.name) AS PK_NAME, CONVERT(SMALLINT, 7) AS DEFERRABILITY, F.delete_referential_action,
						F.update_referential_action
					FROM            sys.all_objects AS O1 INNER JOIN
						sys.foreign_keys AS F INNER JOIN
						sys.foreign_key_columns AS K ON K.constraint_object_id = F.object_id INNER JOIN
						sys.indexes AS I ON F.referenced_object_id = I.object_id AND F.key_index_id = I.index_id ON O1.object_id = F.referenced_object_id INNER JOIN
						sys.all_objects AS O2 ON F.parent_object_id = O2.object_id INNER JOIN
						sys.all_columns AS C1 ON F.referenced_object_id = C1.object_id AND K.referenced_column_id = C1.column_id INNER JOIN
						sys.all_columns AS C2 ON F.parent_object_id = C2.object_id AND K.parent_column_id = C2.column_id
					ORDER BY 'FKTable', C2.column_id";
            }

            try {
                using (var adapter = new SqlDataAdapter(foreignKeySql, (SqlConnection)_dbConnection)) {
                    adapter.MissingSchemaAction = MissingSchemaAction.AddWithKey;

                    // description data table
                    using (var keysData = new DataTable()) {
                        // Just to avoid stupid "Failed to enable constraints" error!
                        using (var tempDs = new DataSet()) {
                            // Avoiding stupid "Failed to enable constraints" error!
                            tempDs.EnforceConstraints = false;
                            tempDs.Tables.Add(keysData);

                            // Get from db
                            adapter.Fill(keysData);
                        }

                        if (keysData.Rows.Count > 0) {
                            foreach (DataRow keysDataRow in keysData.Rows) {
                                var foreignKeyTableName = keysDataRow["FKTable"].ToString();
                                var primaryKeyTableName = keysDataRow["PKTable"].ToString();

                                var foreignKeyTable = FindTable(tables, foreignKeyTableName);
                                var primaryKeyTable = FindTable(tables, primaryKeyTableName);

                                // one-to-many foreign relation will be added
                                if (primaryKeyTable != null) {
                                    // foreign key many end
                                    var manyMultiplicityKey_Local = new DbForeignKey() {
                                        ForeignKeyName = keysDataRow["ForeignKey"].ToString(),
                                        LocalColumnName = keysDataRow["PKColumnName"].ToString(),
                                        ForeignColumnName = keysDataRow["FKColumnName"].ToString(),
                                        ForeignTableName = keysDataRow["FKTable"].ToString(),
                                        Multiplicity = DbForeignKey.ForeignKeyMultiplicity.ManyToOne
                                    };
                                    // check if it is already there
                                    if (primaryKeyTable.ForeignKeys.Exists(
                                        x =>
                                        x.Multiplicity == DbForeignKey.ForeignKeyMultiplicity.ManyToOne &&
                                        x.ForeignKeyName == manyMultiplicityKey_Local.ForeignKeyName))
                                        continue;

                                    manyMultiplicityKey_Local.UpdateAction =
                                        ConvertSqlServerForeignKeyAction(Convert.ToInt32(keysDataRow["update_referential_action"].ToString()));
                                    manyMultiplicityKey_Local.DeleteAction =
                                        ConvertSqlServerForeignKeyAction(Convert.ToInt32(keysDataRow["delete_referential_action"].ToString()));

                                    // to the list
                                    primaryKeyTable.ForeignKeys.Add(manyMultiplicityKey_Local);

                                    // apply local column
                                    DbColumn localColumn = primaryKeyTable.FindColumnDb(manyMultiplicityKey_Local.LocalColumnName);
                                    manyMultiplicityKey_Local.LocalColumn = localColumn;
                                    if (!localColumn.PrimaryKey) {
                                        localColumn.IsReferenceKey = true;
                                        localColumn.IsReferenceKeyTable = primaryKeyTable;
                                    }

                                    if (foreignKeyTable != null) {
                                        // foreign table of that!
                                        manyMultiplicityKey_Local.ForeignTable = foreignKeyTable;

                                        // apply foreign column
                                        DbColumn foreignColumn = foreignKeyTable.FindColumnDb(manyMultiplicityKey_Local.ForeignColumnName);
                                        manyMultiplicityKey_Local.ForeignColumn = foreignColumn;
                                    }
                                    else {
                                        manyMultiplicityKey_Local.ForeignTable = null;
                                        manyMultiplicityKey_Local.ForeignColumn = null;
                                    }
                                }

                                // one-to-? foreign relation will be added
                                if (foreignKeyTable != null) {
                                    // foreign key many end
                                    var oneMultiplicityKey_Foreign = new DbForeignKey() {
                                        ForeignKeyName = keysDataRow["ForeignKey"].ToString(),
                                        LocalColumnName = keysDataRow["FKColumnName"].ToString(),
                                        ForeignColumnName = keysDataRow["PKColumnName"].ToString(),
                                        ForeignTableName = keysDataRow["PKTable"].ToString(),
                                        Multiplicity = DbForeignKey.ForeignKeyMultiplicity.OneToMany
                                    };
                                    // check if it is already there
                                    if (foreignKeyTable.ForeignKeys.Exists(
                                        x =>
                                        x.Multiplicity == DbForeignKey.ForeignKeyMultiplicity.OneToMany &&
                                        x.ForeignKeyName == oneMultiplicityKey_Foreign.ForeignKeyName))
                                        continue;

                                    oneMultiplicityKey_Foreign.UpdateAction =
                                        ConvertSqlServerForeignKeyAction(Convert.ToInt32(keysDataRow["update_referential_action"].ToString()));
                                    oneMultiplicityKey_Foreign.DeleteAction =
                                        ConvertSqlServerForeignKeyAction(Convert.ToInt32(keysDataRow["delete_referential_action"].ToString()));

                                    // to the list
                                    foreignKeyTable.ForeignKeys.Add(oneMultiplicityKey_Foreign);

                                    // apply local column
                                    DbColumn localColumn = foreignKeyTable.FindColumnDb(oneMultiplicityKey_Foreign.LocalColumnName);
                                    oneMultiplicityKey_Foreign.LocalColumn = localColumn;
                                    if (!localColumn.PrimaryKey) {
                                        localColumn.IsReferenceKey = true;
                                        localColumn.IsReferenceKeyTable = primaryKeyTable;
                                    }

                                    if (primaryKeyTable != null) {
                                        // foreign table of that!
                                        oneMultiplicityKey_Foreign.ForeignTable = primaryKeyTable;

                                        // apply foreign column
                                        DbColumn foreignColumn = primaryKeyTable.FindColumnDb(oneMultiplicityKey_Foreign.ForeignColumnName);
                                        oneMultiplicityKey_Foreign.ForeignColumn = foreignColumn;
                                    }
                                    else {
                                        oneMultiplicityKey_Foreign.ForeignTable = null;
                                        oneMultiplicityKey_Foreign.ForeignColumn = null;
                                    }
                                }
                            }// all foreign keys

                            // look for one-to-one situation!
                        }
                    }
                }
            }
            catch {
                // Seems this version of SQL Server doesn't support this query!
                // don't stop here!
            }
        }

        /// <summary>
        /// Reads tables index keys
        /// </summary>
        private void ApplyTablesConstraintKeys(List<DbTable> tables, SQLServerVersions sqlVersion)
        {
            // Fail early for versions we don't support
            if (sqlVersion == SQLServerVersions.SQL2000 || sqlVersion == SQLServerVersions.SQL2000Below) return;

            // Identify indexes and all indexed columns
            string constraintKeySql = @"
-- First query - identify all the indexes in the database
SELECT o.name AS TableName
	 , i.index_id
     , i.object_id
	 , i.name AS IndexName
	 , i.is_unique AS IsUnique
	 , i.is_primary_key AS IsPrimaryKey
	 , i.ignore_dup_key AS IgnoreDuplicateKey
	 , i.is_unique_constraint AS IsUniqueConstraintKey
	 , i.is_disabled AS Disabled
  FROM sys.objects o
       INNER JOIN sys.indexes i ON i.object_id = o.object_id
 WHERE o.is_ms_shipped = 0
   AND o.type = 'U'
       order by i.name;

-- Second query - identify all columns for these indexes
SELECT i.index_id
     , i.object_id
     , c.name as ColumnName
     , ic.*
  FROM sys.indexes i
	   INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id and ic.index_id = i.index_id
	   INNER JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
       order by i.name, ic.index_column_id;
";

            // Load from the database
            DataSet ds = new DataSet();
            using (SqlDataAdapter adapter = new SqlDataAdapter(constraintKeySql, _dbConnection)) {
                adapter.MissingSchemaAction = MissingSchemaAction.AddWithKey;
                adapter.Fill(ds);
            }

            // Okay, now let's look through all the indexes we found
            foreach (DataRow row in ds.Tables[0].Rows) {
                // Find the matching table
                var table = (from t in tables where String.Equals(t.TableName, row["TableName"].ToString()) select t).FirstOrDefault();
                if (table == null) continue;

                // What information do we have about this index?
                var IsUnique = Convert.ToBoolean(row["IsUnique"].ToString());
                var IndexName = row["IndexName"].ToString();
                var IndexId = (int)row["index_id"];
                var ObjectId = (int)row["object_id"];
                var IsPrimaryKey = Convert.ToBoolean(row["IsPrimaryKey"].ToString());

                // Convert all columns to constraint keys
                var columns = (from DataRow r2
                                 in ds.Tables[1].Rows
                               where (int)r2["index_id"] == IndexId
                                  && (int)r2["object_id"] == ObjectId
                               select new DbConstraintKey() {
                                   KeyColumnName = r2["ColumnName"].ToString(),
                                   KeyColumn = table.FindColumnDb(r2["ColumnName"].ToString()),
                               }).ToList();

                // Add either a simple or a complex key
                if (columns.Any()) {
                    table.AddIndex(new DbIndex() {
                        Keys = columns,
                        IsUnique = IsUnique,
                        IndexName = IndexName,
                        IsPrimaryKey = IsPrimaryKey
                    });
                }
            }

            // Finally - scan through each table that doesn't have a primary key and see if we can find one
            //foreach (var table in tables) {
            //    if (!table.HasPrimaryKey()) {
            //        // We don't currently have a primary key, let's see if we can find one
            //        System.Diagnostics.Debug.WriteLine("Table " + table.TableName + " does not have a primary key - checking indexes");
            //        var found = (from DataRow dr in ds.Tables[1].Rows where dr["TableName"] == table.TableName && dr["IsPrimaryKey

            //        keysData.DefaultView.RowFilter = " TableName='" + table.TableName + "' AND IsPrimaryKey=1 ";
            //        DataRowView myrow = null;
            //        foreach (DataRowView keysDataRow in keysData.DefaultView) {
            //            System.Diagnostics.Debug.WriteLine("Table " + table.TableName + " should have a PK, it's named " + keysDataRow["ColumnName"].ToString());
            //            if (myrow != null) {
            //                System.Diagnostics.Debug.WriteLine("Tables with a dual primary key are not supported.");
            //                myrow = null;
            //                break;
            //            }
            //            myrow = keysDataRow;
            //        }

            //        // If we found one single primary key through this means, use it
            //        if (myrow != null) {
            //            foreach (var column in table.SchemaColumns) {
            //                if (String.Equals(column.FieldNameDb, myrow["ColumnName"].ToString(), StringComparison.CurrentCultureIgnoreCase)
            //                    && column.DataTypeDotNet != "System.DateTime") {
            //                    System.Diagnostics.Debug.WriteLine("Matched row " + column.FieldNameDb + " and was able to assign a primary key.");
            //                    column.PrimaryKey = true;
            //                    break;
            //                }
            //            }
            //        }
            //    }
            //}
        }

        /// <summary>
        /// Detecting one-to-one relation
        /// </summary>
        private void ApplyDetectedOneToOneRelation(List<DbTable> tables)
        {
            foreach (var table in tables)
                foreach (var fkey in table.ForeignKeys) {
                    //// already one-to-?
                    //if (fkey.Multiplicity == DbForeignKey.ForeignKeyMultiplicity.OneToMany ||
                    //    fkey.Multiplicity == DbForeignKey.ForeignKeyMultiplicity.OneToOne)
                    //    continue;
                    // already ont-to-one
                    if (fkey.Multiplicity == DbForeignKey.ForeignKeyMultiplicity.OneToOne)
                        continue;

                    if (fkey.LocalColumn == null || fkey.ForeignColumn == null)
                        continue;
                    bool localIsUnique = false;
                    bool foreignIsUnique = false;

                    if (fkey.ForeignColumn.PrimaryKey)
                        foreignIsUnique = true;
                    else {
                        var fkeyC = table.Indexes.FirstOrDefault(x => ((x.Keys.Count == 1) && (x.Keys[0].KeyColumnName == fkey.ForeignColumnName)));
                        if (fkeyC != null) {
                            if (fkeyC.IsUnique)
                                foreignIsUnique = true;
                        }
                    }

                    if (fkey.LocalColumn.PrimaryKey)
                        localIsUnique = true;
                    else {
                        var lkeyC = table.Indexes.FirstOrDefault(x => ((x.Keys.Count == 1) && (x.Keys[0].KeyColumnName == fkey.LocalColumnName)));
                        if (lkeyC != null) {
                            if (lkeyC.IsUnique)
                                localIsUnique = true;
                        }
                    }

                    // both are unique??
                    if (localIsUnique && foreignIsUnique) {
                        // this is one-to-one
                        fkey.Multiplicity = DbForeignKey.ForeignKeyMultiplicity.OneToOne;
                    }
                }
        }

        /// <summary>
        /// Removes duplicate table constraints, PK > UK > IX
        /// </summary>
        private void NormalizeTablesConstraintKeys(List<DbTable> result, SQLServerVersions sqlVersion)
        {
            // look in tables list
            foreach (DbTable table in result) {
                if (table.Indexes.Count == 0) {
                    continue;
                }

                StringCollection duplicateConstraints = new StringCollection();

                // looping the contraints keys
                for (int j = table.Indexes.Count - 1; j >= 0; j--) {
                    var thisIndex = table.Indexes[j];

                    // no primary keys are allowed
                    if (thisIndex.Keys.Count > 0 && thisIndex.IsPrimaryKey) {
                        // There is no need in keeping the primary key
                        table.Indexes.RemoveAt(j);
                        continue;
                    }

                    // If this is a single column index, first look in the foreign keys!
                    if (thisIndex.Keys.Count == 1) {
                        int index = table.ForeignKeys.FindIndex(x => x.LocalColumnName == thisIndex.Keys[0].KeyColumnName);

                        // this is a foreign key and should not be here
                        if (index != -1) {
                            table.Indexes.RemoveAt(j);
                            continue;
                        }
                    }

                    //// if this is not a unique key
                    //// seach for a unique one if it is there
                    //if (thisIndex.IsUnique == false)
                    //{
                    //	index = table.Indexes.FindIndex(x =>
                    //		x.KeyColumnName == thisIndex.KeyColumnName
                    //		&& x.IsUnique == true);

                    //	if (index != -1)
                    //	{
                    //		// the same and the Unique key is already there!
                    //		table.Indexes.RemoveAt(j);
                    //		continue;
                    //	}
                    //}
                    //else
                    //{
                    //	var notUniqueKeys = table.Indexes.FindAll(x =>
                    //		x.KeyColumnName == thisIndex.KeyColumnName
                    //		&& x.IsUnique == false);

                    //	if (notUniqueKeys != null && notUniqueKeys.Count > 0)
                    //	{
                    //		// remove them
                    //		notUniqueKeys.ForEach(x => table.Indexes.Remove(x));
                    //		continue;
                    //	}
                    //}

                    //// look for duplication constraint key
                    //if (duplicateConstraints.Contains(thisIndex.KeyColumnName))
                    //{
                    //	// the column with index is already there
                    //	table.Indexes.RemoveAt(j);
                    //	continue;
                    //}

                    //// all to the constraint key list
                    //duplicateConstraints.Add(thisIndex.KeyColumnName);
                }
            }
        }

        /// <summary>
        /// Reads columns description from SQLServer
        /// </summary>
        private void ApplyColumnsDescription(string tableName, string ownerName, List<DbColumn> columns)
        {
            // READING TABLE DESCRIPTION --> SELECT * FROM sys.extended_properties where minor_id=0 and major_id=OBJECT_ID('acc.AccAccount')  order by major_id

            // there is no column!
            if (columns.Count == 0)
                return;

            // command format
            const string descriptionSql = "SELECT * FROM ::fn_listextendedproperty('MS_Description', 'schema', N'{0}', 'table', N'{1}', 'column', NULL) AS func ";

            try {
                using (var adapter = new SqlDataAdapter(String.Format(descriptionSql, ownerName, tableName), (SqlConnection)_dbConnection)) {
                    adapter.MissingSchemaAction = MissingSchemaAction.AddWithKey;

                    // description data table
                    using (var descriptionData = new DataTable()) {
                        // Jjust to avoid stupid "Failed to enable constraints" error!
                        using (var tempDs = new DataSet()) {
                            // Avoiding stupid "Failed to enable constraints" error!
                            tempDs.EnforceConstraints = false;
                            tempDs.Tables.Add(descriptionData);

                            // Get from db
                            adapter.Fill(descriptionData);
                        }

                        // if something found
                        if (descriptionData.Rows.Count > 0)
                            // find description if there is any
                            foreach (var column in columns) {
                                // filter row to find the column
                                descriptionData.DefaultView.RowFilter = " objname='" + column.FieldNameDb + "' ";
                                if (descriptionData.DefaultView.Count > 0) {
                                    // description found!
                                    column.UserDescription = descriptionData.DefaultView[0].Row["value"].ToString();
                                    column.UserDescription = column.UserDescription.Replace("\r\n", " ").Replace("\n", " ").Replace("\"", "\\\"");
                                }
                            }
                    }
                }
            }
            catch {
                // Seems this version of SQL Server doesn't support this query!
                // don't stop here!
                // TODO: inform user
            }
        }

        /// <summary>
        /// Column DBType name
        /// </summary>
        private string GetColumnDbDataType(DataTable columnsDbTypeTable, string tableName, string columnName)
        {
            DataRow[] drs;
            drs = columnsDbTypeTable.Select(String.Format("TABLE_NAME='{0}' AND COLUMN_NAME='{1}'",
                                              tableName,
                                              columnName));
            // Db Type
            string dbType = drs[0].ItemArray[7].ToString();

            return dbType;
        }

        /// <summary>
        /// Finds table from list
        /// </summary>
        private DbTable FindTable(List<DbTable> tables, string tableName)
        {
            foreach (var table in tables) {
                if (table.TableName == tableName)
                    return table;
            }
            return null;
        }

        /// <summary>
        /// Detecting sql server version
        /// </summary>
        private SQLServerVersions DetectSqlServerVersion(DbConnection conn)
        {
            string versionStr = conn.ServerVersion;
            var majorMatch = Regex.Match(versionStr, @"(?<Major>\d+)\..*", RegexOptions.Compiled);
            if (majorMatch != null) {
                var majorVersion = Convert.ToInt16(majorMatch.Groups["Major"].Value);
                switch (majorVersion) {
                    case 8:
                        return SQLServerVersions.SQL2000;

                    case 9:
                        return SQLServerVersions.SQL2005;

                    case 10:
                        return SQLServerVersions.SQL2008;

                    default:
                        if (majorVersion > 10) {
                            return SQLServerVersions.SQL2008Above;
                        }
                        return SQLServerVersions.SQL2000Below;
                }
            }
            else
                return SQLServerVersions.SQL2000Below;
        }

        private DbForeignKeyAction ConvertSqlServerForeignKeyAction(int actionCode)
        {
            switch (actionCode) {
                case 0:
                    return DbForeignKeyAction.NoAction;

                case 1:
                    return DbForeignKeyAction.Cascade;

                case 2:
                    return DbForeignKeyAction.SetNull;

                case 3:
                    return DbForeignKeyAction.SetDefault;
                    //case 4:
                    //    return DbForeignKeyAction.Restrict;
            }
            return DbForeignKeyAction.NotSet;
        }

        #endregion private methods

        #region Insert with ID statements

        public override string GetInsertWithIdValues()
        {
            return " OUTPUT inserted.[:PrimaryKeyName:] VALUES ";
        }

        public override string GetInsertWithSuffix()
        {
            return ";";
        }

        #endregion Insert with ID statements
    }
}
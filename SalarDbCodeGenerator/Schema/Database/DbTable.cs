﻿using System.Collections.Generic;
using System.Linq;

// ====================================
// SalarDbCodeGenerator
// http://SalarDbCodeGenerator.codeplex.com
// Salar Khalilzadeh <salar2k@gmail.com>
// © 2012, All rights reserved
// 2012/07/06
// ====================================
namespace SalarDbCodeGenerator.Schema.Database
{
    public class DbTable
    {
        public enum TableTypeInfo { Table, View }

        #region field variables

        private string _tableName;

        #endregion field variables

        #region properties

        public bool Enabled { get; set; }
        public bool ReadOnly { get; set; }
        public TableTypeInfo TableType { get; set; }
        public List<DbColumn> SchemaColumns { get; private set; }
        public List<DbForeignKey> ForeignKeys { get; private set; }
        public List<DbIndex> Indexes { get; private set; }
        public string OwnerName { get; set; }

        public string TableNameSchema { get; set; }

        public string TableName
        {
            get { return _tableName; }
            private set {
                _tableName = value;
                TableNameSchema = _tableName;
                TableNameCS = _tableName;
            }
        }

        public string EscapedTableName { get; set; }

        private string _tableNameCs;

        /// <summary>
        /// TableName Case Sensitive.
        /// Some databases (e.g Oracle) allow case sensitive table names which results two table with same name.
        /// </summary>
        public string TableNameCS
        {
            get { return _tableNameCs; }
            set {
                _tableNameCs = value;
                TableNameSchemaCS = value;
            }
        }

        public string TableNameSchemaCS { get; set; }

        #endregion properties

        #region public methods

        public DbTable(string tableName, List<DbColumn> schemaColumns = null, List<DbForeignKey> foreignKeys = null, List<DbIndex> constraintKeys = null)
        {
            TableName = tableName;
            SchemaColumns = schemaColumns ?? new List<DbColumn>();
            ForeignKeys = foreignKeys ?? new List<DbForeignKey>();
            Indexes = constraintKeys ?? new List<DbIndex>();
            TableType = TableTypeInfo.Table;
        }

        /// <summary>
        /// Checks if this table has primary key or not
        /// </summary>
        public bool HasPrimaryKey()
        {
            int num_primary_keys = 0;
            foreach (var column in SchemaColumns) {
                if (column.PrimaryKey) {
                    num_primary_keys++;
                }
            }

            return (num_primary_keys == 1);
        }

        private bool? _hasOneToOneRelation = null;

        /// <summary>
        /// Is this a one-to-one table?
        /// </summary>
        public bool HasOneToOneRelation()
        {
            if (_hasOneToOneRelation != null)
                return _hasOneToOneRelation.Value;
            foreach (var foreignKey in ForeignKeys) {
                if (foreignKey.Multiplicity == DbForeignKey.ForeignKeyMultiplicity.OneToOne) {
                    _hasOneToOneRelation = true;
                    return true;
                }
            }
            _hasOneToOneRelation = false;
            return false;
        }

        /// <summary>
        /// Is this a one-to-one table?
        /// </summary>
        public DbForeignKey GetOneToOneRelation()
        {
            if (_hasOneToOneRelation == false)
                return null;
            foreach (var foreignKey in ForeignKeys) {
                if (foreignKey.Multiplicity == DbForeignKey.ForeignKeyMultiplicity.OneToOne) {
                    _hasOneToOneRelation = true;
                    return foreignKey;
                }
            }
            _hasOneToOneRelation = false;
            return null;
        }

        /// <summary>
        /// Checks if this table has auto increment column or not
        /// </summary>
        public bool HasAutoIncrement()
        {
            foreach (var column in SchemaColumns) {
                if (column.AutoIncrement)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns number of auto increment columns
        /// </summary>
        public int GetAutoIncrementCount()
        {
            int result = 0;
            foreach (var column in SchemaColumns) {
                if (column.AutoIncrement)
                    result++;
            }
            return result;
        }

        /// <summary>
        /// Returns number of auto increment columns
        /// </summary>
        public DbColumn GetFirstAutoIncrementField()
        {
            foreach (var column in SchemaColumns) {
                if (column.AutoIncrement)
                    return column;
            }
            return null;
        }

        /// <summary>
        /// Returns the first primary-key if there is any, otherwise returns null.
        /// </summary>
        public DbColumn GetPrimaryKey()
        {
            foreach (var column in SchemaColumns) {
                if (column.PrimaryKey)
                    return column;
            }
            return null;
        }

        /// <summary>
        /// Returns the number primary-keys in the table.
        /// </summary>
        public int GetPrimaryKeyCount()
        {
            int count = 0;
            foreach (var column in SchemaColumns) {
                if (column.PrimaryKey)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Returns the list of primary-keys in the table.
        /// </summary>
        public DbColumn[] GetPrimaryKeyList()
        {
            var colList = new List<DbColumn>();
            foreach (var column in SchemaColumns) {
                if (column.PrimaryKey)
                    colList.Add(column);
            }
            return colList.ToArray();
        }

        public DbColumn FindColumnDb(string fieldName)
        {
            foreach (var column in SchemaColumns) {
                if (column.FieldNameDb == fieldName)
                    return column;
            }
            return null;
        }

        public DbColumn FindColumnSchema(string fieldName)
        {
            foreach (var column in SchemaColumns) {
                if (column.FieldNameSchema == fieldName)
                    return column;
            }
            return null;
        }

        private List<DbColumn> FindColumns__(string fieldName)
        {
            var result = new List<DbColumn>();
            foreach (var column in SchemaColumns) {
                if (column.FieldNameDb == fieldName)
                    result.Add(column);
            }
            return result;
        }

        #endregion public methods

        #region protected methods

        public override string ToString()
        {
            return this.TableType.ToString() + " " + this.TableName;
        }

        /// <summary>
        /// Returns the escaped table name if available, and the regular table name otherwise
        /// </summary>
        /// <returns></returns>
        public string GetEscapedTableName()
        {
            return EscapedTableName ?? TableName;
        }

        /// <summary>
        /// Add an index; but if any other index on this table has the same name, add something to it
        /// </summary>
        /// <param name="dbIndex"></param>
        public void AddIndex(DbIndex dbIndex)
        {
            // Here are all our existing names
            var existingIndexNames = (from existing in Indexes select existing.GetIndexKeyName()).ToList();
            var rawIndexName = dbIndex.GetIndexKeyName();
            var newIndexName = rawIndexName;
            int i = 1;
            while (existingIndexNames.Contains(newIndexName)) {
                i++;
                newIndexName = rawIndexName + i.ToString();
                dbIndex.SetIndexKeyName(newIndexName);
            }
            Indexes.Add(dbIndex);
        }

        #endregion protected methods
    }
}
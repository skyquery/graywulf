﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.Data.Common;
using System.Runtime.Serialization;

namespace Jhu.Graywulf.Schema
{
    [Serializable]
    [DataContract(Namespace = "")]
    public class DataType : ICloneable
    {
        #region Static functions to create types

        public static DataType Create(Type type, int length)
        {
            return Create(type, length, 0, 0, false);
        }

        /// <summary>
        /// Creates data type descriptor from a .Net type
        /// </summary>
        /// <param name="type"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static DataType Create(Type type, int length, byte precision, byte scale, bool isNullable)
        {
            DataType dt;

            if (type == typeof(Boolean))
            {
                dt = DataTypes.Boolean;
            }
            else if (type == typeof(SByte))
            {
                dt = DataTypes.SByte;
            }
            else if (type == typeof(Byte))
            {
                dt = DataTypes.Byte;
            }
            else if (type == typeof(Int16))
            {
                dt = DataTypes.Int16;
            }
            else if (type == typeof(UInt16))
            {
                dt = DataTypes.UInt16;
            }
            else if (type == typeof(Int32))
            {
                dt = DataTypes.Int32;
            }
            else if (type == typeof(UInt32))
            {
                dt = DataTypes.UInt32;
            }
            else if (type == typeof(Int64))
            {
                dt = DataTypes.Int64;
            }
            else if (type == typeof(UInt64))
            {
                dt = DataTypes.UInt64;
            }
            else if (type == typeof(Single))
            {
                dt = DataTypes.Single;
            }
            else if (type == typeof(Double))
            {
                dt = DataTypes.Double;
            }
            else if (type == typeof(Decimal))
            {
                dt = DataTypes.Decimal;
            }
            else if (type == typeof(DateTime))
            {
                dt = DataTypes.DateTime;
            }
            else if (type == typeof(Guid))
            {
                dt = DataTypes.Guid;
            }
            else if (type == typeof(char))
            {
                dt = DataTypes.Char;
            }
            else if (type == typeof(char[]))
            {
                dt = DataTypes.String;
            }
            else if (type == typeof(string))
            {
                dt = DataTypes.SqlNVarChar;
            }
            else if (type == typeof(byte[]))
            {
                dt = DataTypes.SqlVarBinary;
            }
            else
            {
                throw new NotImplementedException();
            }

            if (dt.HasLength)
            {
                dt.Length = length;
            }

            if (dt.HasPrecision)
            {
                dt.Precision = precision;
            }

            if (dt.HasScale)
            {
                dt.Scale = scale;
            }

            dt.IsNullable = isNullable;

            return dt;
        }

        /* TODO: delete
        public static DataType Create(DataRow dr)
        {
            // Get .Net type and other parameters
            var type = (Type)dr[SchemaTableColumn.DataType];
            var length = Convert.ToInt32(dr[SchemaTableColumn.ColumnSize]);
            var precision = Convert.ToByte(dr[SchemaTableColumn.NumericPrecision]);
            var scale = Convert.ToByte(dr[SchemaTableColumn.NumericScale]);
            var isnullable = Convert.ToBoolean(dr[SchemaTableColumn.AllowDBNull]);

            DataType dt;

            // Try to interpret provider type as sql server type
            SqlDbType sqltype;
            if (Enum.TryParse<SqlDbType>((string)dr["DataTypeName"], true, out sqltype))
            {
                // This can be interpreted as a SQL Server type
                dt = Create(sqltype, length, precision, scale, isnullable);
            }
            else
            {
                // This is a .Net type, might not be supported by SqlServer
                dt = Create(type, length);
            }

            return dt;
        }*/

        #endregion
        #region Private variables for property storage

        /// <summary>
        /// Type name
        /// </summary>
        [NonSerialized]
        private string name;

        /// <summary>
        /// Corresponding .Net type
        /// </summary>
        [NonSerialized]
        private Type type;

        /// <summary>
        /// Corresponding SqlServer type
        /// </summary>
        [NonSerialized]
        private SqlDbType? sqlDbType;

        /// <summary>
        /// Size of the primitive type in bytes
        /// </summary>
        [NonSerialized]
        private int byteSize;

        /// <summary>
        /// Scale (for decimal)
        /// </summary>
        [NonSerialized]
        private byte scale;

        /// <summary>
        /// Precision (for decimal)
        /// </summary>
        [NonSerialized]
        private byte precision;

        /// <summary>
        /// Size in bytes (for char and binary)
        /// </summary>
        [NonSerialized]
        private int length;

        /// <summary>
        /// Maximum length in SQL Server, -1 means max
        /// </summary>
        [NonSerialized]
        private int maxLength;

        /// <summary>
        /// Is length variable (char, binary vs varchar, varbinary)
        /// </summary>
        [NonSerialized]
        private bool isVarLength;

        /// <summary>
        /// Is an array, currently no SQL Server support
        /// </summary>
        [NonSerialized]
        private bool isSqlArray;

        /// <summary>
        /// Length of array, (not supported by SQL Server)
        /// </summary>
        [NonSerialized]
        private int arrayLength;

        /// <summary>
        /// Is variable length array
        /// </summary>
        [NonSerialized]
        private bool isVarArrayLength;

        /// <summary>
        /// Is type nullable
        /// </summary>
        [NonSerialized]
        private bool isNullable;

        #endregion
        #region Properties

        /// <summary>
        /// Gets or sets the name of the data type.
        /// </summary>
        [DataMember]
        public string Name
        {
            get { return name; }
            internal set { name = value; }
        }

        /// <summary>
        /// Gets the SQL name of the type with the length appended.
        /// </summary>
        /// <remarks>
        /// Type name is returned in SQL Server format, e.g. nvarchar(50)
        /// </remarks>
        [IgnoreDataMember]
        public string NameWithLength
        {
            get
            {
                if (!HasLength)
                {
                    return name;
                }
                else if (IsMaxLength)
                {
                    return System.String.Format("{0}(max)", name);
                }
                else
                {
                    return System.String.Format("{0}({1})", name, length);
                }
            }
        }

        /// <summary>
        /// Gets the corresponding .Net type
        /// </summary>
        [IgnoreDataMember]
        public Type Type
        {
            get { return type; }
            internal set { type = value; }
        }

        [DataMember]
        private string Type_ForXml
        {
            get { return type != null ? type.FullName : null; }
            set { type = value != null ? Type.GetType(value) : null; }
        }

        /// <summary>
        /// Gets the corresponding SQL Server type
        /// </summary>
        [DataMember]
        public SqlDbType? SqlDbType
        {
            get { return sqlDbType; }
            internal set { sqlDbType = value; }
        }

        /// <summary>
        /// Gets the size of the primitive type in bytes
        /// </summary>
        [DataMember]
        public int ByteSize
        {
            get { return byteSize; }
            internal set { byteSize = value; }
        }

        /// <summary>
        /// Gets or sets the scale (for decimal values)
        /// </summary>
        [DataMember]
        public byte Scale
        {
            get { return scale; }
            set { scale = value; }
        }

        /// <summary>
        /// Gets or sets the precision (for decimal values)
        /// </summary>
        [DataMember]
        public byte Precision
        {
            get { return precision; }
            set { precision = value; }
        }

        /// <summary>
        /// Gets or sets the length of the type (for char, etc.)
        /// </summary>
        [DataMember]
        public int Length
        {
            get { return length; }
            set { length = value; }
        }

        /// <summary>
        /// Gets if the length parameter is set to max (varchar(max), etc.)
        /// </summary>
        [IgnoreDataMember]
        public bool IsMaxLength
        {
            get { return length == -1 || (long)length * byteSize > 8000; }
        }

        /// <summary>
        /// Gets the maximum length of the type, in SQL Server
        /// </summary>
        [DataMember]
        public int MaxLength
        {
            get { return maxLength; }
            internal set { maxLength = value; }
        }

        /// <summary>
        /// Gets if the length is variable (varchar, varbinary, etc.)
        /// </summary>
        [DataMember]
        public bool IsVarLength
        {
            get { return isVarLength; }
            internal set { isVarLength = value; }
        }

        /// <summary>
        /// Gets if the type is an array.
        /// </summary>
        [DataMember]
        public bool IsSqlArray
        {
            get { return isSqlArray; }
            private set { isSqlArray = value; }
        }

        /// <summary>
        /// Gets or sets the maximum array size.
        /// </summary>
        [DataMember]
        public int ArrayLength
        {
            get { return arrayLength; }
            set { arrayLength = value; }
        }

        /// <summary>
        /// Gets or sets if the array is bounded
        /// </summary>
        [DataMember]
        private bool IsVarArrayLength
        {
            get { return isVarArrayLength; }
            set { isVarArrayLength = value; }
        }

        /// <summary>
        /// Gets or sets whether the data type is nullable
        /// </summary>
        [DataMember]
        public bool IsNullable
        {
            get { return isNullable; }
            set { isNullable = value; }
        }

        /// <summary>
        /// Gets if type is compatible with SQL Server
        /// </summary>
        [IgnoreDataMember]
        public bool IsSqlServerCompatible
        {
            get { return sqlDbType.HasValue; }
        }

        /// <summary>
        /// Gets if the type has a length parameter (char, binary, etc.)
        /// </summary>
        [IgnoreDataMember]
        public bool HasLength
        {
            get
            {
                switch (sqlDbType)
                {
                    case System.Data.SqlDbType.BigInt:
                    case System.Data.SqlDbType.Decimal:
                    case System.Data.SqlDbType.Float:
                    case System.Data.SqlDbType.Int:
                    case System.Data.SqlDbType.Money:
                    case System.Data.SqlDbType.Real:
                    case System.Data.SqlDbType.SmallInt:
                    case System.Data.SqlDbType.SmallMoney:
                    case System.Data.SqlDbType.Bit:
                    case System.Data.SqlDbType.Date:
                    case System.Data.SqlDbType.DateTime:
                    case System.Data.SqlDbType.DateTime2:
                    case System.Data.SqlDbType.DateTimeOffset:
                    case System.Data.SqlDbType.Image:
                    case System.Data.SqlDbType.NText:
                    case System.Data.SqlDbType.SmallDateTime:
                    case System.Data.SqlDbType.Structured:
                    case System.Data.SqlDbType.Text:
                    case System.Data.SqlDbType.Time:
                    case System.Data.SqlDbType.Timestamp:
                    case System.Data.SqlDbType.TinyInt:
                    case System.Data.SqlDbType.Udt:
                    case System.Data.SqlDbType.UniqueIdentifier:
                    case System.Data.SqlDbType.Variant:
                    case System.Data.SqlDbType.Xml:
                        return false;
                    case System.Data.SqlDbType.Char:
                    case System.Data.SqlDbType.VarChar:
                    case System.Data.SqlDbType.NChar:
                    case System.Data.SqlDbType.NVarChar:
                    case System.Data.SqlDbType.Binary:
                    case System.Data.SqlDbType.VarBinary:
                        return true;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        /// <summary>
        /// Gets if the precision of the data type can be set
        /// </summary>
        [IgnoreDataMember]
        public bool HasPrecision
        {
            get
            {
                switch (sqlDbType)
                {
                    case System.Data.SqlDbType.Decimal:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Gets if the scale of the data type can be set
        /// </summary>
        [IgnoreDataMember]
        public bool HasScale
        {
            get
            {
                switch (sqlDbType)
                {
                    case System.Data.SqlDbType.Decimal:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Gets if the corresponding SQL Server type is signed
        /// </summary>
        [IgnoreDataMember]
        public bool IsSigned
        {
            get
            {
                switch (sqlDbType)
                {
                    case System.Data.SqlDbType.BigInt:
                    case System.Data.SqlDbType.Decimal:
                    case System.Data.SqlDbType.Float:
                    case System.Data.SqlDbType.Int:
                    case System.Data.SqlDbType.Money:
                    case System.Data.SqlDbType.Real:
                    case System.Data.SqlDbType.SmallInt:
                    case System.Data.SqlDbType.SmallMoney:
                        return true;
                    case System.Data.SqlDbType.Bit:
                    case System.Data.SqlDbType.Binary:
                    case System.Data.SqlDbType.Char:
                    case System.Data.SqlDbType.Date:
                    case System.Data.SqlDbType.DateTime:
                    case System.Data.SqlDbType.DateTime2:
                    case System.Data.SqlDbType.DateTimeOffset:
                    case System.Data.SqlDbType.Image:
                    case System.Data.SqlDbType.NChar:
                    case System.Data.SqlDbType.NText:
                    case System.Data.SqlDbType.NVarChar:
                    case System.Data.SqlDbType.SmallDateTime:
                    case System.Data.SqlDbType.Structured:
                    case System.Data.SqlDbType.Text:
                    case System.Data.SqlDbType.Time:
                    case System.Data.SqlDbType.Timestamp:
                    case System.Data.SqlDbType.TinyInt:
                    case System.Data.SqlDbType.Udt:
                    case System.Data.SqlDbType.UniqueIdentifier:
                    case System.Data.SqlDbType.VarBinary:
                    case System.Data.SqlDbType.VarChar:
                    case System.Data.SqlDbType.Variant:
                    case System.Data.SqlDbType.Xml:
                        return false;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        /// <summary>
        /// Gets if the corresponding SQL Server type is integer
        /// </summary>
        [IgnoreDataMember]
        public bool IsInteger
        {
            get
            {
                switch (sqlDbType)
                {
                    case System.Data.SqlDbType.BigInt:
                    case System.Data.SqlDbType.Int:
                    case System.Data.SqlDbType.SmallInt:
                    case System.Data.SqlDbType.TinyInt:
                        return true;
                    case System.Data.SqlDbType.Decimal:
                    case System.Data.SqlDbType.Float:
                    case System.Data.SqlDbType.Money:
                    case System.Data.SqlDbType.Real:
                    case System.Data.SqlDbType.SmallMoney:
                    case System.Data.SqlDbType.Bit:
                    case System.Data.SqlDbType.Binary:
                    case System.Data.SqlDbType.Char:
                    case System.Data.SqlDbType.Date:
                    case System.Data.SqlDbType.DateTime:
                    case System.Data.SqlDbType.DateTime2:
                    case System.Data.SqlDbType.DateTimeOffset:
                    case System.Data.SqlDbType.Image:
                    case System.Data.SqlDbType.NChar:
                    case System.Data.SqlDbType.NText:
                    case System.Data.SqlDbType.NVarChar:
                    case System.Data.SqlDbType.SmallDateTime:
                    case System.Data.SqlDbType.Structured:
                    case System.Data.SqlDbType.Text:
                    case System.Data.SqlDbType.Time:
                    case System.Data.SqlDbType.Timestamp:
                    case System.Data.SqlDbType.Udt:
                    case System.Data.SqlDbType.UniqueIdentifier:
                    case System.Data.SqlDbType.VarBinary:
                    case System.Data.SqlDbType.VarChar:
                    case System.Data.SqlDbType.Variant:
                    case System.Data.SqlDbType.Xml:
                        return false;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        // TODO: add properties for HasPrecision and HasScale?

        #endregion
        #region Constructors and initializers

        internal DataType()
        {
            InitializeMembers();
        }

        internal DataType(DataType old)
        {
            CopyMembers(old);
        }

        private void InitializeMembers()
        {
            this.name = null;
            this.type = null;
            this.sqlDbType = System.Data.SqlDbType.Int;
            this.byteSize = 0;
            this.scale = 0;
            this.precision = 0;
            this.length = 0;
            this.maxLength = 0;
            this.isVarLength = false;
            this.isSqlArray = false;
            this.arrayLength = 0;
            this.isVarArrayLength = false;
            this.isNullable = false;
        }

        private void CopyMembers(DataType old)
        {
            this.name = old.name;
            this.type = old.type;
            this.sqlDbType = old.sqlDbType;
            this.byteSize = old.byteSize;
            this.scale = old.scale;
            this.precision = old.precision;
            this.length = old.length;
            this.maxLength = old.maxLength;
            this.isVarLength = old.isVarLength;
            this.isSqlArray = old.isSqlArray;
            this.arrayLength = old.arrayLength;
            this.isVarArrayLength = old.isVarArrayLength;
            this.isNullable = old.isNullable;
        }

        public object Clone()
        {
            return new DataType(this);
        }

        #endregion

        public bool Compare(DataType other)
        {
            var res = true;

            res &= SchemaManager.Comparer.Compare(this.Name, other.name) == 0;
            res &= this.type == other.type;
            res &= this.scale == other.scale;
            res &= this.precision == other.precision;
            res &= !this.HasLength || (this.length == other.length);
            res &= this.isNullable == other.isNullable;

            return res;
        }

        public void CopyToSchemaTableRow(DataRow dr)
        {
            if (HasLength)
            {
                dr[SchemaTableColumn.ColumnSize] = this.length;
            }
            else
            {
                dr[SchemaTableColumn.ColumnSize] = this.byteSize;
            }
            dr[SchemaTableColumn.NumericPrecision] = this.precision;
            dr[SchemaTableColumn.NumericScale] = this.scale;
            dr[SchemaTableColumn.DataType] = this.type;
            dr[SchemaTableColumn.ProviderType] = this.name;
            dr[SchemaTableColumn.IsLong] = this.IsMaxLength;
            dr[SchemaTableOptionalColumn.ProviderSpecificDataType] = this.name;
            dr[SchemaTableColumn.AllowDBNull] = this.isNullable;
        }
    }
}
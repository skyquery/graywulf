﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization;
using Jhu.Graywulf.Registry;
using Jhu.Graywulf.ParserLib;
using Jhu.Graywulf.Schema;
using Jhu.Graywulf.Schema.SqlServer;

namespace Jhu.Graywulf.Jobs.Query
{
    /// <summary>
    /// Implements basic functions to create and manipulate jobs
    /// wrapping queries.
    /// </summary>
    /// <remarks>
    /// The main purpose of this class is to support plugin based
    /// query types and dataset types.
    /// </remarks>
    [Serializable]
    public abstract class QueryFactory : JobFactoryBase
    {
        #region Static members

        public static QueryFactory Create(Federation federation)
        {
            // Load federation and get query factory name from settings
            var ft = Type.GetType(federation.QueryFactory);
            return (QueryFactory)Activator.CreateInstance(ft, federation.Context);
        }

        #endregion

        [NonSerialized]
        private static Type[] queryTypes = null;

        public Type[] QueryTypes
        {
            get
            {
                lock (SyncRoot)
                {
                    if (queryTypes == null)
                    {
                        queryTypes = LoadQueryTypes();
                    }
                }

                return queryTypes;
            }
        }

        protected QueryFactory()
            : base()
        {
            InitializeMembers(new StreamingContext());
        }

        protected QueryFactory(Context context)
            : base(context)
        {
            InitializeMembers(new StreamingContext());
        }

        [OnDeserializing]
        private void InitializeMembers(StreamingContext context)
        {
        }

        protected abstract Type[] LoadQueryTypes();

        public QueryBase CreateQuery(string queryString)
        {
            return CreateQuery(queryString, ExecutionMode.Graywulf, null, null, null, null);
        }

        public QueryBase CreateQuery(string queryString, ExecutionMode mode)
        {
            return CreateQuery(queryString, mode, null, null, null, null);
        }

        public QueryBase CreateQuery(string queryString, ExecutionMode mode, string outputTable)
        {
            return CreateQuery(queryString, mode, outputTable, null, null, null);
        }

        public QueryBase CreateQuery(string queryString, ExecutionMode mode, string outputTable, DatasetBase mydbds, DatasetBase tempds, DatasetBase codeds)
        {
            var parser = CreateParser();
            var root = parser.Execute(queryString);

            QueryBase q = CreateQueryBase((Node)root);
            q.QueryFactoryTypeName = Util.TypeNameFormatter.ToUnversionedAssemblyQualifiedName(this.GetType());
            q.ExecutionMode = mode;

            switch (mode)
            {
                case ExecutionMode.Graywulf:
                    GetInitializedQuery_Graywulf(q, queryString, outputTable);
                    break;
                case ExecutionMode.SingleServer:
                    GetInitializedQuery_SingleServer(q, queryString, outputTable, (SqlServerDataset)mydbds, (SqlServerDataset)tempds, (SqlServerDataset)codeds);
                    break;
                default:
                    throw new NotImplementedException();
            }

            return q;
        }

        public abstract ParserLib.Parser CreateParser();

        public abstract SqlParser.SqlValidator CreateValidator();

        public abstract SqlParser.SqlNameResolver CreateNameResolver();

        protected abstract QueryBase CreateQueryBase(Node root);

        protected abstract void GetInitializedQuery_Graywulf(QueryBase query, string queryString, string outputTable);

        protected abstract void GetInitializedQuery_SingleServer(QueryBase query, string queryString, string outputTable, SqlServerDataset mydbds, SqlServerDataset tempds, SqlServerDataset codeds);

        #region Job scheduling functions

        public abstract JobInstance ScheduleAsJob(string jobName, QueryBase query, string queueName, string comments);

        #endregion
    }
}

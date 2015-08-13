﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Runtime.Serialization;
using System.IO;
using System.Xml.Serialization;
using Jhu.Graywulf.Tasks;
using Jhu.Graywulf.IO;
using Jhu.Graywulf.IO.Tasks;
using Jhu.Graywulf.Registry;
using Jhu.Graywulf.Activities;
using Jhu.Graywulf.Schema;
using Jhu.Graywulf.Schema.SqlServer;
using Jhu.Graywulf.SqlParser;
using Jhu.Graywulf.SqlCodeGen.SqlServer;
using Jhu.Graywulf.RemoteService;

namespace Jhu.Graywulf.Jobs.Query
{
    /// <summary>
    /// Implements basic functions that are required for query execution
    /// in workflow environments.
    /// </summary>
    /// <remarks>
    /// This class is serialized by the workflow engine when persisted and
    /// serialized into XML when a job is created.
    /// </remarks>
    [Serializable]
    [DataContract(Namespace = "")]
    public abstract class QueryObject : IContextObject, ICancelableTask, ICloneable
    {
        #region Member variables

        /// <summary>
        /// Used to synchronize on for certain operations that run in
        /// parallel when query workflows are executed
        /// </summary>
        [NonSerialized]
        internal object syncRoot;

        #endregion
        #region Property storage member variables

        /// <summary>
        /// Individual query time-out, overall job timeout is enforced by
        /// the scheduler in a different way.
        /// </summary>
        private int queryTimeout;

        /// <summary>
        /// Determines if queries are dumped into files during execution
        /// </summary>
        private bool dumpSql;

        /// <summary>
        /// Cache for registry context
        /// </summary>
        [NonSerialized]
        private Context context;

        /// <summary>
        /// Cache for the scheduler interface
        /// </summary>
        [NonSerialized]
        private IScheduler scheduler;

        /// <summary>
        /// Type name of the query factory class
        /// </summary>
        private string queryFactoryTypeName;

        /// <summary>
        /// Holds a reference to the query factory class
        /// </summary>
        [NonSerialized]
        private Lazy<QueryFactory> queryFactory;

        /// <summary>
        /// The original query to be executed
        /// </summary>
        private string queryString;

        private string batchName;
        private string queryName;

        /// <summary>
        /// The dataset to be assumed when no DATASET: part in
        /// table names appear.
        /// </summary>
        private SqlServerDataset defaultDataset;

        /// <summary>
        /// Dataset to store temporary tables during query execution.
        /// </summary>
        private SqlServerDataset temporaryDataset;

        /// <summary>
        /// Dataset to be used to find functions by default.
        /// </summary>
        private SqlServerDataset codeDataset;

        /// <summary>
        /// A list of custom datasets, i.e. those that are not
        /// configured centrally, for example MyDB
        /// </summary>
        private List<DatasetBase> customDatasets;

        /// <summary>
        /// Query execution mode, either single server or Graywulf cluster
        /// </summary>
        private ExecutionMode executionMode;

        /// <summary>
        /// Flag to know if query was already cancelled. Used in ICancelableTask
        /// implementation
        /// </summary>
        [NonSerialized]
        private bool isCanceled;

        /// <summary>
        /// Holds a list of ICancelableTask instances that are all to be canceled
        /// if the query workflow is canceled.
        /// </summary>
        [NonSerialized]
        private Dictionary<string, ICancelableTask> cancelableTasks;

        /// <summary>
        /// The root object of the query parsing tree
        /// </summary>
        [NonSerialized]
        private SelectStatement selectStatement;

        /// <summary>
        /// True, if the FinishInterpret function has completed.
        /// </summary>
        [NonSerialized]
        private bool isInterpretFinished;

        /// <summary>
        /// Holds a list of temporary tables created during query execution.
        /// Need to delete all these after the query has completed.
        /// </summary>
        private ConcurrentDictionary<string, Table> temporaryTables;

        /// <summary>
        /// Holds a list of temporary views created during query execution.
        /// Need to delete all these after the query has completed.
        /// </summary>
        private ConcurrentDictionary<string, View> temporaryViews;

        /// <summary>
        /// Holds a reference to the federation registry object.
        /// </summary>
        private EntityReference<Federation> federationReference;

        /// <summary>
        /// Holds a reference to the code database registry object.
        /// </summary>
        private EntityReference<DatabaseInstance> codeDatabaseInstanceReference;

        /// <summary>
        /// Holds a reference to temporary database registry object.
        /// </summary>
        private EntityReference<DatabaseInstance> temporaryDatabaseInstanceReference;

        /// <summary>
        /// Hold a reference to the server instance that was assigned
        /// by the scheduler to a given partition of the query.
        /// </summary>
        private EntityReference<ServerInstance> assignedServerInstanceReference;

        #endregion
        #region Properties

        [IgnoreDataMember]
        protected SqlQueryCodeGenerator CodeGenerator
        {
            get
            {
                return new SqlQueryCodeGenerator()
                {
                    ResolveNames = true
                };
            }
        }

        /// <summary>
        /// Gets or sets the timeout of individual queries
        /// </summary>
        /// <remarks>
        /// The overall timeout period is enforced by the scheduler.
        /// </remarks>
        [DataMember]
        public int QueryTimeout
        {
            get { return queryTimeout; }
            set { queryTimeout = value; }
        }

        /// <summary>
        /// Gets or sets whether SQL scripts are dumped to files during query execution.
        /// </summary>
        [IgnoreDataMember]
        public bool DumpSql
        {
            get { return dumpSql; }
            set { dumpSql = value; }
        }

        /// <summary>
        /// Gets or sets the registry context
        /// </summary>
        [IgnoreDataMember]
        public Context Context
        {
            get { return context; }
            set
            {
                UpdateContext(value);
            }
        }

        /// <summary>
        /// Gets the scheduler instance
        /// </summary>
        [IgnoreDataMember]
        public IScheduler Scheduler
        {
            get { return scheduler; }
        }

        /// <summary>
        /// Gets or sets the type name string of the query factory class
        /// </summary>
        [DataMember]
        public string QueryFactoryTypeName
        {
            get { return queryFactoryTypeName; }
            set { queryFactoryTypeName = value; }
        }

        /// <summary>
        /// Gets a query factory instance.
        /// </summary>
        [IgnoreDataMember]
        protected QueryFactory QueryFactory
        {
            get { return queryFactory.Value; }
        }

        /// <summary>
        /// Gets or sets the Federation.
        /// </summary>
        [DataMember]
        public EntityReference<Federation> FederationReference
        {
            get { return federationReference; }
            set { federationReference = value; }
        }

        /// <summary>
        /// Gets or sets the query string of the query job.
        /// </summary>
        [DataMember]
        public string QueryString
        {
            get { return queryString; }
            set { queryString = value; }
        }

        [DataMember]
        public string BatchName
        {
            get { return batchName; }
            set { batchName = value; }
        }

        [DataMember]
        public string QueryName
        {
            get { return queryName; }
            set { queryName = value; }
        }

        /// <summary>
        /// Gets or sets the default dataset, i.e. the one that's assumed
        /// when no dataset part is specified in table names.
        /// </summary>
        [DataMember]
        public SqlServerDataset DefaultDataset
        {
            get { return defaultDataset; }
            set { defaultDataset = value; }
        }

        /// <summary>
        /// Gets or sets the temporary dataset to be used to store temporary.
        /// tables.
        /// </summary>
        [IgnoreDataMember]
        public SqlServerDataset TemporaryDataset
        {
            get { return temporaryDataset; }
            set { temporaryDataset = value; }
        }

        /// <summary>
        /// Gets or sets the code database to be used by default to resolve function calls.
        /// </summary>
        [IgnoreDataMember]
        public SqlServerDataset CodeDataset
        {
            get { return codeDataset; }
            set { codeDataset = value; }
        }

        /// <summary>
        /// Gets a list of custom datasets.
        /// </summary>
        /// <remarks>
        /// In case of Graywulf execution mode, this stores
        /// the datasets not in the default list (remote datasets,
        /// for instance)
        /// </remarks>
        [IgnoreDataMember]
        public List<DatasetBase> CustomDatasets
        {
            get { return customDatasets; }
            private set { customDatasets = value; }
        }

        [DataMember(Name = "CustomDatasets")]
        [XmlArray]
        public DatasetBase[] CustomDatasets_ForXml
        {
            get { return customDatasets.ToArray(); }
            set { customDatasets = new List<DatasetBase>(value); }
        }

        /// <summary>
        /// Gets or sets query execution mode.
        /// </summary>
        /// <remarks>
        /// Graywulf or single server
        /// </remarks>
        [DataMember]
        public ExecutionMode ExecutionMode
        {
            get { return executionMode; }
            set { executionMode = value; }
        }

        /// <summary>
        /// Gets if the query has been canceled.
        /// </summary>
        [IgnoreDataMember]
        public bool IsCanceled
        {
            get { return isCanceled; }
        }


        /// <summary>
        /// Gets or sets the reference to the assigned server instance registry object.
        /// </summary>
        [DataMember]
        public EntityReference<ServerInstance> AssignedServerInstanceReference
        {
            get { return assignedServerInstanceReference; }
            set { assignedServerInstanceReference = value; }
        }

        /// <summary>
        /// Gets or sets the assigned server instance registry object.
        /// </summary>
        [IgnoreDataMember]
        public ServerInstance AssignedServerInstance
        {
            get { return assignedServerInstanceReference.Value; }
        }

        /// <summary>
        /// Gets or sets the root object of the query parsing tree.
        /// </summary>
        [IgnoreDataMember]
        public SelectStatement SelectStatement
        {
            get { return selectStatement; }
            protected set { selectStatement = value; }
        }

        /// <summary>
        /// Gets a reference to the temporary database instance registry object.
        /// </summary>
        [IgnoreDataMember]
        protected EntityReference<DatabaseInstance> TemporaryDatabaseInstanceReference
        {
            get { return temporaryDatabaseInstanceReference; }
        }

        /// <summary>
        /// Gets the list of temporary tables created during query execution.
        /// </summary>
        [IgnoreDataMember]
        public ConcurrentDictionary<string, Table> TemporaryTables
        {
            get { return temporaryTables; }
        }

        /// <summary>
        /// Gets the list of temporary views created during query execution.
        /// </summary>
        [IgnoreDataMember]
        public ConcurrentDictionary<string, View> TemporaryViews
        {
            get { return temporaryViews; }
        }

        /// <summary>
        /// Gets a reference to the code database instance registry object.
        /// </summary>
        [IgnoreDataMember]
        protected EntityReference<DatabaseInstance> CodeDatabaseInstanceReference
        {
            get { return codeDatabaseInstanceReference; }
        }

        #endregion
        #region Constructors and initializers

        public QueryObject()
        {
            InitializeMembers(new StreamingContext());
        }

        public QueryObject(Context context)
        {
            InitializeMembers(new StreamingContext());

            this.context = context;
        }

        public QueryObject(QueryObject old)
        {
            CopyMembers(old);
        }

        [OnDeserializing]
        private void InitializeMembers(StreamingContext context)
        {
            this.syncRoot = new object();

            this.queryTimeout = 60;
            this.dumpSql = false;

            this.context = null;
            this.scheduler = null;

            this.queryFactoryTypeName = null;
            this.queryFactory = new Lazy<QueryFactory>(() => (QueryFactory)Activator.CreateInstance(Type.GetType(queryFactoryTypeName)), false);

            this.federationReference = new EntityReference<Federation>(this);

            this.queryString = null;
            this.batchName = null;
            this.queryName = null;

            this.defaultDataset = null;
            this.temporaryDataset = null;
            this.codeDataset = null;
            this.customDatasets = new List<DatasetBase>();

            this.executionMode = ExecutionMode.SingleServer;

            this.isCanceled = false;
            this.cancelableTasks = new Dictionary<string, ICancelableTask>();

            this.assignedServerInstanceReference = new EntityReference<ServerInstance>(this);
            this.selectStatement = null;
            this.isInterpretFinished = false;

            this.temporaryDatabaseInstanceReference = new EntityReference<DatabaseInstance>(this);

            this.temporaryTables = new ConcurrentDictionary<string, Table>(SchemaManager.Comparer);
            this.temporaryViews = new ConcurrentDictionary<string, View>(SchemaManager.Comparer);

            this.codeDatabaseInstanceReference = new EntityReference<DatabaseInstance>(this);
        }

        [OnDeserialized]
        private void UpdateMembers(StreamingContext context)
        {
            this.federationReference.ReferencingObject = this;
            this.assignedServerInstanceReference.ReferencingObject = this;
            this.temporaryDatabaseInstanceReference.ReferencingObject = this;
            this.codeDatabaseInstanceReference.ReferencingObject = this;
        }

        private void CopyMembers(QueryObject old)
        {
            this.syncRoot = new object();

            this.queryTimeout = old.queryTimeout;
            this.dumpSql = old.dumpSql;

            this.context = old.context;
            this.scheduler = old.scheduler;

            this.queryFactoryTypeName = old.queryFactoryTypeName;
            this.queryFactory = new Lazy<QueryFactory>(() => (QueryFactory)Activator.CreateInstance(Type.GetType(queryFactoryTypeName)), false);

            this.federationReference = new EntityReference<Registry.Federation>(this, old.federationReference);

            this.queryString = old.queryString;
            this.batchName = old.batchName;
            this.queryName = old.queryName;

            this.defaultDataset = old.defaultDataset;
            this.temporaryDataset = old.temporaryDataset;
            this.codeDataset = old.codeDataset;
            this.customDatasets = new List<DatasetBase>(old.customDatasets);

            this.executionMode = old.executionMode;

            this.isCanceled = false;
            this.cancelableTasks = new Dictionary<string, ICancelableTask>();

            this.assignedServerInstanceReference = new EntityReference<ServerInstance>(this, old.assignedServerInstanceReference);
            this.selectStatement = null;
            this.isInterpretFinished = false;

            this.temporaryDatabaseInstanceReference = new EntityReference<DatabaseInstance>(this, old.temporaryDatabaseInstanceReference);

            this.temporaryTables = new ConcurrentDictionary<string, Table>(old.temporaryTables, SchemaManager.Comparer);
            this.temporaryViews = new ConcurrentDictionary<string, View>(old.temporaryViews, SchemaManager.Comparer);

            this.codeDatabaseInstanceReference = new EntityReference<DatabaseInstance>(this, old.codeDatabaseInstanceReference);
        }

        public abstract object Clone();

        #endregion

        /// <summary>
        /// Returnes a new registry context when in Graywulf execution mode.
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="activityContext"></param>
        /// <param name="connectionMode"></param>
        /// <param name="transactionMode"></param>
        /// <returns></returns>
        public Jhu.Graywulf.Registry.Context CreateContext(IGraywulfActivity activity, System.Activities.CodeActivityContext activityContext, Jhu.Graywulf.Registry.ConnectionMode connectionMode, Jhu.Graywulf.Registry.TransactionMode transactionMode)
        {
            switch (executionMode)
            {
                case Query.ExecutionMode.SingleServer:
                    return null;
                case Query.ExecutionMode.Graywulf:
                    return Jhu.Graywulf.Registry.ContextManager.Instance.CreateContext(activity, activityContext, connectionMode, transactionMode);
                default:
                    throw new NotImplementedException();
            }
        }

        protected virtual void UpdateContext(Context context)
        {
            this.context = context;
        }

        /// <summary>
        /// Initializes the query object by loading registry objects, if necessary.
        /// </summary>
        /// <param name="context"></param>
        public void InitializeQueryObject(Context context)
        {
            InitializeQueryObject(context, null, true);
        }

        /// <summary>
        /// Initializes the query object by loading registry objects, if necessary.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="scheduler"></param>
        public void InitializeQueryObject(Context context, IScheduler scheduler)
        {
            InitializeQueryObject(context, scheduler, false);
        }

        /// <summary>
        /// Initializes the query object by loading registry objects, if necessary.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="scheduler"></param>
        /// <param name="forceReinitialize"></param>
        public virtual void InitializeQueryObject(Context context, IScheduler scheduler, bool forceReinitialize)
        {
            lock (syncRoot)
            {
                if (context != null)
                {
                    UpdateContext(context);

                    switch (executionMode)
                    {
                        case ExecutionMode.SingleServer:
                            break;
                        case ExecutionMode.Graywulf:
                            LoadAssignedServerInstance(forceReinitialize);
                            LoadSystemDatabaseInstance(TemporaryDatabaseInstanceReference, (GraywulfDataset)TemporaryDataset, forceReinitialize);
                            LoadSystemDatabaseInstance(CodeDatabaseInstanceReference, (GraywulfDataset)CodeDataset, forceReinitialize);
                            LoadDatasets(forceReinitialize);

                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }

                if (scheduler != null)
                {
                    this.scheduler = scheduler;
                }

                Parse(forceReinitialize);
                Interpret(forceReinitialize);
                Validate();
            }
        }

        public void AssignServer(ServerInstance serverInstance)
        {
            assignedServerInstanceReference.Value = serverInstance;

            LoadSystemDatabaseInstance(TemporaryDatabaseInstanceReference, (GraywulfDataset)TemporaryDataset, true);
            LoadSystemDatabaseInstance(CodeDatabaseInstanceReference, (GraywulfDataset)CodeDataset, true);
        }

        #region Cluster registry query functions

        /// <summary>
        /// Returns local datasets that are required to execute the query.
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// The function only returns GraywulfDatasets.
        /// </remarks>
        public Dictionary<string, GraywulfDataset> FindRequiredDatasets()
        {
            var sc = GetSchemaManager();

            // Collect list of required databases
            var ds = new Dictionary<string, GraywulfDataset>(SchemaManager.Comparer);
            var trs = new List<TableReference>();

            foreach (var tr in selectStatement.EnumerateSourceTableReferences(true))
            {
                if (!tr.IsUdf && !tr.IsSubquery && !tr.IsComputed)
                {
                    // Filter out non-graywulf datasets
                    if (!ds.ContainsKey(tr.DatasetName) && (sc.Datasets[tr.DatasetName] is GraywulfDataset))
                    {
                        ds.Add(tr.DatasetName, (GraywulfDataset)sc.Datasets[tr.DatasetName]);
                    }
                }
            }

            return ds;
        }

        protected void LoadAssignedServerInstance(bool forceReinitialize)
        {
            if (!assignedServerInstanceReference.IsEmpty && forceReinitialize)
            {
                assignedServerInstanceReference.Value.GetConnectionString();
            }
        }

        protected void LoadDatasets(bool forceReinitialize)
        {
            switch (ExecutionMode)
            {
                case Query.ExecutionMode.Graywulf:

                    // Initialize temporary database
                    if (temporaryDataset == null || forceReinitialize)
                    {
                        var tempds = new GraywulfDataset(Context);
                        tempds.Name = Registry.Constants.TempDbName;
                        tempds.IsOnLinkedServer = false;
                        tempds.DatabaseVersionReference.Value = FederationReference.Value.TempDatabaseVersion;
                        tempds.CacheSchemaConnectionString();

                        temporaryDataset = tempds;
                    }

                    // Initialize code database
                    if (codeDataset == null || forceReinitialize)
                    {
                        var codeds = new GraywulfDataset(Context);
                        codeds.Name = Registry.Constants.CodeDbName;
                        codeds.IsOnLinkedServer = false;
                        codeds.DatabaseVersionReference.Value = FederationReference.Value.CodeDatabaseVersion;
                        codeds.CacheSchemaConnectionString();

                        codeDataset = codeds;
                    }

                    break;

                case Query.ExecutionMode.SingleServer:

                    // Initialize temporary database
                    if (temporaryDataset == null || forceReinitialize)
                    {
                        // TODO: implement this if necessary
                    }

                    // Initialize code database
                    if (codeDataset == null || forceReinitialize)
                    {
                        // TODO: implement this if necessary
                    }

                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        #endregion
        #region System database functions

        protected void LoadSystemDatabaseInstance(EntityReference<DatabaseInstance> databaseInstance, GraywulfDataset dataset, bool forceReinitialize)
        {
            if (!AssignedServerInstanceReference.IsEmpty && (databaseInstance.IsEmpty || forceReinitialize))
            {
                dataset.Context = Context;
                var dd = dataset.DatabaseVersionReference.Value.DatabaseDefinition;

                dd.LoadDatabaseInstances(false);
                foreach (var di in dd.DatabaseInstances.Values)
                {
                    di.Context = Context;
                }

                // Find database instance that is on the same machine
                try
                {
                    // TODO: only server instance and database definition is checked here, maybe database version would be better
                    databaseInstance.Value = dd.DatabaseInstances.Values.FirstOrDefault(ddi => ddi.ServerInstanceReference.Guid == AssignedServerInstance.Guid);
                    databaseInstance.Value.GetConnectionString();
                }
                catch (Exception ex)
                {
                    throw new Exception(
                        String.Format(
                            "Cannot find instance of system database: {0} ({1}) on server {2}/{3} ({4}).",
                            dataset.Name, dataset.DatabaseName,
                            AssignedServerInstance.Machine.Name, AssignedServerInstance.Name, AssignedServerInstance.GetCompositeName()),
                        ex); // TODO ***
                }
            }
            else if (AssignedServerInstanceReference.IsEmpty)
            {
                databaseInstance.Value = null;
            }
        }

        protected SqlConnectionStringBuilder GetSystemDatabaseConnectionString(CommandTarget target)
        {
            switch (ExecutionMode)
            {
                case ExecutionMode.SingleServer:
                    {
                        SqlServerDataset ds;

                        switch (target)
                        {
                            case CommandTarget.Code:
                                ds = (SqlServerDataset)codeDataset;
                                break;
                            case CommandTarget.Temp:
                                ds = (SqlServerDataset)temporaryDataset;
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        return new SqlConnectionStringBuilder(ds.ConnectionString);
                    }
                case ExecutionMode.Graywulf:
                    {
                        EntityReference<DatabaseInstance> di;

                        switch (target)
                        {
                            case CommandTarget.Code:
                                di = codeDatabaseInstanceReference;
                                break;
                            case CommandTarget.Temp:
                                di = temporaryDatabaseInstanceReference;
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        return di.Value.GetConnectionString();
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public SqlServerDataset GetCodeDatabaseDataset()
        {
            SqlServerDataset codeds;

            switch (ExecutionMode)
            {
                case ExecutionMode.SingleServer:
                    codeds = codeDataset;
                    break;
                case ExecutionMode.Graywulf:
                    // *** TODO: this throws null exception after persist and restore
                    codeds = codeDatabaseInstanceReference.Value.GetDataset();
                    break;
                default:
                    throw new NotImplementedException();
            }

            return codeds;
        }

        public SqlServerDataset GetTemporaryDatabaseDataset()
        {
            SqlServerDataset tempds;

            switch (ExecutionMode)
            {
                case ExecutionMode.SingleServer:
                    tempds = temporaryDataset;
                    break;
                case ExecutionMode.Graywulf:
                    // *** TODO: this throws null exception after persist and restore
                    tempds = temporaryDatabaseInstanceReference.Value.GetDataset();
                    break;
                default:
                    throw new NotImplementedException();
            }

            tempds.IsMutable = true;
            return tempds;
        }

        public virtual Table GetTemporaryTable(string tableName)
        {
            string tempname;
            var tempds = GetTemporaryDatabaseDataset();

            switch (executionMode)
            {
                case Jobs.Query.ExecutionMode.SingleServer:
                    tempname = String.Format("skyquerytemp_{0}", tableName);
                    break;
                case Jobs.Query.ExecutionMode.Graywulf:
                    tempname = String.Format("{0}_{1}_{2}", Context.UserName, Context.JobID, tableName);
                    break;
                default:
                    throw new NotImplementedException();
            }

            return new Table()
            {
                Dataset = tempds,
                DatabaseName = tempds.DatabaseName,
                SchemaName = tempds.DefaultSchemaName,
                TableName = tempname,
            };
        }

        #endregion
        #region Parsing functions

        /// <summary>
        /// Parses the query
        /// </summary>
        protected void Parse(bool forceReinitialize)
        {
            // Reparse only if needed
            if (selectStatement == null || forceReinitialize)
            {
                var parser = queryFactory.Value.CreateParser();
                selectStatement = (SelectStatement)parser.Execute(queryString);
            }
        }

        protected void Validate()
        {
            // Perform validation on the query string

            var validator = queryFactory.Value.CreateValidator();
            validator.Execute(selectStatement);
        }

        /// <summary>
        /// Interprets the parsed query
        /// </summary>
        protected bool Interpret(bool forceReinitialize)
        {
            if (!isInterpretFinished || forceReinitialize)
            {
                // --- Execute name resolution
                var nr = CreateNameResolver(forceReinitialize);
                nr.Execute(selectStatement);

                // --- Normalize where conditions
                var wcn = new SearchConditionNormalizer();
                wcn.Execute(selectStatement);

                FinishInterpret(forceReinitialize);

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Performes additional interpretation steps after the query has been parsed.
        /// </summary>
        /// <param name="forceReinitialize"></param>
        protected virtual void FinishInterpret(bool forceReinitialize)
        {
            this.isInterpretFinished = true;
        }

        /// <summary>
        /// Returns a schema manager, either the cached one, either a newly
        /// created one.
        /// </summary>
        /// <param name="clearCache"></param>
        /// <returns></returns>
        protected virtual SchemaManager GetSchemaManager()
        {
            var sc = CreateSchemaManager();

            // Add custom dataset defined by code
            foreach (var ds in customDatasets)
            {
                // *** TODO: check this
                sc.Datasets[ds.Name] = ds;
            }

            return sc;
        }

        /// <summary>
        /// Creates a SqlSchemaConnector that will look up and cache database table schema
        /// information for query parsing
        /// </summary>
        /// <returns>An initialized SqlSchemaConnector instance.</returns>
        /// <remarks>
        /// The function adds custom datasets (usually MYDBs or remote dataset) defined
        /// for the query job.
        /// </remarks>
        private SchemaManager CreateSchemaManager()
        {
            switch (executionMode)
            {
                case ExecutionMode.SingleServer:
                    return new Schema.SqlServer.SqlServerSchemaManager();
                case ExecutionMode.Graywulf:
                    return GraywulfSchemaManager.Create(federationReference.Value);
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Returns a new name resolver to be used with the parsed query string.
        /// </summary>
        /// <param name="forceReinitialize"></param>
        /// <returns></returns>
        protected virtual SqlNameResolver CreateNameResolver(bool forceReinitialize)
        {
            LoadDatasets(forceReinitialize);

            var nr = queryFactory.Value.CreateNameResolver();
            nr.SchemaManager = GetSchemaManager();

            nr.DefaultTableDatasetName = defaultDataset.Name;
            nr.DefaultFunctionDatasetName = codeDataset.Name;

            return nr;
        }

        #endregion
        #region Scheduler functions

        protected ServerInstance GetNextServerInstance(DatabaseDefinition databaseDefinition, string databaseVersion)
        {
            return GetNextServerInstance(databaseDefinition, databaseVersion);
        }

        protected ServerInstance GetNextServerInstance(DatabaseDefinition databaseDefinition, string databaseVersion, string surrogateDatabaseVersion)
        {
            Guid siguid;

            // Try with requested database version
            siguid = Scheduler.GetNextServerInstance(new Guid[] { databaseDefinition.Guid }, databaseVersion, null);

            // If not found, try with surrogate
            if (surrogateDatabaseVersion != null && siguid == Guid.Empty)
            {
                siguid = Scheduler.GetNextServerInstance(new Guid[] { databaseDefinition.Guid }, surrogateDatabaseVersion, null);
            }

            if (siguid == Guid.Empty)
            {
                throw new Exception("No server found with requested database.");  // *** TODO
            }

            var si = new ServerInstance(Context);
            si.Guid = siguid;
            si.Load();

            return si;
        }

        protected ServerInstance GetNextServerInstance(IEnumerable<DatabaseDefinition> databaseDefinitions, string databaseVersion, string surrogateDatabaseVersion, IEnumerable<DatabaseInstance> specificDatabaseInstances)
        {
            Guid[] dds, dis;
            Guid siguid;

            if (databaseDefinitions != null)
            {
                dds = databaseDefinitions.Select(i => i.Guid).ToArray();
            }
            else
            {
                dds = null;
            }

            if (specificDatabaseInstances != null)
            {
                dis = specificDatabaseInstances.Select(i => i.Guid).ToArray();
            }
            else
            {
                dis = null;
            }
            
            // Try with requested database version
            siguid = scheduler.GetNextServerInstance(dds, databaseVersion, dis);

            // If not found, try with surrogate
            // If not found, try with surrogate
            if (surrogateDatabaseVersion != null && siguid == Guid.Empty)
            {
                siguid = Scheduler.GetNextServerInstance(dds, surrogateDatabaseVersion, dis);
            }

            if (siguid == Guid.Empty)
            {
                throw new Exception("No server found with requested database.");  // *** TODO
            }

            var si = new ServerInstance(Context);
            si.Guid = siguid;
            si.Load();

            return si;
        }

        protected ServerInstance[] GetAvailableServerInstances(IEnumerable<DatabaseDefinition> databaseDefinitions, string databaseVersion, string surrogateDatabaseVersion, IEnumerable<DatabaseInstance> specificDatabaseInstances)
        {
            Guid[] dds, dis;
            Guid[] siguid;

            if (databaseDefinitions != null)
            {
                dds = databaseDefinitions.Select(i => i.Guid).ToArray();
            }
            else
            {
                dds = null;
            }

            if (specificDatabaseInstances != null)
            {
                dis = specificDatabaseInstances.Select(i => i.Guid).ToArray();
            }
            else
            {
                dis = null;
            }

            // Try with requested database version
            siguid = scheduler.GetServerInstances(dds, databaseVersion, dis);

            // If not found, try with surrogate
            // If not found, try with surrogate
            if (surrogateDatabaseVersion != null && (siguid == null || siguid.Length == 0))
            {
                siguid = scheduler.GetServerInstances(dds, surrogateDatabaseVersion, dis);

            }

            if (siguid == null || siguid.Length == 0)
            {
                throw new Exception("No server found with requested database.");  // *** TODO
            }

            var si = new ServerInstance[siguid.Length];
            for (int i = 0; i < siguid.Length; i++)
            {
                si[i] = new ServerInstance(Context);
                si[i].Guid = siguid[i];
                si[i].Load();
            }

            return si;
        }


        protected DatabaseInstance[] GetAvailableDatabaseInstances(DatabaseDefinition databaseDefinition, string databaseVersion)
        {
            return GetAvailableDatabaseInstances(databaseDefinition, databaseVersion, null);
        }

        protected DatabaseInstance[] GetAvailableDatabaseInstances(DatabaseDefinition databaseDefinition, string databaseVersion, string surrogateDatabaseVersion)
        {
            Guid[] diguid;

            // Try with requested database version
            diguid = Scheduler.GetDatabaseInstances(databaseDefinition.Guid, databaseVersion);

            // If not found, try with surrogate
            if (surrogateDatabaseVersion != null && (diguid == null || diguid.Length == 0))
            {
                diguid = Scheduler.GetDatabaseInstances(databaseDefinition.Guid, surrogateDatabaseVersion);
            }

            if (diguid == null || diguid.Length == 0)
            {
                throw new Exception("No instance of the requested database found.");  // *** TODO
            }

            var di = new DatabaseInstance[diguid.Length];
            for (int i = 0; i < diguid.Length; i++)
            {
                di[i] = new DatabaseInstance(Context);
                di[i].Guid = diguid[i];
                di[i].Load();
            }

            return di;
        }

        public DatabaseInstance[] GetAvailableDatabaseInstances(ServerInstance serverInstance, DatabaseDefinition databaseDefinition, string databaseVersion)
        {
            return GetAvailableDatabaseInstances(serverInstance, databaseDefinition, null);
        }


        public DatabaseInstance[] GetAvailableDatabaseInstances(ServerInstance serverInstance, DatabaseDefinition databaseDefinition, string databaseVersion, string surrogateDatabaseVersion)
        {
            Guid[] diguid;

            // Try with requested database version
            diguid = Scheduler.GetDatabaseInstances(serverInstance.Guid, databaseDefinition.Guid, databaseVersion);

            // If not found, try with surrogate
            if (surrogateDatabaseVersion != null && (diguid == null || diguid.Length == 0))
            {
                diguid = Scheduler.GetDatabaseInstances(databaseDefinition.Guid, surrogateDatabaseVersion);
            }

            if (diguid == null || diguid.Length == 0)
            {
                throw new Exception("No instance of the requested database found.");  // *** TODO
            }

            var di = new DatabaseInstance[diguid.Length];
            for (int i = 0; i < diguid.Length; i++)
            {
                di[i] = new DatabaseInstance(Context);
                di[i].Guid = diguid[i];
                di[i].Load();
            }

            return di;
        }

        #endregion
        #region Name substitution

        protected void SubstituteDatabaseNames(SelectStatement selectStatement, ServerInstance serverInstance, string databaseVersion)
        {
            SubstituteDatabaseNames(selectStatement, serverInstance, databaseVersion, null);
        }

        /// <summary>
        /// Looks up actual database instance names on the specified server instance
        /// </summary>
        /// <param name="serverInstance"></param>
        /// <param name="databaseVersion"></param>
        /// <remarks>This function call must be synchronized!</remarks>
        protected void SubstituteDatabaseNames(SelectStatement selectStatement, ServerInstance serverInstance, string databaseVersion, string surrogateDatabaseVersion)
        {
            switch (ExecutionMode)
            {
                case ExecutionMode.SingleServer:
                    // *** Nothing to do here?
                    break;
                case ExecutionMode.Graywulf:
                    {
                        var ef = new Jhu.Graywulf.Registry.EntityFactory(Context);

                        foreach (var tr in selectStatement.EnumerateSourceTableReferences(true))
                        {
                            if (!tr.IsUdf && !tr.IsSubquery && !tr.IsComputed)
                            {
                                SubstituteDatabaseName(tr, serverInstance, databaseVersion, surrogateDatabaseVersion);
                            }
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Substitutes the database name into a table reference.
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="serverInstance"></param>
        /// <param name="databaseVersion"></param>
        /// <remarks>
        /// During query executions, actual database name are not known until a server instance is
        /// assigned to the query partition.
        /// </remarks>
        protected void SubstituteDatabaseName(TableReference tr, ServerInstance serverInstance, string databaseVersion, string surrogateDatabaseVersion)
        {
            SchemaManager sc = GetSchemaManager();

            if (!tr.IsSubquery && !tr.IsComputed)
            {
                DatasetBase ds = sc.Datasets[tr.DatasetName];

                // Graywulf datasets have changing database names depending on the server
                // the database is on.
                if (ds is GraywulfDataset)
                {
                    var gwds = ds as GraywulfDataset;
                    gwds.Context = Context;

                    DatabaseInstance di;
                    if (gwds.IsSpecificInstanceRequired)
                    {
                        di = gwds.DatabaseInstanceReference.Value;
                    }
                    else
                    {
                        // Find appropriate database instance
                        var dis = GetAvailableDatabaseInstances(serverInstance, gwds.DatabaseDefinitionReference.Value, databaseVersion, surrogateDatabaseVersion);
                        di = dis[0];
                    }

                    // Refresh database object, now that the correct database name is set
                    ds = di.GetDataset();
                    tr.DatabaseName = di.DatabaseName;
                    tr.DatabaseObject = ds.GetObject(tr.DatabaseName, tr.SchemaName, tr.DatabaseObjectName);
                }
            }
        }

        /// <summary>
        /// Substitutes names of remote tables with name of temporary tables
        /// holding a cached version of remote tables.
        /// </summary>
        /// <remarks></remarks>
        // TODO: This function call must be synchronized! ??
        protected virtual void SubstituteRemoteTableNames(SelectStatement selectStatement, DatasetBase temporaryDataset, string temporarySchemaName)
        {
            switch (ExecutionMode)
            {
                case ExecutionMode.SingleServer:
                    // No remote table support

                    // Replace remote table references with temp table references
                    foreach (TableReference tr in selectStatement.EnumerateSourceTableReferences(true))
                    {
                        if (!tr.IsSubquery && TemporaryTables.ContainsKey(tr.UniqueName))
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    break;
                case ExecutionMode.Graywulf:
                    var sm = GetSchemaManager();

                    // Replace remote table references with temp table references
                    foreach (TableReference tr in selectStatement.EnumerateSourceTableReferences(true))
                    {
                        SubstituteRemoteTableName(sm, tr, temporaryDataset, temporarySchemaName);
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Substitutes the name of a remote tables with name of the temporary table
        /// holding a cached version of the remote data.
        /// </summary>
        /// <param name="sm"></param>
        /// <param name="tr"></param>
        /// <param name="temporaryDataset"></param>
        /// <param name="temporarySchemaName"></param>
        private void SubstituteRemoteTableName(SchemaManager sm, TableReference tr, DatasetBase temporaryDataset, string temporarySchemaName)
        {
            // Save unique name because it will change as names are substituted
            var un = tr.UniqueName;

            // TODO: write function to determine if a table is to be copied
            // ie. the condition in the if clause of the following line
            if (tr.IsCachable && TemporaryTables.ContainsKey(tr.UniqueName) &&
                IsRemoteDataset(sm.Datasets[tr.DatasetName]))
            {
                tr.DatabaseName = temporaryDataset.DatabaseName;
                tr.SchemaName = temporarySchemaName;
                tr.DatabaseObjectName = TemporaryTables[un].TableName;
                tr.DatabaseObject = null;
            }
        }

        /// <summary>
        /// Checks whether the given dataset is remote to the assigned server
        /// </summary>
        /// <param name="ds"></param>
        /// <returns></returns>
        protected bool IsRemoteDataset(DatasetBase ds)
        {
            if (ds is GraywulfDataset && !((GraywulfDataset)ds).IsSpecificInstanceRequired)
            {
                return false;
            }
            else if (ds is SqlServerDataset)
            {
                // A SqlServer dataset is remote if it's on another server
                var csbr = new SqlConnectionStringBuilder(ds.ConnectionString);
                var csba = AssignedServerInstance.GetConnectionString();

                return StringComparer.InvariantCultureIgnoreCase.Compare(csbr.DataSource, csba.DataSource) != 0;
            }
            else
            {
                // Everything else is remote
                return true;
            }
        }

        #endregion
        #region Actual query execution functions

        protected void ExecuteSqlCommand(string sql, CommandTarget target)
        {
            using (var cmd = new SqlCommand())
            {
                cmd.CommandText = sql;
                ExecuteSqlCommand(cmd, target);
            }
        }

        protected void ExecuteSqlCommand(SqlCommand cmd, CommandTarget target)
        {
            var csb = GetSystemDatabaseConnectionString(target);

            using (SqlConnection cn = new SqlConnection(csb.ConnectionString))
            {
                cn.Open();

                cmd.Connection = cn;
                cmd.CommandTimeout = queryTimeout;

                DumpSqlCommand(cmd);

                ExecuteLongCommandNonQuery(cmd);
            }
        }

        protected object ExecuteSqlCommandScalar(SqlCommand cmd, CommandTarget target)
        {
            var csb = GetSystemDatabaseConnectionString(target);

            using (var cn = new SqlConnection(csb.ConnectionString))
            {
                cn.Open();

                cmd.Connection = cn;
                cmd.CommandTimeout = queryTimeout;

                DumpSqlCommand(cmd);

                return ExecuteLongCommandScalar(cmd);
            }
        }

        protected void ExecuteSqlCommandReader(SqlCommand cmd, CommandTarget target, Action<IDataReader> action)
        {
            var csb = GetSystemDatabaseConnectionString(target);

            using (var cn = new SqlConnection(csb.ConnectionString))
            {
                cn.Open();

                cmd.Connection = cn;
                cmd.CommandTimeout = queryTimeout;

                DumpSqlCommand(cmd);

                ExecuteLongCommandReader(cmd, action);
            }
        }

        protected virtual string GetDumpFileName(CommandTarget target)
        {
            string server = GetSystemDatabaseConnectionString(target).DataSource;
            return String.Format("dump_{0}.sql", server);
        }

        protected void DumpSqlCommand(string sql, CommandTarget target)
        {
            if (dumpSql)
            {
                string filename = GetDumpFileName(target);
                var sw = new StringWriter();

                // Time stamp
                sw.WriteLine("-- {0}\r\n", DateTime.Now);
                sw.WriteLine(sql);
                sw.WriteLine("GO");
                sw.WriteLine();

                File.AppendAllText(filename, sw.ToString());
            }
        }

        private void DumpSqlCommand(SqlCommand cmd)
        {
            if (dumpSql)
            {
                var filename = GetDumpFileName(CommandTarget.Temp);
                var sw = new StringWriter();

                // Time stamp
                sw.WriteLine("-- {0}\r\n", DateTime.Now);

                // Database name
                var csb = new SqlConnectionStringBuilder(cmd.Connection.ConnectionString);

                if (!String.IsNullOrWhiteSpace(csb.InitialCatalog))
                {
                    sw.WriteLine("USE [{0}]", csb.InitialCatalog);
                    sw.WriteLine("GO");
                    sw.WriteLine();
                }

                // Command parameters
                foreach (SqlParameter par in cmd.Parameters)
                {
                    sw.WriteLine(String.Format("DECLARE {0} {1} = {2}",
                        par.ParameterName,
                        par.SqlDbType.ToString(),
                        par.Value.ToString()));
                }

                sw.WriteLine(cmd.CommandText);
                sw.WriteLine("GO");

                File.AppendAllText(filename, sw.ToString());
            }
        }

        #endregion
        #region Cancelable command execution

        public virtual void Execute()
        {
            throw new NotImplementedException();
        }

        protected void RegisterCancelable(Guid key, ICancelableTask task)
        {
            RegisterCancelable(key.ToString(), task);
        }

        protected void RegisterCancelable(string key, ICancelableTask task)
        {
            lock (cancelableTasks)
            {
                cancelableTasks.Add(key, task);
            }
        }

        protected void UnregisterCancelable(Guid key)
        {
            UnregisterCancelable(key.ToString());
        }

        protected void UnregisterCancelable(string key)
        {
            lock (cancelableTasks)
            {
                cancelableTasks.Remove(key);
            }
        }

        public virtual void Cancel()
        {
            if (isCanceled)
            {
                throw new InvalidOperationException(ExceptionMessages.TaskAlreadyCanceled);
            }

            lock (cancelableTasks)
            {
                foreach (var t in cancelableTasks.Values)
                {
                    t.Cancel();
                }
            }

            isCanceled = true;
        }

        #endregion
        #region Generic SQL functions with cancel support

        /// <summary>
        /// Executes a long SQL command in cancelable mode.
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="connectionString"></param>
        /// <param name="timeout"></param>
        protected void ExecuteLongCommandNonQuery(string sql, string connectionString, int timeout)
        {
            using (var cn = new SqlConnection(connectionString))
            {
                cn.Open();

                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.CommandTimeout = timeout;
                    ExecuteLongCommandNonQuery(cmd);
                }
            }
        }

        /// <summary>
        /// Executes a long SQL command in cancelable mode.
        /// </summary>
        /// <param name="cmd"></param>
        private void ExecuteLongCommandNonQuery(SqlCommand cmd)
        {
            var guid = Guid.NewGuid();
            var ccmd = new CancelableDbCommand(cmd);

            RegisterCancelable(guid, ccmd);

            try
            {
#if !SKIPQUERIES
                ccmd.ExecuteNonQuery();
#endif
            }
            finally
            {
                UnregisterCancelable(guid);
            }
        }

        private void ExecuteLongCommandNonQuery(string sql, SourceTableQuery source, int timeout)
        {
            using (var cn = new SqlConnection(source.Dataset.ConnectionString))
            {
                cn.Open();

                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.CommandTimeout = timeout;

                    foreach (var name in source.Parameters.Keys)
                    {
                        var par = cmd.CreateParameter();
                        par.ParameterName = name;
                        par.Value = source.Parameters[name];
                        cmd.Parameters.Add(par);
                    }

                    ExecuteLongCommandNonQuery(cmd);
                }
            }
        }

        /// <summary>
        /// Executes a long SQL command in cancelable mode.
        /// </summary>
        /// <param name="cmd"></param>
        /// <returns></returns>
        private object ExecuteLongCommandScalar(SqlCommand cmd)
        {
            var guid = Guid.NewGuid();
            var ccmd = new CancelableDbCommand(cmd);

            RegisterCancelable(guid, ccmd);

            try
            {
#if !SKIPQUERIES
                return ccmd.ExecuteScalar();
#else
            return 0;
#endif
            }
            finally
            {
                UnregisterCancelable(guid);
            }
        }

        /// <summary>
        /// Executes a long SQL command in cancelable mode.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="action"></param>
        private void ExecuteLongCommandReader(SqlCommand cmd, Action<IDataReader> action)
        {
            var guid = Guid.NewGuid();
            var ccmd = new CancelableDbCommand(cmd);

            RegisterCancelable(guid, ccmd);

            try
            {
                ccmd.ExecuteReader(action);
            }
            finally
            {
                UnregisterCancelable(guid);
            }
        }

        #endregion
        #region Specialized SQL manipulation function

        protected void ExecuteSelectInto(SourceTableQuery source, Table destination, int timeout)
        {
            string sql = String.Format(
                "SELECT __tablealias.* INTO [{0}].[{1}].[{2}] FROM ({3}) AS __tablealias",
                !String.IsNullOrWhiteSpace(destination.DatabaseName) ? destination.DatabaseName : destination.Dataset.DatabaseName,
                destination.SchemaName,
                destination.TableName,
                source.Query);

            ExecuteLongCommandNonQuery(sql, source, timeout);
        }

        protected void ExecuteInsertInto(SourceTableQuery source, Table destination, int timeout)
        {
            string sql = String.Format(
                "INSERT [{0}].[{1}].[{2}] WITH (TABLOCKX) SELECT __tablealias.* FROM ({3}) AS __tablealias",
                !String.IsNullOrWhiteSpace(destination.DatabaseName) ? destination.DatabaseName : destination.Dataset.DatabaseName,
                destination.SchemaName,
                destination.TableName,
                source.Query);

            ExecuteLongCommandNonQuery(sql, source, timeout);
        }

        /// <summary>
        /// Creates and initializes a remote or local table copy task
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="local"></param>
        /// <returns></returns>
        protected ICopyTable CreateTableCopyTask(SourceTableQuery source, DestinationTable destination, bool local)
        {
            var desthost = GetHostnameFromSqlConnectionString(destination.Dataset.ConnectionString);

            ICopyTable qi;

            if (local)
            {
                qi = new CopyTable();
            }
            else
            {
                qi = RemoteServiceHelper.CreateObject<ICopyTable>(desthost, true);
            }

            qi.Source = source;
            qi.Destination = destination;

            return qi;
        }

        private string GetHostnameFromSqlConnectionString(string connectionString)
        {
            // Determine server name from connection string
            // This is required, because bulk copy can go into databases that are only known
            // by their connection string
            // Get server name from data source name (requires trimming the sql server instance name)
            string host;

            var csb = new SqlConnectionStringBuilder(connectionString);
            int i = csb.DataSource.IndexOf('\\');
            if (i > -1)
            {
                host = csb.DataSource.Substring(i);
            }
            else
            {
                host = csb.DataSource;
            }

            try
            {
                // Do a reverse-lookup to get host name
                host = System.Net.Dns.GetHostEntry(host).HostName;
            }
            catch (Exception)
            {
                
            }

            return host;
        }

        #endregion
    }
}

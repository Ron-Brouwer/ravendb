﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Raven.Client;
using Raven.Client.Documents.Exceptions.Patching;
using Raven.Client.Util;
using Raven.Server.Documents.ETL.Providers.SQL.Connections;
using Raven.Server.Documents.ETL.Providers.SQL.Enumerators;
using Raven.Server.Documents.ETL.Providers.SQL.Metrics;
using Raven.Server.Documents.ETL.Providers.SQL.RelationalWriters;
using Raven.Server.Json;
using Raven.Server.NotificationCenter.Notifications;
using Raven.Server.NotificationCenter.Notifications.Details;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.ETL.Providers.SQL
{
    public class SqlEtl : EtlProcess<ToSqlItem, SqlTableWithRecords>
    {
        public const string SqlEtlTag = "SQL ETL";

        public SqlReplicationConfiguration SqlConfiguration { get; }

        private PredefinedSqlConnection _predefinedSqlConnection;

        public readonly SqlReplicationMetricsCountersManager MetricsCountersManager = new SqlReplicationMetricsCountersManager();

        public SqlEtl(DocumentDatabase database, SqlReplicationConfiguration configuration) : base(database, configuration, SqlEtlTag)
        {
            SqlConfiguration = configuration;
        }

        protected override IEnumerator<ToSqlItem> ConvertDocsEnumerator(IEnumerator<Document> docs)
        {
            return new DocumentsToSqlItems(docs);
        }

        protected override IEnumerator<ToSqlItem> ConvertTombstonesEnumerator(IEnumerator<DocumentTombstone> tombstones)
        {
            return new TombstonesToSqlItems(tombstones);
        }

        public override IEnumerable<SqlTableWithRecords> Transform(IEnumerable<ToSqlItem> items, DocumentsOperationContext context)
        {
            var patcher = new SqlReplicationPatchDocument(Database, context, SqlConfiguration);

            foreach (var toSqlItem in items)
            {
                CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    patcher.Transform(toSqlItem, context);

                    Statistics.TransformationSuccess();

                    if (CanContinueBatch() == false)
                        break;
                }
                catch (JavaScriptParseException e)
                {
                    Statistics.MarkTransformationScriptAsInvalid(SqlConfiguration.Script);

                    if (Logger.IsInfoEnabled)
                        Logger.Info($"Could not parse SQL ETL script for '{Name}'", e);

                    break;
                }
                catch (Exception diffExceptionName)
                {
                    Statistics.RecordScriptError(diffExceptionName);

                    if (Logger.IsInfoEnabled)
                        Logger.Info($"Could not process SQL ETL script for '{Name}', skipping document: {toSqlItem.DocumentKey}", diffExceptionName);
                }
            }

            return patcher.Tables.Values;
        }

        public override bool Load(IEnumerable<SqlTableWithRecords> records)
        {
            var hadWork = false;

            using (var writer = new RelationalDatabaseWriter(this, _predefinedSqlConnection, Database))
            {
                foreach (var table in records)
                {
                    try
                    {
                        hadWork = true;

                        var stats = writer.Write(table, CancellationToken);

                        HandleLoadStats(stats, table);
                    }
                    catch (Exception e)
                    {
                        if (Logger.IsInfoEnabled)
                            Logger.Info($"Failure to insert changes to relational database for '{Name}'", e);

                        DateTime newTime;
                        if (Statistics.LastErrorTime == null)
                        {
                            newTime = Database.Time.GetUtcNow().AddSeconds(5);
                        }
                        else
                        {
                            // double the fallback time (but don't cross 15 minutes)
                            var totalSeconds = (SystemTime.UtcNow - Statistics.LastErrorTime.Value).TotalSeconds;
                            newTime = SystemTime.UtcNow.AddSeconds(Math.Min(60 * 15, Math.Max(5, totalSeconds * 2)));
                        }

                        Statistics.RecordWriteError(e, table.Inserts.Count + table.Deletes.Count, newTime);
                    }
                }

                writer.Commit();
            }


            return hadWork;
        }

        private void HandleLoadStats(SqlWriteStats stats, SqlTableWithRecords table)
        {
            if (table.Inserts.Count > 0)
            {
                if (Logger.IsInfoEnabled)
                {
                    Logger.Info(
                        $"[{Name}] Inserted {stats.InsertedRecordsCount} (out of {table.Inserts.Count}) records to '{table.TableName}' table " +
                        $"from the following documents: {string.Join(", ", table.Inserts.Select(x => x.DocumentKey))}");
                }

                if (table.Inserts.Count == stats.InsertedRecordsCount)
                    Statistics.CompleteSuccess(stats.InsertedRecordsCount); // TODO arek
                else
                    Statistics.Success(stats.InsertedRecordsCount); // TODO arek
            }

            if (table.Deletes.Count > 0)
            {
                if (Logger.IsInfoEnabled)
                {
                    Logger.Info(
                        $"[{Name}] Deleted {stats.DeletedRecordsCount} (out of {table.Deletes.Count}) records from '{table.TableName}' table " +
                        $"for the following documents: {string.Join(", ", table.Inserts.Select(x => x.DocumentKey))}");
                }

                // TODO stats for deletes?
            }
        }

        public override bool CanContinueBatch()
        {
            return true; // TODO
        }

        public bool PrepareSqlReplicationConfig(BlittableJsonReaderObject connections, bool writeToLog = true)
        {
            if (string.IsNullOrWhiteSpace(SqlConfiguration.ConnectionStringName) == false)
            {
                object connection;
                if (connections.TryGetMember(SqlConfiguration.ConnectionStringName, out connection))
                {
                    _predefinedSqlConnection = JsonDeserializationServer.PredefinedSqlConnection(connection as BlittableJsonReaderObject);
                    if (_predefinedSqlConnection != null)
                    {
                        return true;
                    }
                }

                var message =
                    $"Could not find connection string named '{SqlConfiguration.ConnectionStringName}' for sql replication config: " +
                    $"{SqlConfiguration.Name}, ignoring sql replication setting.";

                if (writeToLog)
                {
                    if (Logger.IsInfoEnabled)
                        Logger.Info(message);
                }

                Statistics.LastAlert = AlertRaised.Create(Tag, message, AlertType.SqlReplication_ConnectionStringMissing, NotificationSeverity.Error);

                return false;
            }

            var emptyConnectionStringMsg =
                $"Connection string name cannot be empty for sql replication config: {SqlConfiguration.Name}, ignoring sql replication setting.";

            if (writeToLog)
            {
                if (Logger.IsInfoEnabled)
                    Logger.Info(emptyConnectionStringMsg);
            }

            Statistics.LastAlert = AlertRaised.Create(Tag, emptyConnectionStringMsg, AlertType.SqlReplication_ConnectionStringMissing, NotificationSeverity.Error);

            return false;
        }

        protected override void LoadLastProcessedEtag(DocumentsOperationContext context)
        {
            var sqlReplicationStatus = Database.DocumentsStorage.Get(context, Constants.Documents.SqlReplication.RavenSqlReplicationStatusPrefix + Name);
            if (sqlReplicationStatus == null)
            {
                Statistics.LastProcessedEtag = 0;
            }
            else
            {
                var replicationStatus = JsonDeserializationServer.SqlReplicationStatus(sqlReplicationStatus.Data);
                Statistics.LastProcessedEtag = replicationStatus.LastProcessedEtag;
            }
        }

        protected override void StoreLastProcessedEtag(DocumentsOperationContext context)
        {
            var key = Constants.Documents.SqlReplication.RavenSqlReplicationStatusPrefix + Name;
            var document = context.ReadObject(new DynamicJsonValue
            {
                [nameof(SqlReplicationStatus.Name)] = Name,
                [nameof(SqlReplicationStatus.LastProcessedEtag)] = Statistics.LastProcessedEtag
            }, key, BlittableJsonDocumentBuilder.UsageMode.ToDisk);

            Database.DocumentsStorage.Put(context, key, null, document);
        }

        public bool ValidateName()
        {
            if (string.IsNullOrWhiteSpace(SqlConfiguration.Name) == false)
                return true;

            var message = $"Could not find name for sql replication document {SqlConfiguration.Name}, ignoring";

            if (Logger.IsInfoEnabled)
                Logger.Info(message);

            Statistics.LastAlert = AlertRaised.Create(Tag, message, AlertType.SqlReplication_ConnectionStringMissing, NotificationSeverity.Error);
            return false;
        }

        public DynamicJsonValue Simulate(SimulateSqlReplication simulateSqlReplication, DocumentsOperationContext context, IEnumerable<SqlTableWithRecords> toWrite)
        {
            if (simulateSqlReplication.PerformRolledBackTransaction)
            {
                var summaries = new List<TableQuerySummary>();

                using (var writer = new RelationalDatabaseWriter(this, _predefinedSqlConnection, Database))
                {
                    foreach (var records in toWrite)
                    {
                        var commands = new List<DbCommand>();

                        writer.Write(records, CancellationToken, commands);

                        summaries.Add(TableQuerySummary.GenerateSummaryFromCommands(records.TableName, commands));
                    }

                    writer.Rollback();
                }

                return new DynamicJsonValue
                {
                    ["Results"] = new DynamicJsonArray(summaries.ToArray()),
                    ["LastAlert"] = Statistics.LastAlert,
                };
            }
            else
            {
                var tableQuerySummaries = new List<TableQuerySummary.CommandData>();

                var simulatedwriter = new RelationalDatabaseWriterSimulator(_predefinedSqlConnection, SqlConfiguration);

                foreach (var records in toWrite)
                {
                    var commands = simulatedwriter.SimulateExecuteCommandText(records, CancellationToken);

                    tableQuerySummaries.AddRange(commands.Select(x => new TableQuerySummary.CommandData
                    {
                        CommandText = x
                    }));
                }

                return new DynamicJsonValue
                {
                    ["Results"] = new DynamicJsonArray(tableQuerySummaries.ToArray()),
                    ["LastAlert"] = Statistics.LastAlert,
                };
            }
        }

        public static DynamicJsonValue SimulateSqlReplicationSqlQueries(SimulateSqlReplication simulateSqlReplication, DocumentDatabase database, DocumentsOperationContext context)
        {
            try
            {
                var document = database.DocumentsStorage.Get(context, simulateSqlReplication.DocumentId);

                using (var etl = new SqlEtl(database, simulateSqlReplication.Configuration))
                {
                    // TODO arek
                    //if (etl.PrepareSqlReplicationConfig(_connections, false) == false)
                    //{
                    //    return new DynamicJsonValue
                    //    {
                    //        ["LastAlert"] = etl.Statistics.LastAlert,
                    //    };
                    //}

                    var transformed = etl.Transform(new[] { new ToSqlItem(document) }, context);

                    return etl.Simulate(simulateSqlReplication, context, transformed);
                }
            }
            catch (Exception e)
            {
                return new DynamicJsonValue
                {
                    ["LastAlert"] =
                    AlertRaised.Create("SQL ETL",
                        $"Last SQL replication operation for {simulateSqlReplication.Configuration.Name} was failed",
                        AlertType.SqlReplication_Error,
                        NotificationSeverity.Error,
                        key: simulateSqlReplication.Configuration.Name,
                        details: new ExceptionDetails(e)).ToJson()
                };
            }
        }
    }
}
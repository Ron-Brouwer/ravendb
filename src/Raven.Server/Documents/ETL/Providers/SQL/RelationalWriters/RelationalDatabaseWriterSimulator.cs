using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using Raven.Server.Documents.ETL.Providers.SQL.Connections;
using Raven.Server.Documents.SqlReplication;

namespace Raven.Server.Documents.ETL.Providers.SQL.RelationalWriters
{
    public class RelationalDatabaseWriterSimulator : RelationalDatabaseWriterBase
    {
        private readonly SqlReplicationConfiguration _configuration;
        private readonly DbProviderFactory _providerFactory;
        private readonly DbCommandBuilder _commandBuilder;

        public RelationalDatabaseWriterSimulator(PredefinedSqlConnection predefinedSqlConnection, SqlReplicationConfiguration configuration) 
            : base(predefinedSqlConnection)
        {
            _configuration = configuration;
            _providerFactory = DbProviderFactories.GetFactory(predefinedSqlConnection.FactoryName);
            _commandBuilder = _providerFactory.CreateCommandBuilder();
        }

        public IEnumerable<string> SimulateExecuteCommandText(SqlTableWithRecords records, CancellationToken token)
        {
            foreach (var sqlReplicationTable in _configuration.SqlReplicationTables)
            {
                if (sqlReplicationTable.InsertOnlyMode)
                    continue;

                // first, delete all the rows that might already exist there
                foreach (string deleteQuery in GenerateDeleteItemsCommandText(sqlReplicationTable.TableName, sqlReplicationTable.DocumentKeyColumn, _configuration.ParameterizeDeletesDisabled,
                    records.Inserts, token))
                {
                    yield return deleteQuery;
                }
            }

            foreach (string insertQuery in GenerteInsertItemCommandText(records.TableName, records.DocumentKeyColumn, records.Inserts, token))
            {
                yield return insertQuery;
            }
        }

        private IEnumerable<string> GenerteInsertItemCommandText(string tableName, string pkName, List<ToSqlItem> dataForTable, CancellationToken token)
        {
            foreach (var itemToReplicate in dataForTable)
            {
                token.ThrowIfCancellationRequested();
                
                var sb = new StringBuilder("INSERT INTO ")
                        .Append(GetTableNameString(tableName))
                        .Append(" (")
                        .Append(_commandBuilder.QuoteIdentifier(pkName))
                        .Append(", ");
                foreach (var column in itemToReplicate.Columns)
                {
                    if (column.Key == pkName)
                        continue;
                    sb.Append(_commandBuilder.QuoteIdentifier(column.Key)).Append(", ");
                }
                sb.Length = sb.Length - 2;


                sb.Append(") VALUES (")
                    .Append(itemToReplicate.DocumentKey)
                    .Append(", ");

                foreach (var column in itemToReplicate.Columns)
                {
                    if (column.Key == pkName)
                        continue;
                     DbParameter param = new SqlParameter();
                     RelationalDatabaseWriter.SetParamValue(param, column, null);
                     sb.Append("'").Append(param.Value).Append("'").Append(", ");
                }
                sb.Length = sb.Length - 2;
                sb.Append(")");
                if (IsSqlServerFactoryType && _configuration.ForceSqlServerQueryRecompile)
                {
                    sb.Append(" OPTION(RECOMPILE)");
                }

                sb.Append(";");

                yield return sb.ToString();
            }
        }

        private IEnumerable<string> GenerateDeleteItemsCommandText(string tableName, string pkName, bool doNotParameterize, List<ToSqlItem> toSqlItems, CancellationToken token)
        {
            const int maxParams = 1000;

            token.ThrowIfCancellationRequested();

            for (int i = 0; i < toSqlItems.Count; i += maxParams)
            {

                var sb = new StringBuilder("DELETE FROM ")
                    .Append(GetTableNameString(tableName))
                    .Append(" WHERE ")
                    .Append(_commandBuilder.QuoteIdentifier(pkName))
                    .Append(" IN (");

                for (int j = i; j < Math.Min(i + maxParams, toSqlItems.Count); j++)
                {
                    if (i != j)
                        sb.Append(", ");
                    if (doNotParameterize == false)
                    {
                        sb.Append(toSqlItems[j]);
                    }
                    else
                    {
                        sb.Append("'").Append(RelationalDatabaseWriter.SanitizeSqlValue(toSqlItems[j].DocumentKey)).Append("'");
                    }

                }
                sb.Append(")");

                if (IsSqlServerFactoryType && _configuration.ForceSqlServerQueryRecompile)
                {
                    sb.Append(" OPTION(RECOMPILE)");
                }

                sb.Append(";");
                yield return sb.ToString();
            }
        }

        private string GetTableNameString(string tableName)
        {
            if (_configuration.QuoteTables)
            {
                return string.Join(".", tableName.Split('.').Select(x => _commandBuilder.QuoteIdentifier(x)).ToArray());
            }
            else
            {
                return tableName;
            }
        }
    }
}
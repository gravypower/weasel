using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;

namespace Weasel.Postgresql
{
    public static class CommandExtensions
    {
        public static NpgsqlParameter AddParameter(this NpgsqlCommand command, object value, NpgsqlDbType? dbType = null)
        {
            return PostgresqlProvider.Instance.AddParameter(command, value, dbType);
        }


        /// <summary>
        /// Finds or adds a new parameter with the specified name and returns the parameter
        /// </summary>
        /// <param name="command"></param>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="dbType"></param>
        /// <returns></returns>
        public static NpgsqlParameter AddNamedParameter(this NpgsqlCommand command, string name, object value, NpgsqlDbType? dbType = null)
        {
            return PostgresqlProvider.Instance.AddNamedParameter(command, name, value, dbType);
        }



        public static NpgsqlCommand With(this NpgsqlCommand command, string name, string[] value)
        {
            PostgresqlProvider.Instance.AddNamedParameter(command, name, value, NpgsqlDbType.Array | NpgsqlDbType.Varchar);
            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, int[] value)
        {
            PostgresqlProvider.Instance.AddNamedParameter(command, name, value, NpgsqlDbType.Array | NpgsqlDbType.Integer);
            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, long[] value)
        {
            PostgresqlProvider.Instance.AddNamedParameter(command, name, value, NpgsqlDbType.Array | NpgsqlDbType.Bigint);
            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, Guid[] value)
        {
            PostgresqlProvider.Instance.AddNamedParameter(command, name, value, NpgsqlDbType.Array | NpgsqlDbType.Uuid);
            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, DateTime value)
        {
            PostgresqlProvider.Instance.AddNamedParameter(command, name, value, NpgsqlDbType.Timestamp);
            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, DateTimeOffset value)
        {
            PostgresqlProvider.Instance.AddNamedParameter(command, name, value, NpgsqlDbType.TimestampTz);
            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, object value, NpgsqlDbType dbType)
        {
            PostgresqlProvider.Instance.AddNamedParameter(command, name, value, dbType);
            return command;
        }


        public static NpgsqlCommand Returns(this NpgsqlCommand command, string name, NpgsqlDbType type)
        {
            var parameter = command.AddParameter(name);
            parameter.ParameterName = name;
            parameter.NpgsqlDbType = type;
            parameter.Direction = ParameterDirection.ReturnValue;
            return command;
        }

        public static NpgsqlCommand CreateCommand(this NpgsqlConnection conn, string command, NpgsqlTransaction? tx = null)
        {
            return new NpgsqlCommand(command, conn)
            {
                Transaction = tx
            };
        }

        /// <summary>
        /// Calls pg_terminate_backend to kill off all idle sessions. USE CAUTIOUSLY!
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="databaseName"></param>
        /// <returns></returns>
        public static Task KillIdleSessions(this NpgsqlConnection conn, string databaseName)
        {
            return conn.CreateCommand(
                    $"SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = :db AND pid <> pg_backend_pid();")
                .With("db", databaseName)
                .ExecuteNonQueryAsync();
        }

        public static Task DropDatabase(this NpgsqlConnection conn, string databaseName)
        {
            return conn.CreateCommand($"DROP DATABASE IF EXISTS {databaseName}")
                .ExecuteNonQueryAsync();
        }

        public static async Task<bool> DatabaseExists(this NpgsqlConnection conn, string databaseName)
        {
            var name = await conn.CreateCommand("SELECT datname FROM pg_database where datname = :db")
                .With("db", databaseName).ExecuteScalarAsync().ConfigureAwait(false);

            return name != null;
        }

        public static Task<IReadOnlyList<string>> AllDatabaseNames(this NpgsqlConnection conn)
        {
            return conn
                .CreateCommand("SELECT datname FROM pg_database").FetchList<string>();
        }

        /// <summary>
        /// Call a Postgresql function by name
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="functionName"></param>
        /// <returns></returns>
        public static NpgsqlCommand CallFunction(this NpgsqlConnection conn, string functionName)
        {
            var cmd = conn.CreateCommand(functionName);
            cmd.CommandType = CommandType.StoredProcedure;
            return cmd;
        }

        /// <summary>
        /// Call a Postgresql function by name
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="functionName"></param>
        /// <returns></returns>
        public static NpgsqlCommand CallFunction(this NpgsqlConnection conn, DbObjectName functionName)
        {
            var cmd = conn.CreateCommand(functionName.QualifiedName);
            cmd.CommandType = CommandType.StoredProcedure;
            return cmd;
        }
    }
}

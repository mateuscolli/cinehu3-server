#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using Jellyfin.Extensions;
using Microsoft.Extensions.Logging;
using SQLitePCL.pretty;

namespace Emby.Server.Implementations.Data
{
    public abstract class BaseSqliteRepository : IDisposable
    {
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseSqliteRepository"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        protected BaseSqliteRepository(ILogger<BaseSqliteRepository> logger)
        {
            Logger = logger;
        }

        /// <summary>
        /// Gets or sets the path to the DB file.
        /// </summary>
        protected string DbFilePath { get; set; }

        /// <summary>
        /// Gets or sets the number of write connections to create.
        /// </summary>
        /// <value>Path to the DB file.</value>
        protected int WriteConnectionsCount { get; set; } = 1;

        /// <summary>
        /// Gets or sets the number of read connections to create.
        /// </summary>
        protected int ReadConnectionsCount { get; set; } = 1;

        /// <summary>
        /// Gets the logger.
        /// </summary>
        /// <value>The logger.</value>
        protected ILogger<BaseSqliteRepository> Logger { get; }

        /// <summary>
        /// Gets the default connection flags.
        /// </summary>
        /// <value>The default connection flags.</value>
        protected virtual ConnectionFlags DefaultConnectionFlags => ConnectionFlags.NoMutex;

        /// <summary>
        /// Gets the transaction mode.
        /// </summary>
        /// <value>The transaction mode.</value>>
        protected TransactionMode TransactionMode => TransactionMode.Deferred;

        /// <summary>
        /// Gets the transaction mode for read-only operations.
        /// </summary>
        /// <value>The transaction mode.</value>
        protected TransactionMode ReadTransactionMode => TransactionMode.Deferred;

        /// <summary>
        /// Gets the cache size.
        /// </summary>
        /// <value>The cache size or null.</value>
        protected virtual int? CacheSize => null;

        /// <summary>
        /// Gets the locking mode. <see href="https://www.sqlite.org/pragma.html#pragma_locking_mode" />.
        /// </summary>
        protected virtual string LockingMode => "NORMAL";

        /// <summary>
        /// Gets the journal mode. <see href="https://www.sqlite.org/pragma.html#pragma_journal_mode" />.
        /// </summary>
        /// <value>The journal mode.</value>
        protected virtual string JournalMode => "WAL";

        /// <summary>
        /// Gets the journal size limit. <see href="https://www.sqlite.org/pragma.html#pragma_journal_size_limit" />.
        /// </summary>
        /// <value>The journal size limit.</value>
        protected virtual int? JournalSizeLimit => 0;

        /// <summary>
        /// Gets the page size.
        /// </summary>
        /// <value>The page size or null.</value>
        protected virtual int? PageSize => null;

        /// <summary>
        /// Gets the temp store mode.
        /// </summary>
        /// <value>The temp store mode.</value>
        /// <see cref="TempStoreMode"/>
        protected virtual TempStoreMode TempStore => TempStoreMode.Memory;

        /// <summary>
        /// Gets the synchronous mode.
        /// </summary>
        /// <value>The synchronous mode or null.</value>
        /// <see cref="SynchronousMode"/>
        protected virtual SynchronousMode? Synchronous => SynchronousMode.Normal;

        /// <summary>
        /// Gets or sets the write lock.
        /// </summary>
        /// <value>The write lock.</value>
        protected ConnectionPool WriteConnections { get; set; }

        /// <summary>
        /// Gets or sets the write connection.
        /// </summary>
        /// <value>The write connection.</value>
        protected ConnectionPool ReadConnections { get; set; }

        public virtual void Initialize()
        {
            WriteConnections = new ConnectionPool(WriteConnectionsCount, CreateWriteConnection);
            ReadConnections = new ConnectionPool(ReadConnectionsCount, CreateReadConnection);

            // Configuration and pragmas can affect VACUUM so it needs to be last.
            using (var connection = GetConnection())
            {
                connection.Execute("VACUUM");
            }
        }

        protected ManagedConnection GetConnection(bool readOnly = false)
            => readOnly ? ReadConnections.GetConnection() : WriteConnections.GetConnection();

        protected SQLiteDatabaseConnection CreateWriteConnection()
        {
            var writeConnection = SQLite3.Open(
                DbFilePath,
                DefaultConnectionFlags | ConnectionFlags.Create | ConnectionFlags.ReadWrite,
                null);

            if (CacheSize.HasValue)
            {
                writeConnection.Execute("PRAGMA cache_size=" + CacheSize.Value);
            }

            if (!string.IsNullOrWhiteSpace(LockingMode))
            {
                writeConnection.Execute("PRAGMA locking_mode=" + LockingMode);
            }

            if (!string.IsNullOrWhiteSpace(JournalMode))
            {
                writeConnection.Execute("PRAGMA journal_mode=" + JournalMode);
            }

            if (JournalSizeLimit.HasValue)
            {
                writeConnection.Execute("PRAGMA journal_size_limit=" + JournalSizeLimit.Value);
            }

            if (Synchronous.HasValue)
            {
                writeConnection.Execute("PRAGMA synchronous=" + (int)Synchronous.Value);
            }

            if (PageSize.HasValue)
            {
                writeConnection.Execute("PRAGMA page_size=" + PageSize.Value);
            }

            writeConnection.Execute("PRAGMA temp_store=" + (int)TempStore);

            return writeConnection;
        }

        protected SQLiteDatabaseConnection CreateReadConnection()
        {
            var connection = SQLite3.Open(
                DbFilePath,
                DefaultConnectionFlags | ConnectionFlags.ReadOnly,
                null);

            if (CacheSize.HasValue)
            {
                connection.Execute("PRAGMA cache_size=" + CacheSize.Value);
            }

            if (!string.IsNullOrWhiteSpace(LockingMode))
            {
                connection.Execute("PRAGMA locking_mode=" + LockingMode);
            }

            if (!string.IsNullOrWhiteSpace(JournalMode))
            {
                connection.Execute("PRAGMA journal_mode=" + JournalMode);
            }

            if (JournalSizeLimit.HasValue)
            {
                connection.Execute("PRAGMA journal_size_limit=" + JournalSizeLimit.Value);
            }

            if (Synchronous.HasValue)
            {
                connection.Execute("PRAGMA synchronous=" + (int)Synchronous.Value);
            }

            connection.Execute("PRAGMA temp_store=" + (int)TempStore);

            return connection;
        }

        public IStatement PrepareStatement(ManagedConnection connection, string sql)
            => connection.PrepareStatement(sql);

        public IStatement PrepareStatement(IDatabaseConnection connection, string sql)
            => connection.PrepareStatement(sql);

        protected bool TableExists(ManagedConnection connection, string name)
        {
            return connection.RunInTransaction(
                db =>
                {
                    using (var statement = PrepareStatement(db, "select DISTINCT tbl_name from sqlite_master"))
                    {
                        foreach (var row in statement.ExecuteQuery())
                        {
                            if (string.Equals(name, row.GetString(0), StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                },
                ReadTransactionMode);
        }

        protected List<string> GetColumnNames(IDatabaseConnection connection, string table)
        {
            var columnNames = new List<string>();

            foreach (var row in connection.Query("PRAGMA table_info(" + table + ")"))
            {
                if (row.TryGetString(1, out var columnName))
                {
                    columnNames.Add(columnName);
                }
            }

            return columnNames;
        }

        protected void AddColumn(IDatabaseConnection connection, string table, string columnName, string type, List<string> existingColumnNames)
        {
            if (existingColumnNames.Contains(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            connection.Execute("alter table " + table + " add column " + columnName + " " + type + " NULL");
        }

        protected void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name, "Object has been disposed and cannot be accessed.");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool dispose)
        {
            if (_disposed)
            {
                return;
            }

            if (dispose)
            {
                WriteConnections.Dispose();
                ReadConnections.Dispose();
            }

            _disposed = true;
        }
    }
}

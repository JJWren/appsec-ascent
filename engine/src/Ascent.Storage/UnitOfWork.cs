using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>
/// One transaction for one command's state changes (REL-U2-01). It starts with <c>BEGIN IMMEDIATE</c>, so the write
/// lock is taken up front and a later upgrade can't fail. Disposing without <see cref="Commit"/> rolls back.
/// </summary>
public sealed class UnitOfWork : IDisposable
{
    internal UnitOfWork(SqliteConnection connection)
    {
        Connection = connection;
        Transaction = connection.BeginTransaction(deferred: false);
    }

    /// <summary>The connection.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>The transaction.</summary>
    public SqliteTransaction Transaction { get; }

    /// <summary>Creates a command inside this transaction.</summary>
    public SqliteCommand Command() => Statements.Command(Connection, Transaction);

    /// <summary>Commits the transaction.</summary>
    public void Commit() => Transaction.Commit();

    /// <inheritdoc />
    public void Dispose() => Transaction.Dispose();
}

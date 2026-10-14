using MCPServer.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MCPServer.Infrastructure.Data;

public abstract class BaseUnitOfWork : IUnitOfWork, IDisposable
{
    private bool disposed = false;
    protected readonly DbContext DbContext;
    protected IDbContextTransaction Transaction;
    public BaseUnitOfWork(DbContext dbContext)
    {
        DbContext = dbContext;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                DbContext.Dispose();
            }
        }
        this.disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if(Transaction is null)
            Transaction = await DbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (Transaction is null)
                throw new InvalidOperationException("CommitAsync should be called only after OpenTransactionAsync method");
            await DbContext.SaveChangesAsync(cancellationToken);
            await Transaction!.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction != null)
        {
            await Transaction.RollbackAsync(cancellationToken);
            await DisposeTransactionAsync();
        }

    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DisposeTransactionAsync()
    {
        if (Transaction != null)
        {
            await Transaction.DisposeAsync();
            Transaction = null;
        }
    }
}
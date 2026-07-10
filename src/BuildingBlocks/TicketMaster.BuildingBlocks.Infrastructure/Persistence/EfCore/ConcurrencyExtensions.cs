using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore;

public static class ConcurrencyExtensions
{
    /// <summary>
    /// Uses Postgres's built-in <c>xmin</c> system column as the optimistic-concurrency token — no extra column and no
    /// manual rowversion to bump. A shadow <c>uint</c> row-version property is mapped by Npgsql's convention onto the
    /// system <c>xmin</c> column (never emitted as a migration). EF then appends <c>WHERE xmin = @original</c> to
    /// updates/deletes and raises <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> on a lost
    /// update. Call from an entity's configuration: <c>builder.HasSystemConcurrency();</c>.
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasSystemConcurrency<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<uint>("xmin").IsRowVersion();
        return builder;
    }
}

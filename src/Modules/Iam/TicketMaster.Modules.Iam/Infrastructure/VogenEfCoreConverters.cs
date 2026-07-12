using TicketMaster.Modules.Iam.Domain;
using Vogen;

namespace TicketMaster.Modules.Iam.Infrastructure;

/// <summary>
/// Marker for Vogen's source-generated EF Core converter registration. Vogen emits a
/// <c>RegisterAllInVogenEfCoreConverters()</c> extension from these attributes (reflection-free); add each new
/// strongly-typed id here so <c>IamDbContext.ConfigureConventions</c> wires its converter.
/// </summary>
[EfCoreConverter<UserId>]
[EfCoreConverter<RoleId>]
internal sealed partial class VogenEfCoreConverters;

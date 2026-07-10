using TicketMaster.BuildingBlocks.Domain;
using TicketMaster.BuildingBlocks.Domain.Auditing;
using YC.Monad;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>
/// A role: a named bundle of permissions (<c>resource:action</c> strings). Roles are the source of truth for
/// authorization; the role → permission mapping is what the cached permission resolver reads at request time, so
/// the JWT need only carry role names, never a frozen permission list. Construct via <see cref="Create"/>.
/// </summary>
public sealed class Role : AggregateRoot<RoleId>, IAuditable
{
    private readonly HashSet<string> _permissions = new(StringComparer.Ordinal);

    private Role() { } // EF

    private Role(RoleId id, string name) : base(id) => Name = name;

    public string Name { get; private set; } = string.Empty;
    public IReadOnlyCollection<string> Permissions => _permissions;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;

    public static Result<Role> Create(string name, IEnumerable<string> permissions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Create("role.name_required", "Role name is required.", 400);
        if (name.Length > 100)
            return Error.Create("role.name_too_long", "Role name must be at most 100 characters.", 400);

        var role = new Role(RoleId.New(), name.Trim());
        if (permissions is not null)
            foreach (var permission in permissions)
                role._permissions.Add(permission);

        return role;
    }

    public void Grant(string permission) => _permissions.Add(permission);

    public void Revoke(string permission) => _permissions.Remove(permission);
}

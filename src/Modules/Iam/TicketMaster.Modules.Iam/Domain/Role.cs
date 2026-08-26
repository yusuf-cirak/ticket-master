using BuildingBlocks.Domain;
using BuildingBlocks.Domain.Auditing;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>
/// A role: a named bundle of permissions (<c>resource:action</c> strings). Roles are the source of truth for
/// authorization; the role → permission mapping is what the cached permission resolver reads at request time, so
/// the JWT need only carry role names, never a frozen permission list. Construct via <see cref="Create"/>.
/// </summary>
public sealed class Role : AuditableAggregateRoot<RoleId>
{
    // A list (not a set) because EF primitive collections must be an array/IList; uniqueness is kept via Grant/Create.
    private readonly List<string> _permissions = [];

    private Role() { } // EF

    private Role(string name) => Name = name; // Id is a database-generated int identity, assigned on insert

    public string Name { get; private set; } = string.Empty;
    public IReadOnlyCollection<string> Permissions => _permissions;

    public static Result<Role> Create(string name, IEnumerable<string> permissions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Create("role.name_required", "Role name is required.", 400);
        if (name.Length > 100)
            return Error.Create("role.name_too_long", "Role name must be at most 100 characters.", 400);

        var role = new Role(name.Trim());
        if (permissions is not null)
            foreach (var permission in permissions)
                role.Grant(permission);

        return role;
    }

    public void Grant(string permission)
    {
        if (!_permissions.Contains(permission))
            _permissions.Add(permission);
    }

    public void Revoke(string permission) => _permissions.Remove(permission);
}

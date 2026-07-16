using Microsoft.AspNetCore.Identity;
using BuildingBlocks.Domain.ValueObjects;

namespace TicketMaster.Modules.Iam.Infrastructure;

/// <summary>
/// Identity lookup normalizer that keys accounts on <see cref="Email.Canonical"/> (the Gmail dot/plus-folded form)
/// rather than the raw upper-cased address. This is the single place Identity canonicalizes emails, so
/// <see cref="UserManager{TUser}.CreateAsync(TUser,string)"/> and <see cref="UserManager{TUser}.FindByEmailAsync"/>
/// agree with the <c>User</c> aggregate's canonical uniqueness key — CreateAsync's recompute no longer overrides it,
/// and aliases (dots/+tags on Gmail) resolve to one account. Names keep the default upper-invariant form.
/// </summary>
public sealed class CanonicalEmailNormalizer : ILookupNormalizer
{
    public string NormalizeName(string name) => name?.Normalize().ToUpperInvariant();

    public string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return email;

        var result = Email.Create(email);
        return (result.IsSuccess ? result.Value.Canonical : email).ToUpperInvariant();
    }
}

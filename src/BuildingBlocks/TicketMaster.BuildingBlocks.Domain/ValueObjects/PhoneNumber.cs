using System.Text.RegularExpressions;
using Vogen;

namespace TicketMaster.BuildingBlocks.Domain.ValueObjects;

/// <summary>
/// A phone number in E.164 form (e.g. <c>+14155552671</c>). Formatting (spaces, dashes, parentheses) is stripped on
/// input, then the result is validated. Vogen generates value equality, the System.Text.Json converter, and IParsable.
/// (No EF converter here — the domain assembly does not reference EF Core; configure a conversion where persisted.)
/// </summary>
[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public readonly partial struct PhoneNumber
{
    private static string NormalizeInput(string input) =>
        input is null ? string.Empty : FormattingChars().Replace(input.Trim(), string.Empty);

    private static Validation Validate(string value) =>
        E164().IsMatch(value)
            ? Validation.Ok
            : Validation.Invalid("Phone number must be in E.164 format, e.g. +14155552671.");

    [GeneratedRegex(@"^\+[1-9]\d{1,14}$")]
    private static partial Regex E164();

    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex FormattingChars();
}

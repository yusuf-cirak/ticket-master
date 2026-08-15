using System.Text.Json.Serialization;
using BuildingBlocks.Application.Pagination;
using TicketMaster.Modules.Events.Application.Commands;
using TicketMaster.Modules.Events.Application.Queries;
using TicketMaster.Modules.Iam.Application.Features.Authentication;
using TicketMaster.Modules.Iam.Application.Features.Authentication.Commands;
using TicketMaster.Modules.Iam.Application.Features.Authentication.Queries;
using TicketMaster.Modules.Platform;

namespace TicketMaster.AppHost;

/// <summary>
/// System.Text.Json source-generated metadata for the HTTP-boundary DTOs, chained ahead of the reflection resolver
/// (see Program.cs) so a listed type skips per-call reflection metadata discovery while anything unlisted (anonymous
/// projections, ProblemDetails, future DTOs) still falls back to reflection — incremental, never a hard failure.
/// <para>
/// <see cref="JsonSourceGenerationMode.Metadata"/> (not Serialization) is deliberate: serialization keeps going
/// through the shared runtime options, so the global contract — snake_case-upper enums, omit-null, reject-unmapped,
/// MaxDepth (see <c>AddJsonDefaults</c>) — and the Vogen id converters still apply; only the type metadata is
/// precompiled. Adding a boundary DTO = one <c>[JsonSerializable]</c> line (nested types are pulled in automatically).
/// </para>
/// </summary>
// Each module nests its request as `Command` / `Response`, so disambiguate the generated metadata property names.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
// Events
[JsonSerializable(typeof(ScheduleEventCommand.Command), TypeInfoPropertyName = "ScheduleEventCommand")]
[JsonSerializable(typeof(ScheduleEventCommand.Response), TypeInfoPropertyName = "ScheduleEventResponse")]
[JsonSerializable(typeof(GetEventQuery.Detail))]
[JsonSerializable(typeof(PagedResult<ListEventsQuery.Item>))]
// Iam
[JsonSerializable(typeof(RegisterCommand.Command), TypeInfoPropertyName = "RegisterCommand")]
[JsonSerializable(typeof(RegisterCommand.Response), TypeInfoPropertyName = "RegisterResponse")]
[JsonSerializable(typeof(LoginCommand.Command), TypeInfoPropertyName = "LoginCommand")]
[JsonSerializable(typeof(RefreshCommand.Command), TypeInfoPropertyName = "RefreshCommand")]
[JsonSerializable(typeof(LogoutCommand.Command), TypeInfoPropertyName = "LogoutCommand")]
[JsonSerializable(typeof(GetMeQuery.Response), TypeInfoPropertyName = "GetMeResponse")]
[JsonSerializable(typeof(AuthTokensResponse))]
// Platform
[JsonSerializable(typeof(PingRequest))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

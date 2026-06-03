ARG BUILD_CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    TZ=Etc/UTC
# curl is used by the compose healthcheck (which gates Debezium startup on the app being up + migrated).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION
WORKDIR /src
# bin/obj are excluded via .dockerignore; copy sources then restore+publish the host (pulls its
# ProjectReferences across BuildingBlocks/Modules).
COPY . .
RUN dotnet restore src/Host/TicketMaster.AppHost/TicketMaster.AppHost.csproj
RUN dotnet publish src/Host/TicketMaster.AppHost/TicketMaster.AppHost.csproj \
    -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TicketMaster.AppHost.dll"]

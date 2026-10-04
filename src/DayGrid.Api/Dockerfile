# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files first and restore, so Docker layer caching only re-restores when a project
# reference or package list actually changes.
COPY DayGrid.sln .
COPY src/DayGrid.Domain/DayGrid.Domain.csproj src/DayGrid.Domain/
COPY src/DayGrid.Application/DayGrid.Application.csproj src/DayGrid.Application/
COPY src/DayGrid.Infrastructure/DayGrid.Infrastructure.csproj src/DayGrid.Infrastructure/
COPY src/DayGrid.Api/DayGrid.Api.csproj src/DayGrid.Api/
COPY tests/DayGrid.UnitTests/DayGrid.UnitTests.csproj tests/DayGrid.UnitTests/

RUN dotnet restore src/DayGrid.Api/DayGrid.Api.csproj

# Now copy the rest of the source and publish.
COPY src/ src/
RUN dotnet publish src/DayGrid.Api/DayGrid.Api.csproj -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DayGrid.Api.dll"]

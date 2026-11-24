FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["server/Duo.slnx", "server/"]
COPY ["server/Duo.Api/Duo.Api.csproj", "server/Duo.Api/"]
COPY ["server/Duo.Domain/Duo.Domain.csproj", "server/Duo.Domain/"]
COPY ["server/Duo.Domain.Tests/Duo.Domain.Tests.csproj", "server/Duo.Domain.Tests/"]

RUN dotnet restore "server/Duo.slnx"

COPY server/ server/
WORKDIR /src/server/Duo.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN useradd -u 1000 -ms /bin/bash appuser && chown -R appuser:appuser /app
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "Duo.Api.dll"]

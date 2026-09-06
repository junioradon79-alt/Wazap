# WAZAP — image OCI (multi-stage) pour hébergement PaaS (Render/Railway/Azure…).
# L'app publie le SPA et le Blazor depuis src/Wazap.API/wwwroot (déjà inclus), sans étape Node.
# La prod actuelle SmarterASP (self-contained + FTP) reste inchangée.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore en cache : copie des seuls fichiers de projet, puis du code.
COPY Wazap.slnx ./
COPY src/Wazap.API/Wazap.API.csproj src/Wazap.API/
COPY src/Wazap.Application/Wazap.Application.csproj src/Wazap.Application/
COPY src/Wazap.Domain/Wazap.Domain.csproj src/Wazap.Domain/
COPY src/Wazap.Infrastructure/Wazap.Infrastructure.csproj src/Wazap.Infrastructure/
RUN dotnet restore src/Wazap.API/Wazap.API.csproj

COPY src/ src/
RUN dotnet publish src/Wazap.API/Wazap.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Ne PAS activer l'auto-migration ici : la migration est appliquée à l'étape de déploiement
# (dotnet ef database update) ou au démarrage selon la stratégie choisie (voir docs/PAAS_DEPLOYMENT.md).
ENTRYPOINT ["dotnet", "Wazap.API.dll"]

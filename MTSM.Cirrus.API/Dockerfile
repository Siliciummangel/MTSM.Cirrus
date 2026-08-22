FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MTSM.Cirrus.API/MTSM.Cirrus.API.csproj", "MTSM.Cirrus.API/"]
COPY ["MTSM.Cirrus.Core/MTSM.Cirrus.Core.csproj", "MTSM.Cirrus.Core/"]
RUN dotnet restore "./MTSM.Cirrus.API/MTSM.Cirrus.API.csproj"
COPY . .
WORKDIR "/src/MTSM.Cirrus.API"
RUN dotnet build "./MTSM.Cirrus.API.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION
ARG VCS_REF=unknown
RUN test -n "$VERSION" && dotnet publish "./MTSM.Cirrus.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=$VERSION /p:SourceRevisionId=$VCS_REF --no-restore

FROM base AS final
ARG VERSION
ARG VCS_REF=unknown
WORKDIR /app
COPY --from=publish /app/publish .
LABEL org.opencontainers.image.title="MTSM Cirrus API" \
      org.opencontainers.image.description="HTTP API for MTSM Cirrus archival services" \
      org.opencontainers.image.source="https://github.com/Siliciummangel/MTSM.Cirrus" \
      org.opencontainers.image.revision="$VCS_REF" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.licenses="MIT"
ENTRYPOINT ["dotnet", "MTSM.Cirrus.API.dll"]

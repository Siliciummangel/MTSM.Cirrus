FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MTSM.Cirrus.Migration/MTSM.Cirrus.Migration.csproj", "MTSM.Cirrus.Migration/"]
COPY ["MTSM.Cirrus.Core/MTSM.Cirrus.Core.csproj", "MTSM.Cirrus.Core/"]
RUN dotnet restore "./MTSM.Cirrus.Migration/MTSM.Cirrus.Migration.csproj"
COPY . .
WORKDIR "/src/MTSM.Cirrus.Migration"
RUN dotnet build "./MTSM.Cirrus.Migration.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MTSM.Cirrus.Migration.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MTSM.Cirrus.Migration.dll"]

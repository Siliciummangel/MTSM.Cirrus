FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MTSM.Cirrus.Worker/MTSM.Cirrus.Worker.csproj", "MTSM.Cirrus.Worker/"]
COPY ["MTSM.Cirrus.Core/MTSM.Cirrus.Core.csproj", "MTSM.Cirrus.Core/"]
RUN dotnet restore "./MTSM.Cirrus.Worker/MTSM.Cirrus.Worker.csproj"
COPY . .
WORKDIR "/src/MTSM.Cirrus.Worker"
RUN dotnet build "./MTSM.Cirrus.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MTSM.Cirrus.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MTSM.Cirrus.Worker.dll"]

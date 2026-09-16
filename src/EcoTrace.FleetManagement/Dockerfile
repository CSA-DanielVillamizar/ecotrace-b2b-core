FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copiar csproj y restaurar
COPY ["API/EcoTrace.FleetManagement.API.csproj", "API/"]
COPY ["Domain/EcoTrace.FleetManagement.Domain.csproj", "Domain/"]
COPY ["Infrastructure/EcoTrace.FleetManagement.Infrastructure.csproj", "Infrastructure/"]
RUN dotnet restore "API/EcoTrace.FleetManagement.API.csproj"

# Copiar todo y compilar
COPY . .
WORKDIR "/app/API"
RUN dotnet publish "EcoTrace.FleetManagement.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EcoTrace.FleetManagement.API.dll"]

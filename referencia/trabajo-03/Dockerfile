# syntax=docker/dockerfile:1
#
# Un solo Dockerfile para los cuatro servicios y la consola. Cada uno se construye con sus
# propios argumentos (ver docker-compose.yml). Cada imagen compila solo lo que necesita:
# `dotnet publish` sigue las referencias del proyecto elegido y nada mas.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Estos archivos primero: si no cambian, la capa de restauracion se reutiliza.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src ./src

ARG PROYECTO
RUN dotnet publish "$PROYECTO" --configuration Release --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ARG DLL
ENV APP_DLL=$DLL \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app .

# La base SQLite vive en App_Data; en compose se monta como volumen para que sobreviva al contenedor.
RUN mkdir -p /app/App_Data && chown -R $APP_UID /app/App_Data
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]

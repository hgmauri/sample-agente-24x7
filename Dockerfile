# Imagem do agente. Build a partir da raiz do repositório:
#   docker build -t vigia-agente .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Vigia.Agente/Vigia.Agente.csproj src/Vigia.Agente/
RUN dotnet restore src/Vigia.Agente/Vigia.Agente.csproj
COPY src/Vigia.Agente/ src/Vigia.Agente/
RUN dotnet publish src/Vigia.Agente/Vigia.Agente.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Vigia.Agente.dll"]

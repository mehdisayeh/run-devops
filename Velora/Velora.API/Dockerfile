FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Velora.API/Velora.API.csproj Velora.API/
RUN dotnet restore Velora.API/Velora.API.csproj
COPY Velora.API/ Velora.API/
RUN dotnet publish Velora.API/Velora.API.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
USER $APP_UID
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Velora.API.dll"]

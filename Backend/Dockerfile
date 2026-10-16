# Backend image for the UAT stack (docker-compose.yml). Two targets: `migrate` (applies the EF migrations once, the backend does not
# migrate on start) and `runtime` (the API).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CommonService.csproj ./
RUN dotnet restore CommonService.csproj
COPY . .
RUN dotnet publish CommonService.csproj -c Release -o /app/publish --no-restore

FROM build AS migrate
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --version 10.0.0
RUN dotnet build CommonService.csproj -c Release --no-restore
# CONNECTION is the SQL Server connection string; the compose file passes it.
ENTRYPOINT ["sh", "-c", "dotnet ef database update --no-build --configuration Release --project CommonService.csproj --connection \"$CONNECTION\""]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5004
EXPOSE 5004
ENTRYPOINT ["dotnet", "CommonService.dll"]

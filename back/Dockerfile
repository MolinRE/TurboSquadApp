FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY TurboSquadApp.csproj ./
RUN dotnet restore TurboSquadApp.csproj
COPY . ./
RUN dotnet publish TurboSquadApp.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "TurboSquadApp.dll"]

# Сборка
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props GhostLetters.sln ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore GhostLetters.sln
RUN dotnet publish src/GhostLetters.Api/GhostLetters.Api.csproj -c Release -o /app --no-restore

# Запуск
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GhostLetters.Api.dll"]

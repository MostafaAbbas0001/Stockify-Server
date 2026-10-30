# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached until the project file changes).
COPY ["Directory.Build.props", "Stockify.csproj", "./"]
RUN dotnet restore "Stockify.csproj"

# .dockerignore keeps build output and local settings out of the context.
COPY . .

RUN dotnet publish "Stockify.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

# Bundle the EF Core migrations so the container can update the database on start.
RUN dotnet tool install --global dotnet-ef --version 10.0.14&& \
    ConnectionStrings__DefaultConnection="Host=localhost;Database=build;Username=build;Password=build" \
    JwtSettings__Key="build-only-not-a-deployment-secret" \
    /root/.dotnet/tools/dotnet-ef migrations bundle \
    --project Stockify.csproj \
    --configuration Release \
    --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

COPY --from=build /app/publish .
COPY --from=build --chmod=755 /app/efbundle ./efbundle

ENTRYPOINT ["sh", "-c", "./efbundle --connection \"$ConnectionStrings__DefaultConnection\" && exec dotnet Stockify.dll"]

# Filo API container.
# Build from the repository root:
#   docker build -f Filo.Api/Dockerfile -t filo-api Filo.Api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Filo.Api.csproj ./
RUN dotnet restore Filo.Api.csproj

COPY . ./
RUN dotnet publish Filo.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Data Protection keys (sign-in tokens) are kept here; mount a volume so
# they survive redeploys.
RUN mkdir -p /keys && chown $APP_UID /keys

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/keys

# Runs as the image's built-in non-root user.
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "Filo.Api.dll"]

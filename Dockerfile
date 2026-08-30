# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project file first so restore is cached until dependencies change.
COPY src/HoneyBee.Web/HoneyBee.Web.csproj src/HoneyBee.Web/
RUN dotnet restore src/HoneyBee.Web/HoneyBee.Web.csproj

COPY . .
RUN dotnet publish src/HoneyBee.Web/HoneyBee.Web.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Most container hosts route to $PORT; 8080 is the usual default.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Runs as a non-root user, so a flaw in the app is not a flaw in the host.
USER $APP_UID

ENTRYPOINT ["dotnet", "HoneyBee.Web.dll"]

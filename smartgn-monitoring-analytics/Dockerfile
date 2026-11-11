# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY com.ventrax.monitoring/com.ventrax.monitoring.csproj com.ventrax.monitoring/
RUN dotnet restore com.ventrax.monitoring/com.ventrax.monitoring.csproj
COPY com.ventrax.monitoring/ com.ventrax.monitoring/
RUN dotnet publish com.ventrax.monitoring/com.ventrax.monitoring.csproj -c Release -o /app --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8086
EXPOSE 8086
USER $APP_UID
ENTRYPOINT ["dotnet", "com.ventrax.monitoring.dll"]

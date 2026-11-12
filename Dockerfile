# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Smartgn.Monitoring/Smartgn.Monitoring.csproj src/Smartgn.Monitoring/
RUN dotnet restore src/Smartgn.Monitoring/Smartgn.Monitoring.csproj
COPY src/Smartgn.Monitoring/ src/Smartgn.Monitoring/
RUN dotnet publish src/Smartgn.Monitoring/Smartgn.Monitoring.csproj -c Release -o /app --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY tools/health.sh /app/health.sh
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8084
EXPOSE 8084
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s CMD ["bash", "/app/health.sh"]
ENTRYPOINT ["dotnet", "Smartgn.Monitoring.dll"]

# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props HomeShowingScheduler.slnx ./
COPY src/ src/
RUN dotnet publish src/Api/Api.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "Api.dll"]

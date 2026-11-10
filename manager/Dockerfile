FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish manager.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENV MANAGER_DATA=/data
ENV ASPNETCORE_URLS=http://+:8080
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "manager.dll"]
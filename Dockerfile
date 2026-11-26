FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish roomhost/roomhost.csproj -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENV ROOM_DATA=/data
ENV ROOM_ADMIN_URL=http://0.0.0.0:8081
VOLUME /data
EXPOSE 5009/tcp 5009/udp
ENTRYPOINT ["dotnet", "roomhost.dll"]
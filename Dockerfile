FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish roomhost/roomhost.csproj -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app .
ENV ROOM_DATA=/data
VOLUME /data
EXPOSE 5009/tcp 5009/udp
ENTRYPOINT ["dotnet", "roomhost.dll"]
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/MyVPN.Domain/MyVPN.Domain.csproj backend/MyVPN.Domain/
COPY backend/MyVPN.Application/MyVPN.Application.csproj backend/MyVPN.Application/
COPY backend/MyVPN.Infrastructure/MyVPN.Infrastructure.csproj backend/MyVPN.Infrastructure/
COPY backend/MyVPN.Api/MyVPN.Api.csproj backend/MyVPN.Api/
RUN dotnet restore backend/MyVPN.Api/MyVPN.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/MyVPN.Api/MyVPN.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MyVPN.Api.dll"]

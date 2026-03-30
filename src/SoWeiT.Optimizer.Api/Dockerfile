# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/SoWeiT.Optimizer.Api/SoWeiT.Optimizer.Api.csproj", "src/SoWeiT.Optimizer.Api/"]
COPY ["src/SoWeiT.Optimizer.Service/SoWeiT.Optimizer.Service.csproj", "src/SoWeiT.Optimizer.Service/"]
COPY ["src/SoWeiT.Optimizer.History.Contracts/SoWeiT.Optimizer.History.Contracts.csproj", "src/SoWeiT.Optimizer.History.Contracts/"]
COPY ["src/SoWeiT.Optimizer/SoWeiT.Optimizer.csproj", "src/SoWeiT.Optimizer/"]
COPY ["src/SoWeiT.Optimizer.Persistence.Redis/SoWeiT.Optimizer.Persistence.Redis.csproj", "src/SoWeiT.Optimizer.Persistence.Redis/"]
COPY ["src/SoWeiT.Optimizer.Messaging.RabbitMq/SoWeiT.Optimizer.Messaging.RabbitMq.csproj", "src/SoWeiT.Optimizer.Messaging.RabbitMq/"]
RUN dotnet restore "./src/SoWeiT.Optimizer.Api/SoWeiT.Optimizer.Api.csproj"
COPY . .
WORKDIR "/src/src/SoWeiT.Optimizer.Api"
RUN dotnet build "./SoWeiT.Optimizer.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SoWeiT.Optimizer.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:PublishReadyToRun=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SoWeiT.Optimizer.Api.dll"]

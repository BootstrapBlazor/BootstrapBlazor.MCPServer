FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["BootstrapBlazor.McpServer.csproj", "./"]
RUN dotnet restore "BootstrapBlazor.McpServer.csproj"
COPY . .
RUN dotnet publish "BootstrapBlazor.McpServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# We use SDK as the final image because GitSyncBackgroundService triggers "dotnet build" internally to extract the XML.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS final
WORKDIR /app

# Install git explicitly
RUN apt-get update \
    && apt-get install -y git \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Set Default Configuration via Environment Variables
ENV GitSync__RepositoryUrl="https://gitee.com/LongbowEnterprise/BootstrapBlazor.git"
ENV GitSync__CronSchedule="0 3 * * *"
ENV GitSync__OutputDir="/app/data/OutputRAG"
ENV ASPNETCORE_URLS="http://+:5251"
EXPOSE 5251

ENTRYPOINT ["dotnet", "BootstrapBlazor.McpServer.dll"]

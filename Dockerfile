# Stage base
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Stage build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copia apenas os csproj para cache eficiente
COPY FluxoCaixaApi/FluxoCaixaApi.csproj FluxoCaixaApi/
COPY CommandStore/CommandStore.csproj CommandStore/
COPY QueryStore/QueryStore.csproj QueryStore/
COPY Store/Store.csproj Store/
COPY FluxoCaixa/FluxoCaixa.csproj FluxoCaixa/
COPY Command/Command.csproj Command/
COPY Contract/Contract.csproj Contract/
COPY Query/Query.csproj Query/
COPY Integration/Integration.csproj Integration/
COPY Enumeration/Enumeration.csproj Enumeration/
COPY TestesUnitarios/TestesUnitarios.csproj TestesUnitarios/

RUN dotnet restore FluxoCaixaApi/FluxoCaixaApi.csproj

# Copia todo o código
COPY . .

WORKDIR /src/FluxoCaixaApi
RUN dotnet build FluxoCaixaApi.csproj -c $BUILD_CONFIGURATION -o /app/build

# Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish FluxoCaixaApi.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage final - USANDO SDK para ter dotnet-ef
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS final
WORKDIR /app

# Instala o dotnet-ef globalmente
RUN dotnet tool install --global dotnet-ef
ENV PATH="${PATH}:/root/.dotnet/tools"

# Copia os arquivos publicados
COPY --from=publish /app/publish .

# Copia os arquivos fonte necessários para migrations
COPY --from=build /src /src

# Copia o cache do NuGet para o stage final
COPY --from=build /root/.nuget /root/.nuget

ENTRYPOINT ["dotnet", "FluxoCaixaApi.dll"]
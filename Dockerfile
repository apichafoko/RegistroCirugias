# Usar la imagen oficial de .NET 9.0
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Usar la imagen de SDK para build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copiar el archivo de proyecto y restaurar dependencias
COPY ["RegistroCx.csproj", "./"]
RUN dotnet restore "RegistroCx.csproj"

# Copiar el resto del código y compilar
COPY . .
RUN dotnet build "RegistroCx.csproj" -c Release -o /app/build

# Publicar la aplicación
FROM build AS publish
RUN dotnet publish "RegistroCx.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Imagen final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Comando de entrada
ENTRYPOINT ["dotnet", "RegistroCx.dll"]
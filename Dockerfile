# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY FarmaciaSalacor.sln ./
COPY FarmaciaSalacor.Web/FarmaciaSalacor.Web.csproj ./FarmaciaSalacor.Web/

RUN dotnet restore ./FarmaciaSalacor.sln

# Copy the rest of the source
COPY . .

RUN dotnet publish ./FarmaciaSalacor.Web/FarmaciaSalacor.Web.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Railway sets PORT at runtime; Program.cs binds to it.
# No EXPOSE needed, but it doesn't hurt for local usage.
EXPOSE 8080

ENTRYPOINT ["dotnet", "FarmaciaSalacor.Web.dll"]

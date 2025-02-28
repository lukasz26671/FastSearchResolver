# Stage 1: Build and Publish (Native AOT)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /app

# Install required AOT dependencies (clang for linking)
RUN apt-get update && apt-get install -y clang zlib1g-dev

# Copy project files
COPY . ./

# Restore dependencies
RUN dotnet restore

# Publish using Native AOT with trimming and self-contained binary
RUN dotnet publish -c Release -o /out --self-contained true \
    -p:PublishAot=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishSingleFile=true

# Stage 2: Runtime (Minimal, Optimized for Performance)
FROM debian:bookworm-slim AS runtime

WORKDIR /app

# Copy the published output from the build stage
COPY --from=build /out .

# Set executable permissions for the AOT binary
RUN chmod +x FastSearchResolver

# Expose the default application ports (configurable via environment variables)
EXPOSE 5000 5001 8080

# Set ASP.NET Core environment variables to use configuration file
ENV ASPNETCORE_URLS=http://+:8080

# Run the application
CMD ["./FastSearchResolver"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY publish/ .
RUN chmod +x /app/VANWebService
ENV ASPNETCORE_URLS=http://0.0.0.0:38031
EXPOSE 38031
ENTRYPOINT ["./VANWebService", "port=38031"]

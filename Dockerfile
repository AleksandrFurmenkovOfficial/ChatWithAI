FROM ubuntu:22.04

EXPOSE 443

RUN apt-get update && \
    apt-get install -y ca-certificates && \
    update-ca-certificates
RUN update-ca-certificates
RUN apt-get update
RUN apt-get install -y dotnet-sdk-8.0

COPY . /ChatWithAI
WORKDIR /ChatWithAI
ENTRYPOINT ["dotnet", "ChatWithAI.dll"]

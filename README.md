# Observability & Monitoring - eShop Microservices 

This document provides step-by-step instructions on how run the *eShop microservices* environment with OpenTelemetry integration with *traces, **metrics, and **logs*.

---

## How to run the eShop Environment

There is the need to have *Docker* and *Docker Compose* installed

### Steps to Run
1. *Clone the repository:*
   sh
   git clone https://github.com/caridade1706/eShop_AS_02_108307
   cd eShop_AS_02_108307
   
2. *Start the environment using Docker Compose:*

    Make sure you are in the root folder where docker-compose.yml is located.

   sh
   docker-compose up --build
   
   This will start OTel Collector, Prometheus, Jaeger, and Grafana services.

3. *Run the application*

    You can run the application via terminal being in the root folder.

   sh
   dotnet run --project .\src\eShop.AppHost\eShop.AppHost.csproj
   
   
   Or running by the Visual Studio App.
---

## Jaeger - Traces
- Open Jaeger UI at: [http://localhost:16686](http://localhost:16686)
- Select the service (e.g., basket-service) from the dropdown.
- Click *Find Traces* to visualize requests and spans.

---

## Prometheus -  Metrics
- Open Prometheus at: [http://localhost:9090](http://localhost:9090)
- Use the *Graph* or *Table* tab to explore metrics such as basket_items_added, checkout_orders_count, etc.

---

## Viewing Metrics and Traces in Grafana
- Open Grafana at: [http://localhost:3000](http://localhost:3000)
- Default credentials: *admin / admin*
- In the *Dashboard* section there will be dashboards preconfigured to show the metrics and traces implemented.

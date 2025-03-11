using System.Diagnostics.CodeAnalysis;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.Extensions;
using eShop.Basket.API.Model;
using System.Diagnostics.Metrics;
using System.Net.Http;
using System.Text.Json;
using Grpc.Core;

namespace eShop.Basket.API.Grpc;

public class BasketService(
    IBasketRepository repository,
    ILogger<BasketService> logger,
    Meter _meter) : Basket.BasketBase
{
    private readonly Counter<long> _addToCartCounter = _meter.CreateCounter<long>("basket_add_to_cart_count", description: "Número total de produtos adicionados ao carrinho.");
    private readonly Counter<long> _removeFromCartCounter = _meter.CreateCounter<long>("basket_remove_from_cart_count", description: "Número total de produtos removidos do carrinho.");
    private readonly ObservableGauge<long> _basketTotalItemsGauge = _meter.CreateObservableGauge("basket_total_items",
        () => _cartTotal,
        "Quantidade total de itens no carrinho.");

    private readonly ObservableGauge<double> _viewToCartConversionRateGauge = _meter.CreateObservableGauge("view_to_cart_conversion_rate",
        () => _lastCalculatedConversionRate, // ✅ Agora lê um valor pré-calculado
        "Taxa de conversão de visualizações para adição ao carrinho.");

    private static long _cartTotal = 0;
    private static long _totalViews = 1; // 🔹 Evita divisão por zero
    private static long _totalAdds = 0;
    private static double _lastCalculatedConversionRate = 0; // 🔥 Armazena a última taxa calculada

    // 🔹 Executa a atualização de `_totalViews` em segundo plano
    static BasketService()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                await UpdateTotalViews();
                _lastCalculatedConversionRate = CalculateConversionRate(); // 🔥 Atualiza a taxa periodicamente
                await Task.Delay(TimeSpan.FromMinutes(1)); // 🔄 Atualiza a cada 1 minuto
            }
        });
    }

    private static async Task UpdateTotalViews()
    {
        using var httpClient = new HttpClient();
        try
        {
            var response = await httpClient.GetStringAsync("http://catalog-api:5000/metrics/views");
            if (long.TryParse(response, out long views))
            {
                _totalViews = views;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao buscar visualizações do Catalog.API: {ex.Message}");
        }
    }

    private static double CalculateConversionRate()
    {
        if (_totalViews == 0) return 0; // ✅ Evita divisão por zero
        return (_totalAdds / (double)_totalViews) * 100;
    }

    [AllowAnonymous]
    public override async Task<CustomerBasketResponse> GetBasket(GetBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            return new();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin GetBasketById call from method {Method} for basket id {Id}", context.Method, userId);
        }

        var data = await repository.GetBasketAsync(userId);

        if (data is not null)
        {
            return MapToCustomerBasketResponse(data);
        }

        return new();
    }

    public override async Task<CustomerBasketResponse> UpdateBasket(UpdateBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        var existingBasket = await repository.GetBasketAsync(userId);
        var customerBasket = MapToCustomerBasket(userId, request);
        var response = await repository.UpdateBasketAsync(customerBasket);
        if (response is null)
        {
            ThrowBasketDoesNotExist(userId);
        }

        long itemsAdded = 0;
        long itemsRemoved = 0;

        // 🔹 Comparação de quantidade para produtos que ainda existem no carrinho
        foreach (var newItem in request.Items)
        {
            var oldItem = existingBasket?.Items.FirstOrDefault(i => i.ProductId == newItem.ProductId);
            long previousQuantity = oldItem?.Quantity ?? 0;
            long difference = newItem.Quantity - previousQuantity;

            if (difference > 0)
            {
                itemsAdded += difference;
            }
            else if (difference < 0)
            {
                itemsRemoved += Math.Abs(difference);
            }
        }

        // 🔹 Verificar produtos que foram completamente removidos (quantidade foi para zero)
        if (existingBasket != null)
        {
            foreach (var oldItem in existingBasket.Items)
            {
                bool stillExists = request.Items.Any(i => i.ProductId == oldItem.ProductId);
                if (!stillExists)
                {
                    itemsRemoved += oldItem.Quantity;
                }
            }
        }

        if (itemsAdded > 0)
        {
            _addToCartCounter.Add(itemsAdded);
            _totalAdds += itemsAdded;
            logger.LogInformation("AddToCartCounter incrementado por {itemsAdded}", itemsAdded);
        }

        if (itemsRemoved > 0)
        {
            _removeFromCartCounter.Add(itemsRemoved);
            logger.LogInformation("RemoveFromCartCounter incrementado por {itemsRemoved}", itemsRemoved);
        }

        // 🔹 Atualiza o total do carrinho
        _cartTotal = response.Items.Sum(i => i.Quantity);

        return MapToCustomerBasketResponse(response);
    }

    public override async Task<DeleteBasketResponse> DeleteBasket(DeleteBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        var existingBasket = await repository.GetBasketAsync(userId);
        if (existingBasket != null)
        {
            long itemsRemoved = existingBasket.Items.Sum(i => i.Quantity);
            _removeFromCartCounter.Add(itemsRemoved);
            _cartTotal = 0; // 🔥 Zerar o carrinho se ele for deletado
            logger.LogInformation("Carrinho deletado. RemoveFromCartCounter incrementado por {itemsRemoved}", itemsRemoved);
        }

        await repository.DeleteBasketAsync(userId);
        return new();
    }

    [DoesNotReturn]
    private static void ThrowNotAuthenticated() => throw new RpcException(new Status(StatusCode.Unauthenticated, "The caller is not authenticated."));

    [DoesNotReturn]
    private static void ThrowBasketDoesNotExist(string userId) => throw new RpcException(new Status(StatusCode.NotFound, $"Basket with buyer id {userId} does not exist"));

    private static CustomerBasketResponse MapToCustomerBasketResponse(CustomerBasket customerBasket)
    {
        var response = new CustomerBasketResponse();

        foreach (var item in customerBasket.Items)
        {
            response.Items.Add(new BasketItem()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }

        return response;
    }

    private static CustomerBasket MapToCustomerBasket(string userId, UpdateBasketRequest customerBasketRequest)
    {
        var response = new CustomerBasket
        {
            BuyerId = userId
        };

        foreach (var item in customerBasketRequest.Items)
        {
            response.Items.Add(new()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }

        return response;
    }
}

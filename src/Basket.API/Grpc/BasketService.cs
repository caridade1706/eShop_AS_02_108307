using System.Diagnostics.CodeAnalysis;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.Extensions;
using eShop.Basket.API.Model;
using System.Diagnostics.Metrics;
using System.Net.Http;
using System.Text.Json;
using Grpc.Core;
using System.Diagnostics;

namespace eShop.Basket.API.Grpc;

public class BasketService(
    IBasketRepository repository,
    ILogger<BasketService> logger,
    Meter _meter) : Basket.BasketBase
{
    private static readonly ActivitySource ActivitySource = new("Basket.API");
    private readonly Counter<long> _addToCartCounter = _meter.CreateCounter<long>("basket_add_to_cart_count", description: "Número total de produtos adicionados ao carrinho.");
    private readonly Counter<long> _removeFromCartCounter = _meter.CreateCounter<long>("basket_remove_from_cart_count", description: "Número total de produtos removidos do carrinho.");
    private readonly ObservableGauge<long> _basketTotalItemsGauge = _meter.CreateObservableGauge("basket_total_items",
        () => _cartTotal,
        "Quantidade total de itens no carrinho.");

    private static long _cartTotal = 0;
    private static long _totalAdds = 0;

    // 🔹 Método público para atualizar a taxa de conversão (chamado externamente)
   

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
        using var activity = ActivitySource.StartActivity("AddToCart", ActivityKind.Server);
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        activity?.SetTag("user.id", userId);

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
                activity?.SetTag($"cart.product_added.{newItem.ProductId}", newItem.Quantity);
            }
            else if (difference < 0)
            {
                itemsRemoved += Math.Abs(difference);
                activity?.SetTag($"cart.product_removed.{newItem.ProductId}", Math.Abs(difference));
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
                    activity?.SetTag($"cart.product_removed.{oldItem.ProductId}", oldItem.Quantity);
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

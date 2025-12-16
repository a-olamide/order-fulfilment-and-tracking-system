namespace OrderService.Domain
{
    public enum OrderStatus
    {
        Created = 0,
        PaymentAuthorized = 1,
        PaymentFailed = 2,
        FulfillmentScheduled = 3
    }
}

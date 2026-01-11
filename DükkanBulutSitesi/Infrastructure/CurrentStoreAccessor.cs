namespace DükkanBulutSitesi.Infrastructure
{
    public sealed class CurrentStoreAccessor
    {
        private readonly IHttpContextAccessor _http;
        public CurrentStoreAccessor(IHttpContextAccessor http) => _http = http;

        public int CurrentStoreId =>
            _http.HttpContext?.Items.TryGetValue("CurrentStoreId", out var v) == true && v is int g ? g : 0;

        public void ForceSet(int storeId)
        {
            var ctx = _http.HttpContext;
            if (ctx == null) return;

            ctx.Items["CurrentStoreId"] = storeId;
        }
    }
}
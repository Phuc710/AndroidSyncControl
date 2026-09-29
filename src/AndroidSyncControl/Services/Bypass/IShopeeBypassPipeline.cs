using System;
using System.Threading;
using System.Threading.Tasks;

namespace AndroidSyncControl.Services.Bypass
{
    /// <summary>
    /// Contract for the Shopee Risk Engine bypass pipeline.
    /// </summary>
    public interface IShopeeBypassPipeline
    {
        Task<bool> ExecuteBypassAsync(string deviceId, Action<string>? statusCallback = null, CancellationToken ct = default);
    }
}

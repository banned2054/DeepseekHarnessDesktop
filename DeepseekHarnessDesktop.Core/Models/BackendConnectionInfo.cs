namespace DeepseekHarnessDesktop.Core.Models;

/// <summary>后端就绪后上报的连接信息；AuthenticatedUri 携带一次性启动令牌。</summary>
public sealed record BackendConnectionInfo(Uri AuthenticatedUri)
{
    /// <summary>去掉令牌查询参数的 API 根地址。</summary>
    public Uri BaseUrl
    {
        get
        {
            var builder = new UriBuilder(AuthenticatedUri) { Query = null, Path = "/" };
            return builder.Uri;
        }
    }
}

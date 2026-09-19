using System.Threading.Tasks;

namespace UsagePeek
{
    internal interface IUsageProvider
    {
        string Id { get; }
        string DisplayName { get; }
        Task<UsageSnapshot> FetchAsync();
    }
}
